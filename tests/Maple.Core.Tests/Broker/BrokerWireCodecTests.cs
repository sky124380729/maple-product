using Maple.Core.Broker;

namespace Maple.Core.Tests.Broker;

public sealed class BrokerWireCodecTests
{
    [Fact]
    public async Task Round_trips_a_request_with_length_prefixed_json()
    {
        var stream = new MemoryStream();
        var request = new BrokerRequest(
            BrokerProtocol.Version,
            7,
            Guid.Parse("f98d67dd-4d2b-4e09-96ea-17096b806229"),
            BrokerCommandKind.KeyDown,
            BrokerLogicalAction.Attack,
            "Ctrl",
            60_000);

        await BrokerWireCodec.WriteAsync(stream, request, CancellationToken.None);
        stream.Position = 0;
        BrokerRequest? decoded = await BrokerWireCodec.ReadAsync<BrokerRequest>(stream, CancellationToken.None);

        Assert.Equal(request, decoded);
    }

    [Fact]
    public async Task Rejects_frames_larger_than_the_protocol_limit()
    {
        var stream = new MemoryStream();
        await stream.WriteAsync(BitConverter.GetBytes(BrokerWireCodec.MaximumFrameBytes + 1));
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BrokerWireCodec.ReadAsync<BrokerRequest>(stream, CancellationToken.None));
    }
}
