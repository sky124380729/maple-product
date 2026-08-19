using System.IO.Pipes;
using Maple.Core.Broker;
using Maple.Host.Broker;
using Maple.Host.Stationary;
using Maple.Host.Windows;

namespace Maple.Host.Tests.Broker;

public sealed class NamedPipeBrokerClientTests
{
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
}
