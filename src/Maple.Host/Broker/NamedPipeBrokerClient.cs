using System.IO.Pipes;
using Maple.Core.Broker;
using Maple.Host.Safety;
using Maple.Host.Stationary;
using Maple.Host.Windows;

namespace Maple.Host.Broker;

public interface IBrokerConnection : IStationaryActionSink, IBrokerLeaseProbe, IAsyncDisposable
{
    Guid SessionId { get; }
    long LastSequence => 0;
    void SetAttackKey(string key);
    void MarkUnhealthy() { }
    void MarkUnhealthy(string code) => MarkUnhealthy();
    Task<InputActionResult> HeartbeatAsync(CancellationToken cancellationToken);
}

public sealed class NamedPipeBrokerClient : IBrokerConnection
{
    private readonly NamedPipeClientStream pipe;
    private readonly Guid sessionId;
    private readonly SemaphoreSlim ioLock = new(1, 1);
    private long sequence;
    private int disposed;
    private int faulted;
    private string faultCode = "BROKER_UNAVAILABLE";
    private string attackKey = "Ctrl";

    private NamedPipeBrokerClient(NamedPipeClientStream pipe, Guid sessionId)
    {
        this.pipe = pipe;
        this.sessionId = sessionId;
    }

    public Guid SessionId => sessionId;
    public long LastSequence => Interlocked.Read(ref sequence);
    public bool IsHealthy => Volatile.Read(ref disposed) == 0 && Volatile.Read(ref faulted) == 0 && pipe.IsConnected;
    public void MarkUnhealthy() => MarkUnhealthy("BROKER_UNAVAILABLE");
    public void MarkUnhealthy(string code)
    {
        faultCode = string.IsNullOrWhiteSpace(code) ? "BROKER_UNAVAILABLE" : code;
        Volatile.Write(ref faulted, 1);
    }
    public void SetAttackKey(string key) => attackKey = key;

    public static async Task<NamedPipeBrokerClient> ConnectAsync(
        string pipeName,
        string secret,
        WindowIdentity target,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5_000, cancellationToken);
        var client = new NamedPipeBrokerClient(pipe, sessionId);
        await BrokerWireCodec.WriteAsync(
            pipe,
            new BrokerHandshake(
                BrokerProtocol.Version,
                secret,
                sessionId,
                new BrokerTargetIdentity(target.Hwnd, target.ProcessId, target.ProcessPath, target.ProcessStartedAtUnixMs)),
            cancellationToken);
        BrokerHandshakeResponse? response = await BrokerWireCodec.ReadAsync<BrokerHandshakeResponse>(pipe, cancellationToken);
        if (response is null || !response.Accepted)
        {
            await client.DisposeAsync();
            throw new InvalidOperationException(response?.Code ?? "BROKER_HANDSHAKE_FAILED");
        }
        return client;
    }

    public Task<InputActionResult> KeyDownAsync(StationaryInputAction action, int leaseMs, CancellationToken cancellationToken) =>
        SendAsync(BrokerCommandKind.KeyDown, action, KeyFor(action), leaseMs, cancellationToken);

    public Task<InputActionResult> KeyUpAsync(StationaryInputAction action, CancellationToken cancellationToken) =>
        SendAsync(BrokerCommandKind.KeyUp, action, KeyFor(action), 0, cancellationToken);

    public Task<InputActionResult> ReleaseAllAsync(CancellationToken cancellationToken) =>
        SendAsync(BrokerCommandKind.ReleaseAll, null, null, 0, cancellationToken, allowWhenUnhealthy: true);

    public Task<InputActionResult> HeartbeatAsync(CancellationToken cancellationToken) =>
        SendAsync(BrokerCommandKind.Heartbeat, null, null, 0, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            await ioLock.WaitAsync(CancellationToken.None);
            try
            {
                if (pipe.IsConnected)
                {
                    await SendCoreAsync(BrokerCommandKind.Close, null, null, 0, CancellationToken.None);
                }
            }
            finally
            {
                ioLock.Release();
            }
        }
        catch { }
        finally
        {
            pipe.Dispose();
            ioLock.Dispose();
        }
    }

    private async Task<InputActionResult> SendAsync(
        BrokerCommandKind kind,
        StationaryInputAction? action,
        string? key,
        int leaseMs,
        CancellationToken cancellationToken,
        bool allowWhenUnhealthy = false)
    {
        if (!allowWhenUnhealthy && !IsHealthy) return InputActionResult.Fail(faultCode);
        bool lockTaken = false;
        try
        {
            await ioLock.WaitAsync(cancellationToken);
            lockTaken = true;
            return !allowWhenUnhealthy && !IsHealthy
                ? InputActionResult.Fail(faultCode)
                : await SendCoreAsync(kind, action, key, leaseMs, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            return InputActionResult.Fail("BROKER_IO:" + exception.GetType().Name);
        }
        finally
        {
            if (lockTaken) ioLock.Release();
        }
    }

    private async Task<InputActionResult> SendCoreAsync(
        BrokerCommandKind kind,
        StationaryInputAction? action,
        string? key,
        int leaseMs,
        CancellationToken cancellationToken)
    {
        long next = Interlocked.Increment(ref sequence);
        await BrokerWireCodec.WriteAsync(
            pipe,
            new BrokerRequest(
                BrokerProtocol.Version,
                next,
                sessionId,
                kind,
                ToLogicalAction(action),
                key,
                leaseMs),
            cancellationToken);
        BrokerResponse? response = await BrokerWireCodec.ReadAsync<BrokerResponse>(pipe, cancellationToken);
        return response is { Accepted: true }
            ? InputActionResult.Ok(response.Code)
            : InputActionResult.Fail(response?.Code ?? "BROKER_RESPONSE_INVALID");
    }

    private string KeyFor(StationaryInputAction action) => action switch
    {
        StationaryInputAction.Attack => attackKey,
        StationaryInputAction.MoveLeft => "Left",
        StationaryInputAction.MoveRight => "Right",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static BrokerLogicalAction? ToLogicalAction(StationaryInputAction? action) => action switch
    {
        StationaryInputAction.Attack => BrokerLogicalAction.Attack,
        StationaryInputAction.MoveLeft => BrokerLogicalAction.MoveLeft,
        StationaryInputAction.MoveRight => BrokerLogicalAction.MoveRight,
        _ => null
    };
}
