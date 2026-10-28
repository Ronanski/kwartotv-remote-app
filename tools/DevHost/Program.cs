using KwartoTV.Core;

// Runs the real remote server with a fake TV, for trying the browser/Android remote without Windows.
// Usage: dotnet run --project tools/DevHost [port]   (prints a pairing code; pass --fixed-pin 123456 to reuse a code)
int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8765;
var lineup = new ChannelLineup();
lineup.Load(Enumerable.Range(1, 30).Select(i => new Channel(Channel.StableId("https://example.invalid/" + i), $"Demo Channel {i}", $"https://example.invalid/{i}.m3u8", null, "Demo", StreamKind.Hls)));
lineup.TuneNumber(1);
var pairing = new PairingService();
var host = new LogHost();
var router = new CommandRouter(lineup, host);
await using var server = new RemoteServer($"http://+:{port}/", pairing, router, lineup) { ServerName = "Dev TV" };
try { server.Start(); } catch { await using var _ = server; Console.WriteLine("falling back to localhost"); }
var pin = pairing.StartPairing(DateTimeOffset.UtcNow);
Console.WriteLine($"READY http://127.0.0.1:{port}/  pin={pin}");
var tick = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
while (await tick.WaitForNextTickAsync()) lineup.Tick(DateTimeOffset.UtcNow);

sealed class LogHost : IHostActions
{
    public void Execute(string cmd, string? arg) => Console.WriteLine($"HOST {cmd} {arg}");
}
