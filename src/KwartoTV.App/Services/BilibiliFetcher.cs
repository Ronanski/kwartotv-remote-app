using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace KwartoTV.App.Services;

/// <summary>
/// Some of bilibili.tv's lists (recommended, the anime page) only answer from inside its own site (they need the cookies
/// the site sets). This opens bilibili.tv in a browser kept off screen, with the same saved profile as Bilibili in
/// KwartoTV (so a sign-in counts), and fetches the lists from there.
/// </summary>
public sealed class BilibiliFetcher
{
    private const string Script = @"
(function () {
  if (window.__kwFetch) return; window.__kwFetch = true;
  window.__kwGet = function (key, url) {
    fetch(url, { credentials: 'include' }).then(function (r) { return r.text(); })
      .then(function (t) { window.chrome.webview.postMessage(JSON.stringify({ key: key, body: t })); })
      .catch(function (e) { window.chrome.webview.postMessage(JSON.stringify({ key: key, body: '', error: '' + e })); });
  };
})();";

    /// <summary>Fetches each url from inside bilibili.tv. Returns key → body (missing keys failed). At most [timeout].</summary>
    public static async Task<Dictionary<string, string>> FetchAsync(IReadOnlyDictionary<string, string> urls, TimeSpan timeout)
    {
        var result = new Dictionary<string, string>();
        var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Width = 800, Height = 600, Left = -20000, Top = -20000, Background = Brushes.Black, Title = "KwartoTV Bilibili", Content = web,
        };
        window.Show();
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.Profile("webview-web-bilibili"), new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.IsMuted = true;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(Script);
            var done = new TaskCompletionSource();
            core.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var d = JsonDocument.Parse(e.TryGetWebMessageAsString());
                    var key = d.RootElement.GetProperty("key").GetString();
                    var body = d.RootElement.GetProperty("body").GetString();
                    if (key is not null && !string.IsNullOrEmpty(body)) result[key] = body;
                    else if (key is not null) result[key] = "";
                    if (result.Count >= urls.Count) done.TrySetResult();
                }
                catch { }
            };
            var loaded = new TaskCompletionSource();
            core.NavigationCompleted += (_, _) => loaded.TrySetResult();
            core.Navigate("https://www.bilibili.tv/en");
            await Task.WhenAny(loaded.Task, Task.Delay(timeout));
            // The site sets its cookies while it starts: give it a moment.
            await Task.Delay(1500);
            foreach (var (key, url) in urls)
                await core.ExecuteScriptAsync($"window.__kwGet({JsonSerializer.Serialize(key)}, {JsonSerializer.Serialize(url)})");
            await Task.WhenAny(done.Task, Task.Delay(timeout));
        }
        catch (Exception ex) { AppPaths.LogLine("bilibili: page fetch: " + ex.Message); }
        finally
        {
            try { web.Dispose(); } catch { }
            try { window.Close(); } catch { }
        }
        return result.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
