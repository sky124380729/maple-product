using System.IO.Pipes;
using Maple.Core.Broker;
using Maple.Host.Broker;
using Maple.Host.Stationary;
using Maple.Host.Navigation;
using Maple.Host.Windows;

namespace Maple.Host.Tests.Broker;

public sealed class NamedPipeBrokerClientTests
{
    [Fact]
    public async Task Sends_vertical_navigation_action_as_whitelisted_broker_request()
    {
        string pipeName = $"maple-host-test-{Guid.NewGuid():N}";
        Guid sessionId = Guid.NewGuid();
        Task<BrokerRequest?> requestTask = ReceiveFirstRequestAsync(pipeName);
        NamedPipeBrokerClient client = await NamedPipeBrokerClient.ConnectAsync(
            pipeName, "test-secret", new WindowIdentity(100, 200, @"C:\Games\MapleStory.exe", 1234),
            sessionId, CancellationToken.None);

        InputActionResult response = await client.KeyDownAsync(NavigationInputAction.MoveUp, 100, CancellationToken.None);
        BrokerRequest? request = await requestTask;

        Assert.True(response.Success);
        Assert.Equal(BrokerLogicalAction.MoveUp, request!.Action);
        Assert.Equal("Up", request.Key);
        Assert.Equal(BrokerMovementReleaseMode.BrokerDeadline, request.MovementReleaseMode);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Marks_stationary_movement_for_host_key_up()
    {
        string pipeName = $"maple-host-test-{Guid.NewGuid():N}";
        Guid sessionId = Guid.NewGuid();
        Task<BrokerRequest?> requestTask = ReceiveFirstRequestAsync(pipeName);
        NamedPipeBrokerClient client = await NamedPipeBrokerClient.ConnectAsync(
            pipeName, "test-secret", new WindowIdentity(100, 200, @"C:\Games\MapleStory.exe", 1234),
            sessionId, CancellationToken.None);

        InputActionResult response = await client.KeyDownAsync(
            StationaryInputAction.MoveLeft,
            40,
            CancellationToken.None);
        BrokerRequest? request = await requestTask;

        Assert.True(response.Success);
        Assert.Equal(BrokerLogicalAction.MoveLeft, request!.Action);
        Assert.Equal(BrokerMovementReleaseMode.HostKeyUp, request.MovementReleaseMode);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Maps_stationary_key_up_timing_from_the_broker_response()
    {
        string pipeName = $"maple-host-test-{Guid.NewGuid():N}";
        Guid sessionId = Guid.NewGuid();
        Task<BrokerRequest?> requestTask = RespondToKeyUpWithTimingAsync(pipeName);
        NamedPipeBrokerClient client = await NamedPipeBrokerClient.ConnectAsync(
            pipeName, "test-secret", new WindowIdentity(100, 200, @"C:\Games\MapleStory.exe", 1234),
            sessionId, CancellationToken.None);

        InputActionResult response = await client.KeyUpAsync(
            StationaryInputAction.MoveLeft,
            CancellationToken.None);
        BrokerRequest? request = await requestTask;

        Assert.True(response.Success);
        Assert.Equal(46, response.ActualHoldMs);
        Assert.Equal(6, response.ReleaseLatenessMs);
        Assert.Equal(BrokerCommandKind.KeyUp, request!.Kind);
        Assert.Equal(BrokerMovementReleaseMode.HostKeyUp, request.MovementReleaseMode);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_sends_close_before_disconnecting()
    {
        string pipeName = $"maple-host-test-{Guid.NewGuid():N}";
        Guid sessionId = Guid.NewGuid();
        var target = new WindowIdentity(100, 200, @"C:\Games\MapleStory.exe", 1234);
        Task<BrokerRequest?> requestTask = ReceiveFirstRequestAsync(pipeName);

        NamedPipeBrokerClient client = await NamedPipeBrokerClient.ConnectAsync(
            pipeName,
            "test-secret",
            target,
            sessionId,
            CancellationToken.None);

        await client.DisposeAsync();
        BrokerRequest? request = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(request);
        Assert.Equal(BrokerCommandKind.Close, request.Kind);
        Assert.Equal(sessionId, request.SessionId);
    }

    [Fact]
    public async Task Dispose_waits_for_in_flight_io_before_sending_close()
    {
        string pipeName = $"maple-host-test-{Guid.NewGuid():N}";
        Guid sessionId = Guid.NewGuid();
        var target = new WindowIdentity(100, 200, @"C:\Games\MapleStory.exe", 1234);
        var heartbeatReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowHeartbeatResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<IReadOnlyList<BrokerCommandKind>> requestsTask = ReceiveHeartbeatThenCloseAsync(
            pipeName,
            heartbeatReceived,
            allowHeartbeatResponse.Task);
        NamedPipeBrokerClient client = await NamedPipeBrokerClient.ConnectAsync(
            pipeName,
            "test-secret",
            target,
            sessionId,
            CancellationToken.None);

        Task<InputActionResult> heartbeat = client.HeartbeatAsync(CancellationToken.None);
        await heartbeatReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task dispose = client.DisposeAsync().AsTask();

        Assert.False(dispose.IsCompleted);
        allowHeartbeatResponse.TrySetResult();
        Assert.True((await heartbeat.WaitAsync(TimeSpan.FromSeconds(2))).Success);
        await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(
            [BrokerCommandKind.Heartbeat, BrokerCommandKind.Close],
            await requestsTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Abort_interrupts_in_flight_io_without_waiting_for_the_io_lock()
    {
        string pipeName = $"maple-host-test-{Guid.NewGuid():N}";
        Guid sessionId = Guid.NewGuid();
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowServerClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task serverTask = ReceiveRequestWithoutResponseAsync(
            pipeName,
            requestReceived,
            allowServerClose.Task);
        NamedPipeBrokerClient client = await NamedPipeBrokerClient.ConnectAsync(
            pipeName,
            "test-secret",
            new WindowIdentity(100, 200, @"C:\Games\MapleStory.exe", 1234),
            sessionId,
            CancellationToken.None);
        Task<InputActionResult> heartbeat = client.HeartbeatAsync(CancellationToken.None);
        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task dispose = client.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);

        client.Abort();

        InputActionResult response = await heartbeat.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(response.Success);
        Assert.StartsWith("BROKER_IO:", response.Code);
        await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        allowServerClose.TrySetResult();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task<BrokerRequest?> ReceiveFirstRequestAsync(string pipeName)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync();

        BrokerHandshake? handshake = await BrokerWireCodec.ReadAsync<BrokerHandshake>(server, CancellationToken.None);
        Assert.NotNull(handshake);
        await BrokerWireCodec.WriteAsync(
            server,
            new BrokerHandshakeResponse(true, "HANDSHAKE_ACCEPTED"),
            CancellationToken.None);

        BrokerRequest? request = await BrokerWireCodec.ReadAsync<BrokerRequest>(server, CancellationToken.None);
        if (request is not null)
        {
            await BrokerWireCodec.WriteAsync(
                server,
                new BrokerResponse(BrokerProtocol.Version, request.Sequence, true, "CLOSED"),
                CancellationToken.None);
        }
        return request;
    }

    private static async Task<IReadOnlyList<BrokerCommandKind>> ReceiveHeartbeatThenCloseAsync(
        string pipeName,
        TaskCompletionSource heartbeatReceived,
        Task allowHeartbeatResponse)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync();
        BrokerHandshake? handshake = await BrokerWireCodec.ReadAsync<BrokerHandshake>(server, CancellationToken.None);
        Assert.NotNull(handshake);
        await BrokerWireCodec.WriteAsync(
            server,
            new BrokerHandshakeResponse(true, "HANDSHAKE_ACCEPTED"),
            CancellationToken.None);

        var requests = new List<BrokerCommandKind>();
        BrokerRequest? heartbeat = await BrokerWireCodec.ReadAsync<BrokerRequest>(server, CancellationToken.None);
        Assert.NotNull(heartbeat);
        requests.Add(heartbeat.Kind);
        heartbeatReceived.TrySetResult();
        await allowHeartbeatResponse;
        await BrokerWireCodec.WriteAsync(
            server,
            new BrokerResponse(BrokerProtocol.Version, heartbeat.Sequence, true, "HEARTBEAT_OK"),
            CancellationToken.None);

        BrokerRequest? close = await BrokerWireCodec.ReadAsync<BrokerRequest>(server, CancellationToken.None);
        Assert.NotNull(close);
        requests.Add(close.Kind);
        await BrokerWireCodec.WriteAsync(
            server,
            new BrokerResponse(BrokerProtocol.Version, close.Sequence, true, "CLOSED"),
            CancellationToken.None);
        return requests;
    }

    private static async Task<BrokerRequest?> RespondToKeyUpWithTimingAsync(string pipeName)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync();
        BrokerHandshake? handshake = await BrokerWireCodec.ReadAsync<BrokerHandshake>(server, CancellationToken.None);
        Assert.NotNull(handshake);
        await BrokerWireCodec.WriteAsync(
            server,
            new BrokerHandshakeResponse(true, "HANDSHAKE_ACCEPTED"),
            CancellationToken.None);

        BrokerRequest? request = await BrokerWireCodec.ReadAsync<BrokerRequest>(server, CancellationToken.None);
        Assert.NotNull(request);
        await BrokerWireCodec.WriteAsync(
            server,
            new BrokerResponse(
                BrokerProtocol.Version,
                request.Sequence,
                true,
                "KEY_UP_SENT",
                ActualHoldMs: 46,
                ReleaseLatenessMs: 6),
            CancellationToken.None);
        return request;
    }

    private static async Task ReceiveRequestWithoutResponseAsync(
        string pipeName,
        TaskCompletionSource requestReceived,
        Task allowServerClose)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync();
        BrokerHandshake? handshake = await BrokerWireCodec.ReadAsync<BrokerHandshake>(server, CancellationToken.None);
        Assert.NotNull(handshake);
        await BrokerWireCodec.WriteAsync(
            server,
            new BrokerHandshakeResponse(true, "HANDSHAKE_ACCEPTED"),
            CancellationToken.None);
        BrokerRequest? request = await BrokerWireCodec.ReadAsync<BrokerRequest>(server, CancellationToken.None);
        Assert.NotNull(request);
        requestReceived.TrySetResult();
        await allowServerClose;
    }
}
