using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using KwartoTV.App.Services;

namespace KwartoTV.App.Player;

/// <summary>Thin libmpv wrapper. Video renders into the window handle given to Create (mpv "wid").</summary>
public sealed class MpvPlayer : IDisposable
{
    private const string Lib = "libmpv-2.dll";

    [DllImport(Lib)] private static extern IntPtr mpv_create();
    [DllImport(Lib)] private static extern int mpv_initialize(IntPtr ctx);
    [DllImport(Lib)] private static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport(Lib)] private static extern int mpv_set_option_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Lib)] private static extern int mpv_set_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Lib)] private static extern IntPtr mpv_get_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] private static extern int mpv_command(IntPtr ctx, IntPtr[] args);
    [DllImport(Lib)] private static extern void mpv_free(IntPtr data);
    [DllImport(Lib)] private static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
    [DllImport(Lib)] private static extern int mpv_request_log_messages(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string minLevel);
    [DllImport(Lib)] private static extern IntPtr mpv_error_string(int error);

    [StructLayout(LayoutKind.Sequential)] private struct MpvEvent { public int EventId; public int Error; public ulong ReplyUserdata; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] private struct MpvEventLog { public IntPtr Prefix; public IntPtr Level; public IntPtr Text; public int LogLevel; }
    [StructLayout(LayoutKind.Sequential)] private struct MpvEventEndFile { public int Reason; public int Error; }

    private const int EvLog = 2, EvEndFile = 7, EvFileLoaded = 8, EndReasonError = 4;

    /// <summary>Public stream servers often refuse mpv's own name, so identify as a normal browser unless the playlist says otherwise.</summary>
    public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private IntPtr _ctx;

    /// <summary>True when libmpv-2.dll can be found next to the app.</summary>
    public static bool IsAvailable()
    {
        try
        {
            if (NativeLibrary.TryLoad(Lib, Assembly.GetExecutingAssembly(), DllImportSearchPath.AssemblyDirectory, out var h)) { NativeLibrary.Free(h); return true; }
        }
        catch { }
        return false;
    }

    public static MpvPlayer Create(IntPtr windowHandle)
    {
        var p = new MpvPlayer { _ctx = mpv_create() };
        if (p._ctx == IntPtr.Zero) throw new InvalidOperationException("mpv_create failed");
        p.Opt("wid", windowHandle.ToInt64().ToString(CultureInfo.InvariantCulture));
        p.Opt("vo", "gpu");
        p.Opt("hwdec", "auto-safe");
        p.Opt("idle", "yes");
        p.Opt("force-window", "yes");
        p.Opt("keep-open", "no");
        p.Opt("osc", "no");
        p.Opt("input-default-bindings", "no");
        p.Opt("input-vo-keyboard", "no");
        p.Opt("cursor-autohide", "no");
        p.Opt("network-timeout", "15");
        p.Opt("cache", "yes");
        p.Opt("demuxer-max-bytes", "64MiB");
        p.Opt("sub-auto", "fuzzy");
        p.Opt("audio-client-name", "KwartoTV");
        p.Opt("ytdl", "no");   // YouTube is handled by the in-app browser; the youtube-dl hook only slows down and confuses live streams
        p.Opt("user-agent", DefaultUserAgent);
        if (mpv_initialize(p._ctx) < 0) { p.Dispose(); throw new InvalidOperationException("mpv_initialize failed"); }
        mpv_request_log_messages(p._ctx, "warn");
        return p;
    }

    /// <summary>What mpv reported since the last call: the file opened, or why it failed (HTTP error, bad format, ...).</summary>
    public readonly record struct PlayEvents(bool Loaded, string? Problem);

    private readonly List<string> _recentLog = new();

    public PlayEvents TakeEvents()
    {
        bool loaded = false; string? problem = null;
        for (int i = 0; i < 64 && _ctx != IntPtr.Zero; i++)
        {
            var ptr = mpv_wait_event(_ctx, 0);
            if (ptr == IntPtr.Zero) break;
            var ev = Marshal.PtrToStructure<MpvEvent>(ptr);
            if (ev.EventId == 0) break;
            switch (ev.EventId)
            {
                case EvFileLoaded: loaded = true; _recentLog.Clear(); break;
                case EvLog when ev.Data != IntPtr.Zero:
                {
                    var log = Marshal.PtrToStructure<MpvEventLog>(ev.Data);
                    var text = Marshal.PtrToStringUTF8(log.Text)?.Trim();
                    if (!string.IsNullOrEmpty(text)) { _recentLog.Add(text); if (_recentLog.Count > 8) _recentLog.RemoveAt(0); }
                    break;
                }
                case EvEndFile when ev.Data != IntPtr.Zero:
                {
                    var end = Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
                    if (end.Reason == EndReasonError)
                    {
                        var err = Marshal.PtrToStringUTF8(mpv_error_string(end.Error)) ?? "error " + end.Error;
                        // the most useful warning line (server said 403/404, unsupported format, ...) is usually just before the error
                        var hint = _recentLog.LastOrDefault(l => !l.Contains("ytdl", StringComparison.OrdinalIgnoreCase) && !l.Contains("youtube-dl", StringComparison.OrdinalIgnoreCase)
                            && (l.Contains("HTTP", StringComparison.OrdinalIgnoreCase) || l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains("failed", StringComparison.OrdinalIgnoreCase)));
                        problem = hint is null ? err : $"{err} ({Shorten(hint)})";
                        AppPaths.LogLine("player: " + problem);
                    }
                    break;
                }
            }
        }
        return new PlayEvents(loaded, problem);
    }

    private static string Shorten(string s) => s.Length <= 90 ? s : s[..87] + "...";

    private void Opt(string name, string value) => mpv_set_option_string(_ctx, name, value);

    private int Command(params string[] args)
    {
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            return _ctx == IntPtr.Zero ? -1 : mpv_command(_ctx, ptrs);
        }
        finally { for (int i = 0; i < args.Length; i++) Marshal.FreeCoTaskMem(ptrs[i]); }
    }

    private string? Get(string name)
    {
        if (_ctx == IntPtr.Zero) return null;
        var p = mpv_get_property_string(_ctx, name);
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(p); } finally { mpv_free(p); }
    }

    private void Set(string name, string value) { if (_ctx != IntPtr.Zero) mpv_set_property_string(_ctx, name, value); }

    /// <summary>Per-stream headers from the playlist (#EXTVLCOPT). Pass nulls to go back to the defaults.</summary>
    public void SetHeaders(string? referrer, string? userAgent)
    {
        Set("referrer", referrer ?? "");
        Set("user-agent", userAgent ?? DefaultUserAgent);
    }

    public void Load(string urlOrPath) => Command("loadfile", urlOrPath, "replace");
    public void Stop() => Command("stop");

    public bool Paused
    {
        get => Get("pause") == "yes";
        set => Set("pause", value ? "yes" : "no");
    }

    /// <summary>True when nothing is loaded: either never started, finished, or the stream failed.</summary>
    public bool Idle => Get("idle-active") == "yes";

    /// <summary>True while the stream is stalled waiting for data.</summary>
    public bool Buffering => Get("paused-for-cache") == "yes";

    public double Position => double.TryParse(Get("time-pos"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    public double Duration => double.TryParse(Get("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public void Seek(int seconds) => Command("seek", seconds.ToString(CultureInfo.InvariantCulture), "relative");
    public void SetSpeed(double speed) => Set("speed", speed.ToString(CultureInfo.InvariantCulture));

    public void Dispose()
    {
        var c = _ctx; _ctx = IntPtr.Zero;
        if (c != IntPtr.Zero) mpv_terminate_destroy(c);
    }
}
