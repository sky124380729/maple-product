namespace Maple.InputBroker;

public static class Program
{
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        if (args.Length != 12) return 1;
        var values = args
            .Chunk(2)
            .ToDictionary(item => item[0], item => item[1], StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("--pipe", out string? pipe) ||
            !values.TryGetValue("--secret", out string? secret) ||
            !values.TryGetValue("--hwnd", out string? hwndValue) ||
            !values.TryGetValue("--pid", out string? pidValue) ||
            !values.TryGetValue("--path", out string? path) ||
            !values.TryGetValue("--started", out string? startedValue) ||
            !long.TryParse(hwndValue, out long hwnd) ||
            !int.TryParse(pidValue, out int pid) ||
            !long.TryParse(startedValue, out long started)) return 1;

        var target = new Maple.Core.Broker.BrokerTargetIdentity(hwnd, pid, path, started);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        new NamedPipeBrokerServer().RunAsync(pipe, secret, target, cancellation.Token).GetAwaiter().GetResult();
        return 0;
    }
}
