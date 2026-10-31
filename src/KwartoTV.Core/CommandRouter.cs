namespace KwartoTV.Core;

public sealed record RemoteCommand(string? Id, string Cmd, string? Arg);
public sealed record CommandResult(bool Ok, string? Error = null);

/// <summary>Thrown by the shell when a command cannot be done, with a sentence the phone shows as is.</summary>
public sealed class UserMessageException : Exception
{
    public UserMessageException(string message) : base(message) { }
}

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
        "app.youtube", "app.netflix", "app.bilibili", "app.livetv", "app.library", "app.settings",
        "power.sleep", "power.screenoff", "power.screenon", "power.shutdown", "power.restart",
        // the phone's channel finder: check every channel, then pick working ones and favorites
        "scan.start", "scan.stop", "catalog.choose", "catalog.favorite", "catalog.choosegroup",
        // text typed on the phone for the text box waiting on the TV ("1:" also presses Go)
        "text.set",
        // a sign-in made on the phone (Netflix, Bilibili) handed to the TV: "netflix|cookie=value; ..."
        "web.signin",
    };

    // While the TV is locked (clock screen) only these work: unlock, power, and volume.
    private static readonly HashSet<string> AllowedWhileLocked = new()
    {
        "power.screenon", "power.screenoff", "power.sleep", "power.shutdown", "power.restart",
        "media.volup", "media.voldown", "media.volume", "media.mute",
    };

    /// <summary>The message a phone gets for a button pressed while the TV is locked.</summary>
    public const string LockedMessage = "The TV is locked. Unlock it on the phone: Power → Unlock.";

    /// <summary>True while the TV shows its lock screen. Set by the shell.</summary>
    public Func<bool>? IsLocked { get; set; }

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
        if (c.Arg is { Length: > 64 } && !(c.Cmd == "playlist.add" && c.Arg.Length <= 1024) && !(c.Cmd == "text.set" && c.Arg.Length <= TextRequest.MaxLength + 2) && !(c.Cmd == "web.signin" && c.Arg.Length <= PhoneSignIn.MaxCookieText + 20)) return new(false, "bad argument");
        if (c.Cmd == "playlist.add" && !AppSettings.IsHttpsUrl(c.Arg ?? "") && !(Uri.TryCreate(c.Arg, UriKind.Absolute, out var pu) && M3uParser.IsStreamUri(pu) && pu.Scheme != "file"))
            return new(false, "bad link");

        if (!AllowedWhileLocked.Contains(c.Cmd) && IsLocked?.Invoke() == true) return new(false, LockedMessage);

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
            case "catalog.choose" or "catalog.favorite":
                if (!ChannelFinder.TryParseToggle(c.Arg, out _, out var cid) || !ChannelFinder.IsChannelId(cid)) return new(false, "bad channel");
                break;
            case "text.set":
                // "1:" or "0:" and the text (may be empty: clears the box); no control characters
                if (c.Arg is not { Length: >= 2 } ta || ta[1] != ':' || ta[0] is not ('0' or '1') || ta.Skip(2).Any(char.IsControl)) return new(false, "bad text");
                break;
            case "web.signin":
                if (PhoneSignIn.ParseArg(c.Arg).Service is null) return new(false, "The sign-in did not come through. Try again.");
                break;
            case "catalog.choosegroup":
                if (!ChannelFinder.TryParseToggle(c.Arg, out _, out var grp) || grp.Length > 60) return new(false, "bad category");
                break;
            case "media.speed":
                if (!double.TryParse(c.Arg, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var sp) || sp < 0.25 || sp > 4) return new(false, "bad speed");
                break;
        }

        try { _host.Execute(c.Cmd, c.Arg); return new(true); }
        // A UserMessageException carries a sentence for the phone; anything else only its type (no internals).
        catch (UserMessageException ex) { return new(false, ex.Message); }
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
