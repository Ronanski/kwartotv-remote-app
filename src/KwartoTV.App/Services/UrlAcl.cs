using System.Diagnostics;
using System.Security.Principal;

namespace KwartoTV.App.Services;

/// <summary>
/// Opens Windows Firewall for the phone remote (TCP port, mDNS, and the app itself). The installer does the same;
/// the "Fix network access" button runs it again elevated. Also removes the old http.sys URL reservation from earlier versions.
/// </summary>
public static class UrlAcl
{
    public static string Prefix(int port) => $"http://+:{port}/";

    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Commands that open the firewall for the remote. Mirrors installer\KwartoTV.iss.</summary>
    public static string[] SetupCommands(int port) => new[]
    {
        $"netsh http delete urlacl url=http://+:{port}/",
        $"netsh advfirewall firewall delete rule name=\"KwartoTV app\"",
        $"netsh advfirewall firewall add rule name=\"KwartoTV app\" dir=in action=allow program=\"{Environment.ProcessPath}\" profile=any",
        $"netsh advfirewall firewall delete rule name=\"KwartoTV remote\"",
        $"netsh advfirewall firewall add rule name=\"KwartoTV remote\" dir=in action=allow protocol=TCP localport={string.Join(",", KwartoTV.Core.RemoteServer.PortsToTry(port))} profile=any",
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
