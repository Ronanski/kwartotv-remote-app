using System.Diagnostics;
using System.Security.Principal;

namespace KwartoTV.App.Services;

/// <summary>
/// HttpListener on http://+:PORT/ needs a URL reservation (and the firewall open) for non-admin users.
/// The installer adds both. If the app starts without them it asks Windows once (UAC prompt) to add them.
/// </summary>
public static class UrlAcl
{
    public static string Prefix(int port) => $"http://+:{port}/";

    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Commands that reserve the URL for all local users and open the firewall. Also used by the installer script.</summary>
    public static string[] SetupCommands(int port) => new[]
    {
        $"netsh http delete urlacl url=http://+:{port}/",
        $"netsh http add urlacl url=http://+:{port}/ sddl=D:(A;;GX;;;BU)",
        $"netsh advfirewall firewall delete rule name=\"KwartoTV remote\"",
        $"netsh advfirewall firewall add rule name=\"KwartoTV remote\" dir=in action=allow protocol=TCP localport={port} profile=any",
        $"netsh advfirewall firewall delete rule name=\"KwartoTV discovery\"",
        $"netsh advfirewall firewall add rule name=\"KwartoTV discovery\" dir=in action=allow protocol=UDP localport=5353 profile=any",
    };

    /// <summary>Runs the setup elevated. Returns false if the user declined the prompt or it failed.</summary>
    public static bool RunElevated(int port)
    {
        try
        {
            var script = string.Join(" & ", SetupCommands(port).Select(c => c.Replace("&", "^&")));
            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(20000);
            return true;
        }
        catch { return false; }
    }
}
