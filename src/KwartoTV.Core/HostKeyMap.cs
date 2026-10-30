namespace KwartoTV.Core;

public readonly record struct KeyStroke(ushort Vk, bool Extended = false, bool Alt = false);

/// <summary>Which window currently has the screen. Decides what "Back" means.</summary>
public enum ForegroundKind { KwartoTV, YouTube, Netflix, Web }

/// <summary>Remote command to Windows key presses. Pure data so it can be tested on any OS.</summary>
public static class HostKeyMap
{
    public const ushort VkBack = 0x08, VkReturn = 0x0D, VkEscape = 0x1B, VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28;
    public const ushort VkVolumeMute = 0xAD, VkVolumeDown = 0xAE, VkVolumeUp = 0xAF;
    public const ushort VkMediaNext = 0xB0, VkMediaPrev = 0xB1, VkMediaStop = 0xB2, VkMediaPlayPause = 0xB3;

    private static IReadOnlyList<KeyStroke> One(ushort vk, bool ext = false, bool alt = false) => new[] { new KeyStroke(vk, ext, alt) };

    public static IReadOnlyList<KeyStroke>? Navigation(string cmd, ForegroundKind fg) => cmd switch
    {
        "nav.up" => One(VkUp, true),
        "nav.down" => One(VkDown, true),
        "nav.left" => One(VkLeft, true),
        "nav.right" => One(VkRight, true),
        "nav.ok" or "key.enter" => One(VkReturn),
        "key.backspace" => One(VkBack),
        // YouTube TV and Netflix (KwartoTV's TV navigation, and the Store app) treat Esc as Back. Ordinary sites use browser-style Alt+Left.
        "nav.back" => fg is ForegroundKind.Web ? One(VkLeft, true, alt: true) : One(VkEscape),
        _ => null,
    };

    public static IReadOnlyList<KeyStroke>? Media(string cmd) => cmd switch
    {
        "media.playpause" => One(VkMediaPlayPause, true),
        "media.next" => One(VkMediaNext, true),
        "media.prev" => One(VkMediaPrev, true),
        "media.stop" => One(VkMediaStop, true),
        "media.mute" => One(VkVolumeMute, true),
        "media.volup" => One(VkVolumeUp, true),
        "media.voldown" => One(VkVolumeDown, true),
        _ => null,
    };

    /// <summary>Arrow presses for players that seek 10 s per arrow (YouTube TV, Netflix).</summary>
    public static IReadOnlyList<KeyStroke> Seek(int seconds)
    {
        int n = Math.Clamp((int)Math.Round(Math.Abs(seconds) / 10.0), 1, 30);
        return Enumerable.Repeat(new KeyStroke(seconds < 0 ? VkLeft : VkRight, true), n).ToList();
    }

    /// <summary>Windows volume keys move 2% per press, so go to 0 and climb to the target.</summary>
    public static IReadOnlyList<KeyStroke> VolumeTo(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var keys = Enumerable.Repeat(new KeyStroke(VkVolumeDown, true), 50).ToList();
        keys.AddRange(Enumerable.Repeat(new KeyStroke(VkVolumeUp, true), percent / 2));
        return keys;
    }
}
