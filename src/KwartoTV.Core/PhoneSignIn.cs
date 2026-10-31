using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>
/// Signing in to Netflix or Bilibili on the phone instead of on the TV: the phone opens the service's own sign-in page,
/// and once signed in sends the sign-in (its cookies) to the TV, which puts them in its own browser for that service and
/// opens the home page, ready for the arrows, OK and Back.
/// </summary>
public static class PhoneSignIn
{
    public sealed record Service(string Id, string Name, string Domain, string Home, string SignInPage, string SessionCookie);

    public static readonly IReadOnlyList<Service> Services = new[]
    {
        new Service("netflix", "Netflix", "netflix.com", "https://www.netflix.com/browse", "https://www.netflix.com/login", "NetflixId"),
        new Service("bilibili", "Bilibili", "bilibili.tv", "https://www.bilibili.tv/en/", "https://www.bilibili.tv/en/login", "SESSDATA"),
    };

    public static Service? Find(string? id) => Services.FirstOrDefault(s => s.Id == id);

    /// <summary>Longest cookie text a phone may send.</summary>
    public const int MaxCookieText = 12000;

    /// <summary>"name=value; name2=value2" (as Android's CookieManager gives them) into pairs; bad parts are skipped.</summary>
    public static IReadOnlyList<(string Name, string Value)> ParseCookies(string? text)
    {
        var list = new List<(string, string)>();
        foreach (var part in (text ?? "").Split(';'))
        {
            var p = part.Trim();
            var eq = p.IndexOf('=');
            if (eq <= 0) continue;
            var name = p[..eq].Trim();
            var value = p[(eq + 1)..].Trim();
            if (name.Length > 200 || value.Length > 4096 || name.Any(c => char.IsControl(c) || c is ' ' or ',' or ';' or '=') || value.Any(char.IsControl)) continue;
            list.Add((name, value));
        }
        return list;
    }

    /// <summary>Phone sign-in argument: "netflix|a=b; c=d". Null service when it is not one of ours or has no session cookie.</summary>
    public static (Service? Service, IReadOnlyList<(string Name, string Value)> Cookies) ParseArg(string? arg)
    {
        var bar = arg?.IndexOf('|') ?? -1;
        if (arg is null || bar <= 0) return (null, Array.Empty<(string, string)>());
        var service = Find(arg[..bar]);
        var cookies = ParseCookies(arg[(bar + 1)..]);
        return service is null || !cookies.Any(c => c.Name == service.SessionCookie) ? (null, cookies) : (service, cookies);
    }

    /// <summary>{"type":"signin","service":"netflix","name":"Netflix","page":"https://www.netflix.com/login","done":false}: the TV asks phones to sign in (or says it worked).</summary>
    public static string Message(Service s, bool done) =>
        JsonSerializer.Serialize(new { type = "signin", service = s.Id, name = s.Name, page = s.SignInPage, domain = s.Domain, done });
}
