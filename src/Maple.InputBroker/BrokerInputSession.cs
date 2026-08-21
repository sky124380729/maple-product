using Maple.Core.Configuration;
using Maple.Core.Broker;

namespace Maple.InputBroker;

public sealed class BrokerInputSession(
    IBrokerKeySender sender,
    IBrokerClock clock,
    IBrokerTargetSafetyGate targetSafety,
    IMovementLeaseScheduler movementLeases,
    int heartbeatTimeoutMs) : IAsyncDisposable
{
    private const int MaximumMoveLeaseMs = 5_000;
    private readonly object sync = new();
    private readonly Dictionary<BrokerLogicalAction, ActiveKey> active = [];
    private bool armed;
    private bool disposed;
    private long lastHeartbeatMonoMs = clock.NowMonoMs;
    private long nextLeaseGeneration;
    private long lastSequence;
    private BrokerTargetIdentity? armedTarget;

    public IReadOnlyCollection<string> ActiveKeys
    {
        get
        {
            lock (sync) return active.Values.Select(item => item.Key).ToArray();
        }
    }

    public void Arm(BrokerTargetIdentity target, string handshakeSecret)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (target.Hwnd == 0 || target.ProcessId <= 0 || string.IsNullOrWhiteSpace(target.ProcessPath))
                throw new ArgumentException("A complete target identity is required.", nameof(target));
            if (string.IsNullOrWhiteSpace(handshakeSecret))
                throw new ArgumentException("A handshake secret is required.", nameof(handshakeSecret));
            ReleaseAll();
            armed = true;
            armedTarget = target;
            lastHeartbeatMonoMs = clock.NowMonoMs;
            lastSequence = 0;
        }
    }

    public Task<BrokerResponse> HandleAsync(BrokerRequest request)
    {
        lock (sync)
        {
            if (disposed) return Task.FromResult(Reject(request, "SESSION_DISPOSED"));
            if (request.ProtocolVersion != BrokerProtocol.Version)
                return Task.FromResult(RejectAndRelease(request, "PROTOCOL_VERSION_MISMATCH"));
            if (request.Sequence <= lastSequence)
                return Task.FromResult(RejectAndRelease(request, "SEQUENCE_INVALID"));
            lastSequence = request.Sequence;

            if (request.Kind == BrokerCommandKind.ReleaseAll)
                return Task.FromResult(ReleaseAllResponse(request));
            if (request.Kind == BrokerCommandKind.Close)
                return Task.FromResult(Close(request));
            if (!armed) return Task.FromResult(Reject(request, "TARGET_NOT_ARMED"));

            if (request.Kind == BrokerCommandKind.Heartbeat)
            {
                if (clock.NowMonoMs - lastHeartbeatMonoMs > heartbeatTimeoutMs)
                    return Task.FromResult(RejectAndDisarm(request, "HEARTBEAT_TIMEOUT"));
                lastHeartbeatMonoMs = clock.NowMonoMs;
                return Task.FromResult(Accept(request, "HEARTBEAT_OK"));
            }

            if (clock.NowMonoMs - lastHeartbeatMonoMs > heartbeatTimeoutMs)
                return Task.FromResult(RejectAndDisarm(request, "HEARTBEAT_TIMEOUT"));
            BrokerTargetSafetyResult targetResult = targetSafety.Evaluate(armedTarget!);
            if (!targetResult.Success)
                return Task.FromResult(RejectAndDisarm(request, targetResult.Code));

            return Task.FromResult(request.Kind switch
            {
                BrokerCommandKind.KeyDown => KeyDown(request),
                BrokerCommandKind.KeyUp => KeyUp(request),
                _ => RejectAndRelease(request, "COMMAND_UNSUPPORTED")
            });
        }
    }

    public Task CheckWatchdogAsync()
    {
        lock (sync)
        {
            if (disposed) return Task.CompletedTask;
            bool heartbeatExpired = armed && clock.NowMonoMs - lastHeartbeatMonoMs > heartbeatTimeoutMs;
            bool leaseExpired = active.Values.Any(item => clock.NowMonoMs > item.LeaseDeadlineMonoMs);
            bool targetInvalid = armedTarget is not null && !targetSafety.Evaluate(armedTarget).Success;
            if (leaseExpired)
                ReleaseAll();

            if (heartbeatExpired || targetInvalid)
            {
                ReleaseAll();
                armed = false;
                armedTarget = null;
            }
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (!disposed)
            {
                ReleaseAll();
                disposed = true;
            }
        }
        await movementLeases.DisposeAsync();
    }

    private BrokerResponse KeyDown(BrokerRequest request)
    {
        if (!TryValidateAction(request, out BrokerLogicalAction action, out string key))
            return RejectAndRelease(request, "INVALID_DURATION");

        if (!ReleaseOpposite(action)) return RejectAndRelease(request, "KEY_UP_FAILED");
        long generation = ++nextLeaseGeneration;
        if (active.TryGetValue(action, out ActiveKey? current))
        {
            if (!string.Equals(current.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                CancelMovementLease(action, current.Generation);
                if (!sender.Send(current.Key, isKeyUp: true))
                    return RejectAndRelease(request, "KEY_UP_FAILED");
                active.Remove(action);
                if (!sender.Send(key, isKeyUp: false)) return RejectAndRelease(request, "KEY_DOWN_FAILED");
            }
            long deadline = clock.NowMonoMs + request.LeaseMs;
            active[action] = new ActiveKey(key, deadline, generation);
            ScheduleMovementLease(action, generation, deadline);
            return Accept(request, "KEY_LEASE_REFRESHED");
        }

        if (!sender.Send(key, isKeyUp: false)) return RejectAndRelease(request, "KEY_DOWN_FAILED");
        long physicalDeadline = clock.NowMonoMs + request.LeaseMs;
        active[action] = new ActiveKey(key, physicalDeadline, generation);
        ScheduleMovementLease(action, generation, physicalDeadline);
        return Accept(request, "KEY_DOWN_SENT");
    }

    private BrokerResponse KeyUp(BrokerRequest request)
    {
        if (request.Action is not { } action || string.IsNullOrWhiteSpace(request.Key))
            return RejectAndRelease(request, "ACTION_REQUIRED");
        if (!active.TryGetValue(action, out ActiveKey? current))
            return Accept(request, "KEY_ALREADY_UP");
        CancelMovementLease(action, current.Generation);
        bool success = sender.Send(current.Key, isKeyUp: true);
        if (success) active.Remove(action);
        return success ? Accept(request, "KEY_UP_SENT") : RejectAndRelease(request, "KEY_UP_FAILED");
    }

    private bool TryValidateAction(BrokerRequest request, out BrokerLogicalAction action, out string key)
    {
        action = request.Action ?? default;
        key = request.Key ?? string.Empty;
        if (request.Action is null || string.IsNullOrWhiteSpace(key) || request.LeaseMs <= 0) return false;
        int maximum = action == BrokerLogicalAction.Attack
            ? StationaryAttackConfig.AttackDurationLimitMs
            : MaximumMoveLeaseMs;
        if (request.LeaseMs > maximum) return false;
        return action switch
        {
            BrokerLogicalAction.Attack => StationaryAttackConfig.AllowedAttackKeys.Contains(key),
            BrokerLogicalAction.MoveLeft => key.Equals("Left", StringComparison.OrdinalIgnoreCase),
            BrokerLogicalAction.MoveRight => key.Equals("Right", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private bool ReleaseOpposite(BrokerLogicalAction action)
    {
        BrokerLogicalAction? opposite = action switch
        {
            BrokerLogicalAction.MoveLeft => BrokerLogicalAction.MoveRight,
            BrokerLogicalAction.MoveRight => BrokerLogicalAction.MoveLeft,
            _ => null
        };
        if (opposite is not { } value || !active.TryGetValue(value, out ActiveKey? key)) return true;
        CancelMovementLease(value, key.Generation);
        if (!sender.Send(key.Key, isKeyUp: true)) return false;
        active.Remove(value);
        return true;
    }

    private BrokerResponse ReleaseAllResponse(BrokerRequest request) =>
        ReleaseAll() ? Accept(request, "ALL_KEYS_RELEASED") : Reject(request, "RELEASE_FAILED");

    private BrokerResponse Close(BrokerRequest request)
    {
        bool released = ReleaseAll();
        armed = false;
        armedTarget = null;
        return released ? Accept(request, "CLOSED") : Reject(request, "RELEASE_FAILED");
    }

    private bool ReleaseAll()
    {
        movementLeases.CancelAll();
        bool success = true;
        foreach ((BrokerLogicalAction action, ActiveKey key) in active.ToArray())
        {
            if (sender.Send(key.Key, isKeyUp: true)) active.Remove(action);
            else success = false;
        }
        return success;
    }

    private void ScheduleMovementLease(BrokerLogicalAction action, long generation, long deadlineMonoMs)
    {
        if (action is BrokerLogicalAction.MoveLeft or BrokerLogicalAction.MoveRight)
            movementLeases.Schedule(action, generation, deadlineMonoMs, OnMovementLeaseExpired);
    }

    private void CancelMovementLease(BrokerLogicalAction action, long generation)
    {
        if (action is BrokerLogicalAction.MoveLeft or BrokerLogicalAction.MoveRight)
            movementLeases.Cancel(action, generation);
    }

    private void OnMovementLeaseExpired(BrokerLogicalAction action, long generation)
    {
        lock (sync)
        {
            if (disposed || !active.TryGetValue(action, out ActiveKey? current) || current.Generation != generation)
                return;
            if (sender.Send(current.Key, isKeyUp: true)) active.Remove(action);
        }
    }

    private BrokerResponse RejectAndRelease(BrokerRequest request, string code)
    {
        ReleaseAll();
        return Reject(request, code);
    }

    private BrokerResponse RejectAndDisarm(BrokerRequest request, string code)
    {
        ReleaseAll();
        armed = false;
        armedTarget = null;
        return Reject(request, code);
    }

    private static BrokerResponse Accept(BrokerRequest request, string code) =>
        new(BrokerProtocol.Version, request.Sequence, true, code);

    private static BrokerResponse Reject(BrokerRequest request, string code) =>
        new(BrokerProtocol.Version, request.Sequence, false, code);

    private sealed record ActiveKey(string Key, long LeaseDeadlineMonoMs, long Generation);
}
