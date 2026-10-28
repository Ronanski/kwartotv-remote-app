using System.Runtime.InteropServices;
using KwartoTV.Core;

namespace KwartoTV.App.Services;

/// <summary>
/// The Windows master volume (what the speaker icon in the taskbar controls), through the Core Audio API.
/// If the PC has no usable audio device (for example over some remote-desktop setups) it falls back to the volume keys.
/// </summary>
public static class SystemVolume
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private static readonly Guid EndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static Guid _context = Guid.Empty;

    /// <summary>Last level we know, 0-100. Used when the Core Audio call is not available.</summary>
    private static int _estimate = 50;
    private static bool _estimateMuted;

    public static event Action<int, bool>? Changed;

    private static IAudioEndpointVolume? Endpoint()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0) return null; // eRender, eMultimedia
            var iid = EndpointVolumeIid;
            if (device.Activate(ref iid, 23, IntPtr.Zero, out var obj) != 0) return null;
            return (IAudioEndpointVolume)obj;
        }
        catch (Exception ex) { AppPaths.LogLine("audio: " + ex.Message); return null; }
    }

    /// <summary>Current level (0-100) and mute state. Null when there is no audio device.</summary>
    public static (int Percent, bool Muted)? Read()
    {
        var ep = Endpoint();
        if (ep is null) return null;
        try
        {
            ep.GetMasterVolumeLevelScalar(out var level);
            ep.GetMute(out var muted);
            return ((int)Math.Round(level * 100), muted);
        }
        catch { return null; }
    }

    /// <summary>Raises or lowers the volume by [delta] points and tells the screen to show the volume rocker.</summary>
    public static void Change(int delta)
    {
        var ep = Endpoint();
        if (ep is not null)
        {
            try
            {
                ep.GetMasterVolumeLevelScalar(out var level);
                var next = Math.Clamp(level + delta / 100f, 0f, 1f);
                ep.SetMasterVolumeLevelScalar(next, ref _context);
                if (delta > 0) ep.SetMute(false, ref _context);
                ep.GetMute(out var muted);
                Publish((int)Math.Round(next * 100), muted);
                return;
            }
            catch (Exception ex) { AppPaths.LogLine("audio change: " + ex.Message); }
        }
        // No audio device we can talk to: press the media keys (2% each) and show our best guess.
        var presses = Math.Max(1, Math.Abs(delta) / 2);
        Native.Press(Enumerable.Repeat(new KeyStroke(delta > 0 ? HostKeyMap.VkVolumeUp : HostKeyMap.VkVolumeDown, true), presses));
        _estimate = Math.Clamp(_estimate + delta, 0, 100);
        if (delta > 0) _estimateMuted = false;
        Publish(_estimate, _estimateMuted);
    }

    public static void SetPercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var ep = Endpoint();
        if (ep is not null)
        {
            try
            {
                ep.SetMasterVolumeLevelScalar(percent / 100f, ref _context);
                ep.SetMute(false, ref _context);
                Publish(percent, false);
                return;
            }
            catch (Exception ex) { AppPaths.LogLine("audio set: " + ex.Message); }
        }
        Native.Press(HostKeyMap.VolumeTo(percent));
        _estimate = percent; _estimateMuted = false;
        Publish(percent, false);
    }

    public static void ToggleMute()
    {
        var ep = Endpoint();
        if (ep is not null)
        {
            try
            {
                ep.GetMute(out var muted);
                ep.SetMute(!muted, ref _context);
                ep.GetMasterVolumeLevelScalar(out var level);
                Publish((int)Math.Round(level * 100), !muted);
                return;
            }
            catch (Exception ex) { AppPaths.LogLine("audio mute: " + ex.Message); }
        }
        Native.Press(new[] { new KeyStroke(HostKeyMap.VkVolumeMute, true) });
        _estimateMuted = !_estimateMuted;
        Publish(_estimate, _estimateMuted);
    }

    private static void Publish(int percent, bool muted) => Changed?.Invoke(percent, muted);
}
