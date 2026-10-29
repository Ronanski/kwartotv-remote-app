namespace KwartoTV.Core;

public sealed record RemoteCommand(string? Id, string Cmd, string? Arg);
public sealed record CommandResult(bool Ok, string? Error = null);

/// <summary>Implemented by the Windows shell: navigation keys, media keys, volume, power.</summary>
public interface IHostActions
{
    void Execute(string cmd, string? arg);
}

/// <summary>Whitelists remote commands, validates arguments, and handles channel commands itself.</summary>
public sealed class CommandRouter
{
    private static readonly HashSet<string> HostCommands = new()
    {
        "nav.up", "nav.down", "nav.left", "nav.right", "nav.ok", "nav.back", "nav.home",
        "media.playpause", "media.next", "media.prev", "media.stop", "media.mute",
        "media.seek", "media.volume", "media.speed",
        "media.volup", "media.voldown",
        "channel.list", "playlist.add",
        "key.text", "key.backspace", "key.enter",
        "mouse.move", "mouse.click", "mouse.rightclick", "mouse.scroll",
        "app.youtube", "app.netflix", "app.livetv", "app.library", "app.settings",
        "power.sleep", "power.screenoff", "power.screenon", "power.shutdown", "power.restart",
    };

    private readonly ChannelLineup _lineup;
    private readonly IHostActions _host;
    private readonly Func<DateTimeOffset> _clock;

    public CommandRouter(ChannelLineup lineup, IHostActions host, Func<DateTimeOffset>? clock = null)
    {
        _lineup = lineup; _host = host; _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public CommandResult Handle(RemoteCommand c)
    {
        if (string.IsNullOrWhiteSpace(c.Cmd) || c.Cmd.Length > 40) return new(false, "bad command");
        // A playlist link from the phone can be long; everything else is short.
        if (c.Arg is { Length: > 64 } && !(c.Cmd == "playlist.add" && c.Arg.Length <= 1024)) return new(false, "bad argument");
        if (c.Cmd == "playlist.add" && !AppSettings.IsHttpsUrl(c.Arg ?? "") && !(Uri.TryCreate(c.Arg, UriKind.Absolute, out var pu) && M3uParser.IsStreamUri(pu) && pu.Scheme != "file"))
            return new(false, "bad link");

        switch (c.Cmd)
        {
            case "channel.next": return Tuned(_lineup.Next());
            case "channel.prev": return Tuned(_lineup.Previous());
            case "channel.last": return Tuned(_lineup.Last());
            case "channel.number":
                return int.TryParse(c.Arg, out var n) ? Tuned(_lineup.TuneNumber(n)) : new(false, "bad number");
            case "channel.commit":
                return Tuned(_lineup.Commit());
            case "channel.digit":
                if (!int.TryParse(c.Arg, out var d) || d < 0 || d > 9) return new(false, "bad digit");
                _lineup.PressDigit(d, _clock());
                return new(true);
            case "channel.favorite":
                var cur = _lineup.Current;
                return cur is null ? new(false, "no channel") : (_lineup.ToggleFavorite(cur.Id), new CommandResult(true)).Item2;
        }

        if (!HostCommands.Contains(c.Cmd)) return new(false, "unknown command");

        switch (c.Cmd)
        {
            case "media.seek":
                if (!int.TryParse(c.Arg, out var s) || s < -3600 || s > 3600) return new(false, "bad seek");
                break;
            case "media.volume":
                if (!int.TryParse(c.Arg, out var v) || v < 0 || v > 100) return new(false, "bad volume");
                break;
            case "mouse.move":
                if (!TryMove(c.Arg, out _, out _)) return new(false, "bad move");
                break;
            case "mouse.scroll":
                if (!int.TryParse(c.Arg, out var sc) || sc < -20 || sc > 20 || sc == 0) return new(false, "bad scroll");
                break;
            case "key.text":
                if (string.IsNullOrEmpty(c.Arg) || c.Arg.Any(char.IsControl)) return new(false, "bad text");
                break;
            case "media.speed":
                if (!double.TryParse(c.Arg, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var sp) || sp < 0.25 || sp > 4) return new(false, "bad speed");
                break;
        }

        try { _host.Execute(c.Cmd, c.Arg); return new(true); }
        catch (Exception ex) { return new(false, ex.GetType().Name); }
    }

    /// <summary>"dx,dy" in pixels, each -300..300.</summary>
    public static bool TryMove(string? arg, out int dx, out int dy)
    {
        dx = dy = 0;
        var parts = (arg ?? "").Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out dx) && int.TryParse(parts[1], out dy) && Math.Abs(dx) <= 300 && Math.Abs(dy) <= 300;
    }

    private static CommandResult Tuned(Channel? ch) => ch is null ? new(false, "no such channel") : new(true);
}
