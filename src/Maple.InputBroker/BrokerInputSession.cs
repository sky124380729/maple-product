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
    private readonly Dictionary<BrokerLogicalAction, LeaseCompletion> completedLeases = [];
    private bool armed;
    private bool disposed;
    private long lastHeartbeatMonoMs = clock.NowMonoMs;
    private long nextLeaseGeneration;
    private long lastSequence;
    private BrokerTargetIdentity? armedTarget;

    public BrokerInputSession(
        IBrokerKeySender sender,
        IBrokerClock clock,
        IBrokerTargetSafetyGate targetSafety,
        int heartbeatTimeoutMs)
        : this(sender, clock, targetSafety, new NoopMovementLeaseScheduler(), heartbeatTimeoutMs)
    {
    }

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
            bool leaseExpired = active.Any(item =>
                !IsMovement(item.Key) && clock.NowMonoMs > item.Value.LeaseDeadlineMonoMs);
            bool targetInvalid = armedTarget is not null && !targetSafety.Evaluate(armedTarget).Success;
            if (leaseExpired)
                ReleaseAll(preserveMovementTiming: true);

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
        completedLeases.Remove(action);
        if (active.TryGetValue(action, out ActiveKey? current))
        {
            if (!string.Equals(current.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                CancelMovementLease(action, current.Generation);
                if (!sender.Send(current.Key, isKeyUp: true))
                    return RejectAndRelease(request, "KEY_UP_FAILED");
                active.Remove(action);
                if (!sender.Send(key, isKeyUp: false)) return RejectAndRelease(request, "KEY_DOWN_FAILED");
                long replacementPressedAt = clock.NowMonoMs;
                long replacementDeadline = checked(replacementPressedAt + request.LeaseMs);
                active[action] = new ActiveKey(
                    key,
                    replacementPressedAt,
                    replacementDeadline,
                    request.LeaseMs,
                    generation,
                    request.MovementReleaseMode);
                ScheduleMovementLease(action, generation, replacementDeadline, request.MovementReleaseMode);
                return Accept(request, "KEY_LEASE_REFRESHED");
            }
            CancelMovementLease(action, current.Generation);
            long refreshedAt = clock.NowMonoMs;
            long refreshedDeadline = checked(refreshedAt + request.LeaseMs);
            int requestedFromPhysicalDown = checked((int)(refreshedDeadline - current.PressedAtMonoMs));
            active[action] = current with
            {
                LeaseDeadlineMonoMs = refreshedDeadline,
                RequestedLeaseMs = requestedFromPhysicalDown,
                Generation = generation,
                MovementReleaseMode = request.MovementReleaseMode
            };
            ScheduleMovementLease(action, generation, refreshedDeadline, request.MovementReleaseMode);
            return Accept(request, "KEY_LEASE_REFRESHED");
        }

        if (!sender.Send(key, isKeyUp: false)) return RejectAndRelease(request, "KEY_DOWN_FAILED");
        long pressedAt = clock.NowMonoMs;
        long deadline = checked(pressedAt + request.LeaseMs);
        active[action] = new ActiveKey(
            key,
            pressedAt,
            deadline,
            request.LeaseMs,
            generation,
            request.MovementReleaseMode);
        ScheduleMovementLease(action, generation, deadline, request.MovementReleaseMode);
        return Accept(request, "KEY_DOWN_SENT");
    }

    private BrokerResponse KeyUp(BrokerRequest request)
    {
        if (request.Action is not { } action || string.IsNullOrWhiteSpace(request.Key))
            return RejectAndRelease(request, "ACTION_REQUIRED");

        if (completedLeases.Remove(action, out LeaseCompletion? completion))
            return completion.Accepted
                ? Accept(request, "KEY_ALREADY_UP", completion.ActualHoldMs, completion.ReleaseLatenessMs)
                : Reject(request, completion.Code);

        if (!active.TryGetValue(action, out ActiveKey? current))
            return Accept(request, "KEY_ALREADY_UP");
        CancelMovementLease(action, current.Generation);
        bool success = sender.Send(current.Key, isKeyUp: true);
        if (!success) return RejectAndRelease(request, "KEY_UP_FAILED");
        long releasedAt = clock.NowMonoMs;
        active.Remove(action);
        (int? actualHoldMs, int? releaseLatenessMs) = MovementTiming(action, current, releasedAt);
        return Accept(request, "KEY_UP_SENT", actualHoldMs, releaseLatenessMs);
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
            BrokerLogicalAction.MoveUp => key.Equals("Up", StringComparison.OrdinalIgnoreCase),
            BrokerLogicalAction.MoveDown => key.Equals("Down", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private bool ReleaseOpposite(BrokerLogicalAction action)
    {
        foreach ((BrokerLogicalAction heldAction, ActiveKey key) in active.ToArray())
        {
            if (heldAction == action) continue;
            CancelMovementLease(heldAction, key.Generation);
            if (!sender.Send(key.Key, isKeyUp: true)) return false;
            long releasedAt = clock.NowMonoMs;
            active.Remove(heldAction);
            (int? actualHoldMs, int? releaseLatenessMs) = MovementTiming(heldAction, key, releasedAt);
            if (actualHoldMs.HasValue && releaseLatenessMs.HasValue)
            {
                completedLeases[heldAction] = new LeaseCompletion(
                    "KEY_ALREADY_UP",
                    true,
                    key.Generation,
                    actualHoldMs,
                    releaseLatenessMs);
            }
            else
            {
                completedLeases.Remove(heldAction);
            }
        }
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

    private bool ReleaseAll(bool preserveMovementTiming = false)
    {
        movementLeases.CancelAll();
        if (!preserveMovementTiming)
            completedLeases.Clear();
        bool success = true;
        foreach ((BrokerLogicalAction action, ActiveKey key) in active.ToArray())
        {
            if (!sender.Send(key.Key, isKeyUp: true))
            {
                success = false;
                continue;
            }

            long releasedAt = clock.NowMonoMs;
            active.Remove(action);
            if (!preserveMovementTiming)
                continue;

            (int? actualHoldMs, int? releaseLatenessMs) = MovementTiming(action, key, releasedAt);
            if (actualHoldMs.HasValue && releaseLatenessMs.HasValue)
            {
                completedLeases[action] = new LeaseCompletion(
                    "KEY_ALREADY_UP",
                    true,
                    key.Generation,
                    actualHoldMs,
                    releaseLatenessMs);
            }
        }
        return success;
    }

    private void ScheduleMovementLease(
        BrokerLogicalAction action,
        long generation,
        long deadlineMonoMs,
        BrokerMovementReleaseMode movementReleaseMode)
    {
        if (!IsMovement(action)) return;
        long releaseDeadlineMonoMs = movementReleaseMode == BrokerMovementReleaseMode.HostKeyUp
            ? checked(deadlineMonoMs + BrokerProtocol.StationaryMovementReleaseSafetyMarginMs)
            : deadlineMonoMs;
        movementLeases.Schedule(action, generation, releaseDeadlineMonoMs, OnMovementLeaseExpired);
    }

    private void CancelMovementLease(BrokerLogicalAction action, long generation)
    {
        if (IsMovement(action))
            movementLeases.Cancel(action, generation);
    }

    private void OnMovementLeaseExpired(BrokerLogicalAction action, long generation)
    {
        lock (sync)
        {
            if (disposed || !active.TryGetValue(action, out ActiveKey? current) ||
                current.Generation != generation)
                return;

            bool released = sender.Send(current.Key, isKeyUp: true);
            long releasedAt = clock.NowMonoMs;
            if (!released)
            {
                completedLeases[action] = new LeaseCompletion(
                    "KEY_LEASE_RELEASE_FAILED",
                    false,
                    generation,
                    null,
                    null);
                return;
            }

            active.Remove(action);
            (int? actualHoldMs, int? releaseLatenessMs) = MovementTiming(action, current, releasedAt);
            completedLeases[action] = new LeaseCompletion(
                "KEY_ALREADY_UP",
                true,
                generation,
                actualHoldMs,
                releaseLatenessMs);
        }
    }

    private static (int? ActualHoldMs, int? ReleaseLatenessMs) MovementTiming(
        BrokerLogicalAction action,
        ActiveKey key,
        long releasedAtMonoMs)
    {
        if (!IsMovement(action))
            return (null, null);

        int actualHoldMs = checked((int)Math.Max(0, releasedAtMonoMs - key.PressedAtMonoMs));
        return (actualHoldMs, Math.Max(0, actualHoldMs - key.RequestedLeaseMs));
    }

    private static bool IsMovement(BrokerLogicalAction action) => action is
        BrokerLogicalAction.MoveLeft or BrokerLogicalAction.MoveRight
        or BrokerLogicalAction.MoveUp or BrokerLogicalAction.MoveDown;

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

    private static BrokerResponse Accept(
        BrokerRequest request,
        string code,
        int? actualHoldMs = null,
        int? releaseLatenessMs = null) =>
        new(BrokerProtocol.Version, request.Sequence, true, code, actualHoldMs, releaseLatenessMs);

    private static BrokerResponse Reject(
        BrokerRequest request,
        string code,
        int? actualHoldMs = null,
        int? releaseLatenessMs = null) =>
        new(BrokerProtocol.Version, request.Sequence, false, code, actualHoldMs, releaseLatenessMs);

    private sealed record ActiveKey(
        string Key,
        long PressedAtMonoMs,
        long LeaseDeadlineMonoMs,
        int RequestedLeaseMs,
        long Generation,
        BrokerMovementReleaseMode MovementReleaseMode);

    private sealed record LeaseCompletion(
        string Code,
        bool Accepted,
        long Generation,
        int? ActualHoldMs,
        int? ReleaseLatenessMs);
}
