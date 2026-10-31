namespace KwartoTV.Core;

/// <summary>When a channel's health is checked again, and how a live stream that drops is reconnected.</summary>
public static class HealthPolicy
{
    /// <summary>Channels that work are checked again after a day.</summary>
    public static readonly TimeSpan RecheckOk = TimeSpan.FromHours(24);

    /// <summary>
    /// Channels that did not work get another chance after 2 hours: many come back (a server restart, a short outage),
    /// and a channel that failed once should not stay hidden for a whole day.
    /// </summary>
    public static readonly TimeSpan RecheckDead = TimeSpan.FromHours(2);

    /// <summary>How long a channel that really played stays trusted before it is checked again.</summary>
    public static readonly TimeSpan PlayedTrust = TimeSpan.FromDays(3);

    /// <summary>YouTube live links come and go during the day, so they are checked much more often.</summary>
    public static readonly TimeSpan RecheckLive = TimeSpan.FromMinutes(20);

    public static bool NeedsCheck(Channel c, ChannelHealth? h, DateTimeOffset now)
    {
        if (h is null || h.Status == HealthStatus.Unknown) return true;
        // It really played lately: a check (which only downloads a little, with other identities) must not overrule that.
        if (h.FromPlayer && h.Status == HealthStatus.Ok) return now - h.CheckedAt > PlayedTrust;
        var after = c.Kind == StreamKind.YouTube ? RecheckLive : h.Status == HealthStatus.Dead ? RecheckDead : RecheckOk;
        return now - h.CheckedAt > after;
    }

    /// <summary>
    /// A live channel that was playing and then stopped (Wi-Fi blip, server hiccup) is opened again by itself,
    /// waiting a little longer each time. Returns the wait before reconnect number [attempt] (0-based), or null to give up.
    /// </summary>
    public static TimeSpan? ReconnectDelay(int attempt) => attempt switch
    {
        0 => TimeSpan.FromSeconds(1),
        1 => TimeSpan.FromSeconds(3),
        2 => TimeSpan.FromSeconds(6),
        3 => TimeSpan.FromSeconds(10),
        4 => TimeSpan.FromSeconds(20),
        _ => null,
    };
}
