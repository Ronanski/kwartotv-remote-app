using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using KwartoTV.Core;

int pass = 0, fail = 0;
void Check(bool ok, string name)
{
    if (ok) pass++; else { fail++; Console.WriteLine("FAIL: " + name); }
}

// Placeholder URLs only: this repo ships no real channel sources.
const string Sample = "\uFEFF#EXTM3U\r\n" +
    "#EXTINF:-1 tvg-id=\"a\" tvg-logo=\"https://example.invalid/a.png\" group-title=\"News\",Channel A\r\n" +
    "https://example.invalid/a.m3u8\r\n" +
    "#EXTVLCOPT:http-user-agent=x\r\n" +
    "#EXTINF:-1 group-title=\"Kids, Fun\",Channel B, The Sequel\r\n" +
    "https://example.invalid/b.m3u8?token=1\r\n" +
    "#EXTINF:-1,Channel C\r\n" +
    "https://www.youtube.com/watch?v=PLACEHOLDER\r\n" +
    "#EXTINF:-1,Dup A\r\n" +
    "https://example.invalid/a.m3u8\r\n" +
    "#EXTINF:-1,Bad scheme\r\n" +
    "javascript:alert(1)\r\n" +
    "#EXTINF:-1,File scheme\r\n" +
    "file:///c:/secret.mp4\r\n" +
    "https://example.invalid/bare/stream.ts\r\n";

// ---- M3U parser
var parsed = M3uParser.Parse(Sample);
Check(parsed.Count == 4, $"parser count (got {parsed.Count})");
Check(parsed[0].Name == "Channel A" && parsed[0].Group == "News" && parsed[0].LogoUrl == "https://example.invalid/a.png", "parser attrs");
Check(parsed[0].Kind == StreamKind.Hls, "kind hls");
Check(parsed[1].Name == "Channel B, The Sequel" && parsed[1].Group == "Kids, Fun", "parser commas in names/groups");
Check(parsed[1].Kind == StreamKind.Hls, "kind hls with query");
Check(parsed[2].Kind == StreamKind.YouTube, "kind youtube");
Check(parsed[3].Name == "stream.ts" && parsed[3].Group == "Other" && parsed[3].Kind == StreamKind.Direct, "bare url fallback");
Check(parsed.All(c => c.Url.StartsWith("http")), "only http(s) accepted");
Check(M3uParser.Parse("").Count == 0 && M3uParser.Parse("garbage\n#EXTINF").Count == 0, "empty/garbage");
Check(M3uParser.Parse(Sample).Select(c => c.Id).SequenceEqual(parsed.Select(c => c.Id)), "stable ids");

// ---- Lineup
var lu = new ChannelLineup();
var tuned = new List<int>();
lu.Tuned += (_, n) => tuned.Add(n);
lu.Load(parsed);
Check(lu.Current is null && lu.CurrentNumber == 0, "no channel before tuning");
Check(lu.TuneNumber(1)?.Name == "Channel A", "tune 1");
Check(lu.Next()?.Name == "Channel B, The Sequel", "next");
Check(lu.Last()?.Name == "Channel A", "last");
Check(lu.Last()?.Name == "Channel B, The Sequel", "last toggles");
Check(lu.TuneNumber(4) is not null && lu.Next()?.Name == "Channel A", "next wraps");
Check(lu.Previous()?.Id == parsed[3].Id, "previous wraps");
Check(lu.TuneNumber(99) is null && lu.TuneNumber(0) is null && lu.TuneNumber(-3) is null, "out of range ignored");
Check(tuned.Count > 0, "Tuned event fires");

var t0 = DateTimeOffset.UtcNow;
Check(lu.PressDigit(2, t0) is null && lu.DigitBuffer == "2", "digit buffered");
Check(lu.Tick(t0 + TimeSpan.FromSeconds(1)) is null, "no commit before timeout");
Check(lu.Tick(t0 + TimeSpan.FromSeconds(2.1))?.Name == "Channel B, The Sequel" && lu.DigitBuffer == "", "commit after timeout");
lu.PressDigit(1, t0); 
Check(lu.PressDigit(2, t0 + TimeSpan.FromSeconds(0.5)) is null, "two digits, 12 invalid pending");
Check(lu.PressDigit(3, t0 + TimeSpan.FromSeconds(0.9)) is null && lu.DigitBuffer == "", "3 digits auto-commit, invalid ignored");
lu.PressDigit(3, t0); lu.PressDigit(1, t0 + TimeSpan.FromSeconds(5));
Check(lu.DigitBuffer == "1", "stale buffer reset by timeout");
lu.Commit();

Check(lu.ToggleFavorite(parsed[2].Id) && lu.Favorites.Count == 1, "favorite on");
Check(!lu.ToggleFavorite(parsed[2].Id) && lu.Favorites.Count == 0, "favorite off");
Check(!lu.ToggleFavorite("nope"), "favorite unknown id");
lu.ToggleFavorite(parsed[1].Id);
var state = lu.Export();
var lu2 = new ChannelLineup(); lu2.Load(parsed); lu2.Restore(state);
Check(lu2.Favorites.Single().Id == parsed[1].Id && lu2.Current?.Id == lu.Current?.Id, "state round trip");
Check(lu.Groups.SequenceEqual(new[] { "Kids, Fun", "News", "Other" }), "groups");

lu.TuneNumber(2);
lu.Load(parsed.Skip(1)); 
Check(lu.Current?.Id == parsed[1].Id && lu.CurrentNumber == 1, "reload keeps current channel, renumbers");

// ---- Pairing
var pairing = new PairingService();
var now = DateTimeOffset.UtcNow;
var pin = pairing.StartPairing(now);
Check(pin.Length == 6 && pin.All(char.IsDigit), "pin format");
Check(pairing.Complete("000000" == pin ? "111111" : "000000", "x", now) is null, "wrong pin rejected");
var ok = pairing.Complete(pin, "  Samsung A56  ", now);
Check(ok is not null && ok.Value.Device.Name == "Samsung A56", "pair success");
Check(pairing.Complete(pin, "again", now) is null, "pin single use");
Check(pairing.Validate(ok!.Value.Device.Id, ok.Value.Token), "token validates");
Check(!pairing.Validate(ok.Value.Device.Id, ok.Value.Token + "x") && !pairing.Validate("zz", ok.Value.Token) && !pairing.Validate(null, null), "bad creds rejected");
Check(!pairing.Export().Contains(ok.Value.Token), "token not stored in plain text");

var pin2 = pairing.StartPairing(now);
Check(pairing.Complete(pin2, "late", now + PairingService.PinLifetime + TimeSpan.FromSeconds(1)) is null, "pin expires");
var pin3 = pairing.StartPairing(now);
var wrong = pin3 == "123456" ? "654321" : "123456";
for (int i = 0; i < 5; i++) pairing.Complete(wrong, "x", now);
Check(pairing.Complete(pin3, "x", now) is null, "pin locked after 5 failures");

var p2 = pairing.StartPairing(now); var second = pairing.Complete(p2, "Tablet", now)!;
Check(pairing.Devices.Count == 2, "two devices at once");
pairing.SetBlocked(ok.Value.Device.Id, true);
Check(!pairing.Validate(ok.Value.Device.Id, ok.Value.Token), "blocked device rejected");
Check(pairing.Validate(second.Value.Device.Id, second.Value.Token), "other device unaffected by block");
pairing.SetBlocked(ok.Value.Device.Id, false);
Check(pairing.Validate(ok.Value.Device.Id, ok.Value.Token), "unblock");
var reload = new PairingService(); reload.Import(pairing.Export());
Check(reload.Validate(second.Value.Device.Id, second.Value.Token), "persisted devices validate");
Check(pairing.Revoke(second.Value.Device.Id) && !pairing.Validate(second.Value.Device.Id, second.Value.Token), "revoke");

// ---- Router
var host = new FakeHost();
var rl = new ChannelLineup(); rl.Load(parsed);
var router = new CommandRouter(rl, host);
Check(router.Handle(new(null, "channel.number", "2")).Ok && rl.CurrentNumber == 2, "router channel.number");
Check(router.Handle(new(null, "channel.next", null)).Ok && rl.CurrentNumber == 3, "router channel.next");
Check(!router.Handle(new(null, "channel.number", "abc")).Ok, "router bad number");
Check(!router.Handle(new(null, "channel.number", "50")).Ok, "router missing channel");
Check(router.Handle(new(null, "channel.favorite", null)).Ok && rl.Favorites.Count == 1, "router favorite");
Check(router.Handle(new(null, "media.volume", "40")).Ok && host.Last == "media.volume:40", "router volume");
Check(!router.Handle(new(null, "media.volume", "400")).Ok && !router.Handle(new(null, "media.volume", null)).Ok, "router volume bounds");
Check(!router.Handle(new(null, "media.seek", "99999")).Ok, "router seek bounds");
Check(router.Handle(new(null, "media.speed", "1.5")).Ok && !router.Handle(new(null, "media.speed", "9")).Ok, "router speed bounds");
Check(!router.Handle(new(null, "format.disk", "c:")).Ok && !router.Handle(new(null, "", null)).Ok, "router rejects unknown");
Check(!router.Handle(new(null, new string('x', 100), null)).Ok && !router.Handle(new(null, "nav.up", new string('1', 100))).Ok, "router length caps");
host.Throw = true;
Check(!router.Handle(new(null, "nav.up", null)).Ok, "router survives host exception");
host.Throw = false;

// ---- Server (loopback)
int port = 18700 + Random.Shared.Next(200);
var sp = new PairingService(); var sl = new ChannelLineup(); sl.Load(parsed);
var sh = new FakeHost();
await using (var server = new RemoteServer($"http://127.0.0.1:{port}/", sp, new CommandRouter(sl, sh), sl))
{
    server.Start();
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };

    var bad = await http.PostAsJsonAsync("pair", new { pin = "000000", name = "x" });
    Check(bad.StatusCode == HttpStatusCode.Forbidden, "server pair w/o pin = 403");

    var spin = sp.StartPairing(DateTimeOffset.UtcNow);
    var good = await http.PostAsJsonAsync("pair", new { pin = spin, name = "Phone" });
    Check(good.IsSuccessStatusCode, "server pair ok");
    var creds = await good.Content.ReadFromJsonAsync<JsonElement>();
    var dev = creds.GetProperty("deviceId").GetString()!; var tok = creds.GetProperty("token").GetString()!;

    var junk = await http.PostAsync("pair", new StringContent("not json", Encoding.UTF8, "application/json"));
    Check(junk.StatusCode == HttpStatusCode.Forbidden, "server junk body = 403");
    Check((await http.GetAsync("nothing")).StatusCode == HttpStatusCode.NotFound, "server 404");

    using (var badWs = new ClientWebSocket())
    {
        bool refused = false;
        try { await badWs.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?device={dev}&token=wrong"), CancellationToken.None); }
        catch { refused = true; }
        Check(refused, "ws bad token refused");
    }

    using var ws = new ClientWebSocket();
    await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?device={dev}&token={tok}"), CancellationToken.None);
    var first = JsonDocument.Parse(await Recv(ws));
    Check(first.RootElement.GetProperty("type").GetString() == "state", "ws initial state");

    await SendJson(ws, new { id = "1", cmd = "channel.next" });
    var m1 = JsonDocument.Parse(await Recv(ws)); var m2 = JsonDocument.Parse(await Recv(ws));
    var types = new[] { m1, m2 }.Select(d => d.RootElement.GetProperty("type").GetString()).OrderBy(x => x).ToArray();
    Check(types.SequenceEqual(new[] { "ack", "state" }), "ws ack + state broadcast");

    await SendJson(ws, new { id = "2", cmd = "media.volume", arg = "55" });
    var ack = JsonDocument.Parse(await Recv(ws));
    Check(ack.RootElement.GetProperty("ok").GetBoolean() && sh.Last == "media.volume:55", "ws host command executed");

    await SendJson(ws, new { id = "3", cmd = "power.nuke" });
    var rej = JsonDocument.Parse(await Recv(ws));
    Check(!rej.RootElement.GetProperty("ok").GetBoolean(), "ws unknown command rejected");

    sp.Revoke(dev);
    await SendJson(ws, new { id = "4", cmd = "nav.up" });
    bool closed = false;
    try { var buf = new byte[1024]; var r = await ws.ReceiveAsync(buf, new CancellationTokenSource(3000).Token); closed = r.MessageType == WebSocketMessageType.Close; }
    catch { closed = true; }
    Check(closed, "ws closed after revoke");
}

// ---- App launch plans (command construction only; actual launch must be verified on Windows)
var edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
var yt = AppLaunchPlans.YouTube(@"C:\KwartoTV\yt-profile", f => f == edge);
Check(yt is not null && yt.FileName == edge, "youtube uses installed browser");
Check(yt!.Args.Contains("--app=https://www.youtube.com/tv") && yt.Args.Contains("--start-fullscreen"), "youtube tv fullscreen app window");
Check(yt.Args.Any(a => a.StartsWith("--user-data-dir=") && a.Contains("yt-profile")), "youtube own profile");
Check(AppLaunchPlans.YouTube(@"C:\p", _ => false) is null, "youtube null when no browser");
var chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
Check(AppLaunchPlans.YouTube(@"C:\p", f => f == chrome)?.FileName == chrome, "falls back to chrome");
Check(AppLaunchPlans.Web("http://insecure.example/", @"C:\p", _ => true) is null, "web tile rejects http");
Check(AppLaunchPlans.Web("javascript:alert(1)", @"C:\p", _ => true) is null && AppLaunchPlans.Web("not a url", @"C:\p", _ => true) is null, "web tile rejects bad urls");
Check(AppLaunchPlans.Web("https://example.com/watch", @"C:\p", _ => true)!.Args[0] == "--app=https://example.com/watch", "web tile https ok");
var nf = AppLaunchPlans.Netflix();
Check(nf.FileName == "explorer.exe" && nf.Args.Single().StartsWith(@"shell:AppsFolder\"), "netflix store launch");

// ---- Playlist loader (local test server only)
Check(!PlaylistLoader.IsAllowedSource(new Uri("http://example.com/a.m3u")), "loader rejects remote http");
Check(PlaylistLoader.IsAllowedSource(new Uri("https://example.com/a.m3u")) && PlaylistLoader.IsAllowedSource(new Uri("http://127.0.0.1:1/a.m3u")), "loader allows https and loopback");
int lport = 18900 + Random.Shared.Next(100);
using (var ls = new HttpListener())
{
    ls.Prefixes.Add($"http://127.0.0.1:{lport}/");
    ls.Start();
    _ = Task.Run(async () =>
    {
        while (ls.IsListening)
        {
            HttpListenerContext c;
            try { c = await ls.GetContextAsync(); } catch { break; }
            if (c.Request.Url!.AbsolutePath == "/ok.m3u")
            {
                var bytes = Encoding.UTF8.GetBytes(Sample);
                await c.Response.OutputStream.WriteAsync(bytes);
            }
            else c.Response.StatusCode = 404;
            c.Response.Close();
        }
    });
    using var lh = new HttpClient();
    var loaded = await PlaylistLoader.LoadAsync($"http://127.0.0.1:{lport}/ok.m3u", lh);
    Check(loaded.Count == 4 && loaded[0].Name == "Channel A", "loader downloads and parses");
    bool failed404 = false;
    try { await PlaylistLoader.LoadAsync($"http://127.0.0.1:{lport}/missing.m3u", lh); } catch (HttpRequestException) { failed404 = true; }
    Check(failed404, "loader surfaces 404");
    bool badUrl = false;
    try { await PlaylistLoader.LoadAsync("file:///c:/x.m3u", lh); } catch (ArgumentException) { badUrl = true; }
    Check(badUrl, "loader rejects file urls");
    ls.Stop();
}

// ---- Settings, catalog, library, pairing link, Netflix lookup, mDNS, web remote
{
    var st = new AppSettings();
    Check(st.AddPlaylist("Mine", "https://example.invalid/a.m3u") is null, "settings add https playlist");
    Check(st.AddPlaylist("Mine", "https://example.invalid/a.m3u") is not null, "settings rejects duplicate playlist");
    Check(st.AddPlaylist("x", "http://example.com/a.m3u") is not null, "settings rejects remote http playlist");
    Check(st.AddPlaylist("x", "javascript:alert(1)") is not null && st.AddPlaylist("x", "relative/path.m3u") is not null, "settings rejects junk playlist");
    Check(st.AddPlaylist("Local", @"C:\tv\list.m3u8") is null || OperatingSystem.IsWindows() == false, "settings accepts rooted m3u path (windows form)");
    Check(st.AddWebTile("Site", "https://example.com/watch") is null && st.AddWebTile("Site", "http://example.com") is not null, "settings web tile https only");
    Check(st.AddWebTile("", "https://news.example.org/") is null && st.WebTiles.Last().Name == "news.example.org", "settings web tile default name");
    var sfile = Path.Combine(Path.GetTempPath(), "kw-" + Guid.NewGuid().ToString("N") + ".json");
    JsonFile.Save(sfile, st);
    var back = JsonFile.LoadOrNew<AppSettings>(sfile);
    Check(back.Playlists.Count == st.Playlists.Count && back.WebTiles.Count == 2 && back.Port == 8765, "settings json round trip");
    File.WriteAllText(sfile, "{ not json");
    Check(JsonFile.LoadOrNew<AppSettings>(sfile).Playlists.Count == 0, "settings corrupt file falls back to empty");
    File.Delete(sfile);
    Check(st.RemovePlaylist(st.Playlists[0].Id) && st.RemoveWebTile(st.WebTiles[0].Id), "settings remove");

    var dir = Path.Combine(Path.GetTempPath(), "kwlib-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(dir, "sub"));
    File.WriteAllText(Path.Combine(dir, "b.mp4"), "x"); File.WriteAllText(Path.Combine(dir, "sub", "a.mkv"), "x");
    File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
    File.WriteAllText(Path.Combine(dir, "list.m3u"), Sample);
    var lib = LibraryScanner.Scan(new[] { dir, Path.Combine(dir, "missing") });
    Check(lib.Count == 2 && lib[0].Title == "a" && lib.All(i => i.Path.EndsWith(".mp4") || i.Path.EndsWith(".mkv")), "library finds media only, recursively");
    Check(ChannelCatalog.LoadFile(Path.Combine(dir, "list.m3u")).Count == 4, "catalog loads local m3u");
    var gone = new PlaylistEntry("2", "Gone", Path.Combine(dir, "nope.m3u"));
    bool goneFailed = false; try { await ChannelCatalog.LoadOneAsync(gone, new HttpClient()); } catch (FileNotFoundException) { goneFailed = true; }
    var localLoaded = await ChannelCatalog.LoadOneAsync(new PlaylistEntry("1", "Local", Path.Combine(dir, "list.m3u")), new HttpClient());
    Check(localLoaded.Count == 4 && goneFailed, "catalog loads a local playlist and reports a missing one");
    Directory.Delete(dir, true);

    var link = PairLink.Build("192.168.1.20", 8765, "012345");
    Check(PairLink.TryParse(link, out var lh, out var lp, out var lpin) && lh == "192.168.1.20" && lp == 8765 && lpin == "012345", "pair link round trip");
    Check(!PairLink.TryParse("kwartotv://pair?host=a&port=1&pin=12", out _, out _, out _) && !PairLink.TryParse("https://evil/pair?host=a&port=1&pin=123456", out _, out _, out _)
          && !PairLink.TryParse("kwartotv://pair?host=a&port=99999&pin=123456", out _, out _, out _) && !PairLink.TryParse(null, out _, out _, out _), "pair link rejects bad input");

    const string one = "{\"Name\":\"Netflix\",\"AppID\":\"4DF9E0F8.Netflix_mcm4njqhnhss8!Netflix.App\"}";
    Check(NetflixLocator.Find(one) == "4DF9E0F8.Netflix_mcm4njqhnhss8!Netflix.App", "netflix locator single object");
    Check(NetflixLocator.Find("[{\"Name\":\"Other\",\"AppID\":\"x!y\"},{\"Name\":\"Netflix\",\"AppID\":\"Z.Netflix!App\"}]") == "Z.Netflix!App", "netflix locator array");
    Check(NetflixLocator.Find("") is null && NetflixLocator.Find("garbage") is null && NetflixLocator.Find("{\"Name\":\"Netflix\",\"AppID\":\"a b&calc\"}") is null, "netflix locator rejects junk and unsafe ids");
    Check(AppLaunchPlans.Netflix("Z.Netflix!App").Args.Single() == @"shell:AppsFolder\Z.Netflix!App", "netflix launch uses discovered id");

    // router: new commands
    var rh = new FakeHost(); var rlu = new ChannelLineup(); rlu.Load(parsed);
    var rr = new CommandRouter(rlu, rh);
    Check(rr.Handle(new(null, "key.text", "hello é")).Ok && rh.Last == "key.text:hello é", "router key.text");
    Check(!rr.Handle(new(null, "key.text", "a\nb")).Ok && !rr.Handle(new(null, "key.text", "")).Ok && !rr.Handle(new(null, "key.text", null)).Ok, "router key.text rejects control chars and empty");
    Check(rr.Handle(new(null, "media.volup", null)).Ok && rr.Handle(new(null, "app.youtube", null)).Ok && rr.Handle(new(null, "key.backspace", null)).Ok, "router volume, app and key commands");
    rlu.PressDigit(2, DateTimeOffset.UtcNow);
    Check(rr.Handle(new(null, "channel.commit", null)).Ok && rlu.CurrentNumber == 2, "router channel.commit tunes typed number");

    // mDNS
    var addrs = new[] { IPAddress.Parse("192.168.1.20") };
    var resp = MdnsPacket.BuildResponse("Living Room", "kwarto-pc", addrs, 8765);
    var recs = MdnsPacket.ParseRecords(resp);
    Check(recs.Any(r => r.Type == 12 && r.Name == "_kwartotv._tcp.local"), "mdns has PTR");
    var srv = recs.First(r => r.Type == 33);
    Check(srv.Name == "Living Room._kwartotv._tcp.local" && (srv.Data[4] << 8 | srv.Data[5]) == 8765, "mdns SRV carries name and port");
    Check(recs.Any(r => r.Type == 1 && r.Name == "kwarto-pc.local" && r.Data.SequenceEqual(new byte[] { 192, 168, 1, 20 })), "mdns has A record");
    Check(recs.Any(r => r.Type == 16), "mdns has TXT");
    var query = new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 }.Concat(new byte[] { 9 }).Concat(Encoding.ASCII.GetBytes("_kwartotv")).Concat(new byte[] { 4 }).Concat(Encoding.ASCII.GetBytes("_tcp"))
        .Concat(new byte[] { 5 }).Concat(Encoding.ASCII.GetBytes("local")).Concat(new byte[] { 0, 0, 12, 0, 1 }).ToArray();
    Check(MdnsPacket.ParseQuestions(query).SequenceEqual(new[] { "_kwartotv._tcp.local" }), "mdns parses question");
    using (var md = new MdnsResponder("Living Room", "kwarto-pc", 8765, () => addrs))
    {
        Check(md.Respond(query) is { } rb && MdnsPacket.ParseRecords(rb).Any(r => r.Type == 33), "mdns responds to our service query");
        var other = (byte[])query.Clone(); other[13] = (byte)'x';
        Check(md.Respond(other) is null, "mdns ignores other services");
        Check(md.Respond(resp) is null && md.Respond(new byte[] { 1, 2, 3 }) is null, "mdns ignores responses and garbage");
    }
    Check(MdnsPacket.SanitizeLabel("a.b\u0007c", "x") == "abc" && MdnsPacket.SanitizeLabel("  ", "fallback") == "fallback" && Encoding.UTF8.GetByteCount(MdnsPacket.SanitizeLabel(new string('é', 100), "x")) <= 60, "mdns label sanitising");

    // web remote page + /info + revoke drops socket
    int wport = 19100 + Random.Shared.Next(200);
    var wp = new PairingService(); var wl = new ChannelLineup(); wl.Load(parsed);
    await using (var ws2 = new RemoteServer($"http://127.0.0.1:{wport}/", wp, new CommandRouter(wl, new FakeHost()), wl) { ServerName = "Test TV" })
    {
        ws2.Start();
        using var hc = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{wport}/") };
        var page = await hc.GetAsync("/");
        var html = await page.Content.ReadAsStringAsync();
        Check(page.IsSuccessStatusCode && html.Contains("KwartoTV") && html.Contains("/pair") && page.Content.Headers.ContentType?.MediaType == "text/html", "server serves web remote page");
        Check(page.Headers.Contains("Content-Security-Policy"), "web remote has CSP");
        Check((await hc.GetStringAsync("/info")).Contains("Test TV"), "server /info");
        var pinW = wp.StartPairing(DateTimeOffset.UtcNow);
        var pr = await hc.PostAsJsonAsync("/pair", new { pin = pinW, name = "Phone" });
        var cred = await pr.Content.ReadFromJsonAsync<JsonElement>();
        using var cws = new ClientWebSocket();
        await cws.ConnectAsync(new Uri($"ws://127.0.0.1:{wport}/ws?device={cred.GetProperty("deviceId").GetString()}&token={cred.GetProperty("token").GetString()}"), CancellationToken.None);
        await Recv(cws);
        wp.SetBlocked(cred.GetProperty("deviceId").GetString()!, true);
        using var cts2 = new CancellationTokenSource(5000);
        var closeBuf = new byte[256]; WebSocketReceiveResult? cr = null;
        try { cr = await cws.ReceiveAsync(closeBuf, cts2.Token); } catch { }
        Check(cr?.MessageType == WebSocketMessageType.Close || cws.State is WebSocketState.CloseReceived or WebSocketState.Aborted, "blocking a device disconnects it immediately");
    }
    bool changed = false; var cp = new PairingService(); cp.Changed += () => changed = true;
    cp.Complete(cp.StartPairing(DateTimeOffset.UtcNow), "x", DateTimeOffset.UtcNow);
    Check(changed, "pairing raises Changed");
}

// ---- Host key map
Check(HostKeyMap.Navigation("nav.back", ForegroundKind.YouTube)![0].Vk == HostKeyMap.VkEscape && HostKeyMap.Navigation("nav.back", ForegroundKind.KwartoTV)![0].Vk == HostKeyMap.VkEscape, "back is Esc in YouTube and in the app");
Check(HostKeyMap.Navigation("nav.back", ForegroundKind.Netflix)![0] is { Vk: HostKeyMap.VkLeft, Alt: true } && HostKeyMap.Navigation("nav.back", ForegroundKind.Web)![0].Alt, "back is Alt+Left in Netflix and websites");
Check(HostKeyMap.Navigation("nav.up", ForegroundKind.KwartoTV)![0] is { Vk: HostKeyMap.VkUp, Extended: true } && HostKeyMap.Navigation("nav.ok", ForegroundKind.KwartoTV)![0].Vk == HostKeyMap.VkReturn, "arrows are extended keys");
Check(HostKeyMap.Navigation("media.mute", ForegroundKind.KwartoTV) is null && HostKeyMap.Media("media.mute")![0].Vk == HostKeyMap.VkVolumeMute && HostKeyMap.Media("media.next")![0].Vk == HostKeyMap.VkMediaNext, "media key mapping");
Check(HostKeyMap.Seek(-30).Count == 3 && HostKeyMap.Seek(-30)[0].Vk == HostKeyMap.VkLeft && HostKeyMap.Seek(5).Count == 1 && HostKeyMap.Seek(3600).Count == 30, "seek presses");
var vt = HostKeyMap.VolumeTo(40);
Check(vt.Count(k => k.Vk == HostKeyMap.VkVolumeDown) == 50 && vt.Count(k => k.Vk == HostKeyMap.VkVolumeUp) == 20 && HostKeyMap.VolumeTo(500).Count(k => k.Vk == HostKeyMap.VkVolumeUp) == 50, "volume to target");

// ---- LAN address picking (virtual adapters last)
{
    var vm = new LanAddressPicker.Candidate("VMware Network Adapter VMnet1", "VMware Virtual Ethernet Adapter", IPAddress.Parse("192.168.164.1"), false);
    var wifi = new LanAddressPicker.Candidate("Wi-Fi", "Intel(R) Wi-Fi 6 AX201", IPAddress.Parse("192.168.1.45"), true);
    var wsl = new LanAddressPicker.Candidate("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", IPAddress.Parse("172.29.64.1"), false);
    var apipa = new LanAddressPicker.Candidate("Ethernet", "Realtek", IPAddress.Parse("169.254.3.4"), false);
    var ranked = LanAddressPicker.Rank(new[] { vm, wsl, apipa, wifi });
    Check(ranked[0].ToString() == "192.168.1.45" && ranked.Count == 3 && ranked.Last().ToString() != "169.254.3.4", "real Wi-Fi address is listed before VMware/WSL, link-local dropped");
    Check(LanAddressPicker.Rank(new[] { vm }).Count == 1, "a PC with only a virtual adapter still gets an address");
}

// ---- TCP server: prefix parsing, chunked bodies, listening on all cards like the real app
{
    Check(RemoteServer.ParsePrefix("http://+:8765/").Equals((IPAddress.Any, 8765)) && RemoteServer.ParsePrefix("http://127.0.0.1:9/").Equals((IPAddress.Loopback, 9)), "server prefix parsing");
    bool badPrefix = false; try { RemoteServer.ParsePrefix("http://+/"); } catch (ArgumentException) { badPrefix = true; }
    Check(badPrefix, "server prefix needs a port");
    Check(RemoteServer.DecodeChunked(Encoding.ASCII.GetBytes("4\r\nWiki\r\n5\r\npedia\r\n0\r\n\r\n"), out var cb) == 1 && Encoding.ASCII.GetString(cb) == "Wikipedia", "chunked body decodes");
    Check(RemoteServer.DecodeChunked(Encoding.ASCII.GetBytes("4\r\nWi"), out _) == 0 && RemoteServer.DecodeChunked(Encoding.ASCII.GetBytes("zz\r\n"), out _) == -1, "chunked body: partial waits, garbage rejected");
    Check(RemoteServer.DecodeChunked(Encoding.ASCII.GetBytes("FFFF\r\n"), out _) == -1, "chunked body size cap");

    int aport = 19400 + Random.Shared.Next(200);
    var ap = new PairingService(); var al = new ChannelLineup(); al.Load(parsed);
    await using (var anyServer = new RemoteServer($"http://+:{aport}/", ap, new CommandRouter(al, new FakeHost()), al))
    {
        anyServer.Start();
        using var hc = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{aport}/") };
        Check((await hc.GetStringAsync("/info")).Contains("KwartoTV"), "server on all cards answers (no URL reservation needed)");
        var lan = MdnsResponder.LocalAddresses().FirstOrDefault();
        if (lan is not null)
        {
            using var lc = new HttpClient { BaseAddress = new Uri($"http://{lan}:{aport}/"), Timeout = TimeSpan.FromSeconds(5) };
            Check((await lc.GetStringAsync("/info")).Contains("KwartoTV"), $"server answers on the LAN address {lan}");
        }
        var pinA = ap.StartPairing(DateTimeOffset.UtcNow);
        var body = new StringContent($"{{\"pin\":\"{pinA}\",\"name\":\"Fixed length\"}}", Encoding.UTF8, "application/json");
        var resp = await hc.PostAsync("/pair", body);
        Check(resp.IsSuccessStatusCode, "pairing with a Content-Length body (like the Android app)");
        var big = await hc.PostAsync("/pair", new StringContent(new string('x', 5000), Encoding.UTF8, "application/json"));
        Check((int)big.StatusCode == 413, "oversized pairing body refused");
        Check((int)(await hc.GetAsync("/nope")).StatusCode == 404, "unknown path 404");
        using var raw = new System.Net.Sockets.TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, aport);
        await raw.GetStream().WriteAsync("garbage\r\n\r\n"u8.ToArray());
        var rb = new byte[64]; int rn = await raw.GetStream().ReadAsync(rb);
        Check(Encoding.ASCII.GetString(rb, 0, rn).StartsWith("HTTP/1.1 400"), "garbage request gets 400");
        // the port is now taken: a second server must fail loudly, not silently
        bool taken = false;
        await using (var secondServer = new RemoteServer($"http://+:{aport}/", ap, new CommandRouter(al, new FakeHost()), al))
        { try { secondServer.Start(); } catch (System.Net.Sockets.SocketException) { taken = true; } }
        Check(taken || !OperatingSystem.IsWindows(), "port already in use is reported");
    }
}

Check(RemoteServer.PortsToTry(18765).SequenceEqual(new[] { 18765, 8765, 8766, 8767, 8768, 8769, 28765, 38765, 48765, 58765 }) && RemoteServer.PortsToTry(8765).First() == 8765 && RemoteServer.PortsToTry(8765).Distinct().Count() == RemoteServer.PortsToTry(8765).Count, "port fallback order: last used first, no duplicates");

// ---- iptv-org links, #EXTVLCOPT headers, mouse commands, defaults
{
    Check(IptvOrg.CountryUrl("PH") == "https://iptv-org.github.io/iptv/countries/ph.m3u" && IptvOrg.CategoryUrl("News") == "https://iptv-org.github.io/iptv/categories/news.m3u", "iptv-org playlist links");
    Check(IptvOrg.BuiltInCountries.Any(c => c.Code == "PH" && c.Name == "Philippines") && IptvOrg.Categories.Contains("news") && IptvOrg.Categories.Contains("kids"), "iptv-org built-in country and category names");
    var cs = IptvOrg.ParseCountries("[{\"name\":\"Philippines\",\"code\":\"PH\",\"flag\":\"x\"},{\"name\":\"Bad\"},{\"code\":\"US\",\"name\":\"United States\"}]");
    Check(cs.Count == 2 && cs[0].Code == "PH", "iptv-org countries.json parses and skips bad rows");
    Check(IptvOrg.ParseCountries("not json").Count == 0 && IptvOrg.ParseCountries("{}").Count == 0, "iptv-org countries.json garbage gives empty list");
    var sug = IptvOrg.CountrySuggestions(IptvOrg.BuiltInCountries);
    Check(sug.All(s => AppSettings.IsHttpsUrl(s.Url)) && sug.First().Name.StartsWith("A"), "iptv-org suggestions are https and sorted");
    Check(IptvOrg.Filter(sug, "phil").Single().Name == "Philippines" && IptvOrg.Filter(sug, "").Count == sug.Count, "iptv-org filter by typed text");

    const string withOpts = "#EXTM3U\n#EXTINF:-1 group-title=\"News\",Needs Headers\n#EXTVLCOPT:http-referrer=https://example.invalid/\n#EXTVLCOPT:http-user-agent=Custom UA/1.0\nhttps://example.invalid/h.m3u8\n#EXTINF:-1,Plain\nhttps://example.invalid/p.m3u8\n";
    var wo = M3uParser.Parse(withOpts);
    Check(wo[0].Referrer == "https://example.invalid/" && wo[0].UserAgent == "Custom UA/1.0", "EXTVLCOPT referrer and user agent are kept");
    Check(wo[1].Referrer is null && wo[1].UserAgent is null, "EXTVLCOPT options do not leak into the next channel");

    var mh = new FakeHost(); var mr = new CommandRouter(new ChannelLineup(), mh);
    Check(mr.Handle(new(null, "mouse.move", "12,-30")).Ok && mh.Last == "mouse.move:12,-30" && mr.Handle(new(null, "mouse.click", null)).Ok && mr.Handle(new(null, "mouse.scroll", "-3")).Ok, "router mouse commands");
    Check(!mr.Handle(new(null, "mouse.move", "9999,0")).Ok && !mr.Handle(new(null, "mouse.move", "abc")).Ok && !mr.Handle(new(null, "mouse.scroll", "0")).Ok && !mr.Handle(new(null, "mouse.scroll", "99")).Ok, "router mouse bounds");
    var defaults = new AppSettings();
    Check(defaults.NetflixInApp, "defaults: Netflix opens in the app");
}

// ---- updates
{
    var h = new string('a', 64);
    string Manifest(string exeUrl = "https://github.com/Ronanski/game-dev-era/releases/download/latest/KwartoTV.exe", string sha = null!) =>
        "{\"build\":42,\"version\":\"1.0.42\",\"exe\":{\"url\":\"" + exeUrl + "\",\"sha256\":\"" + (sha ?? h) + "\"},\"apk\":{\"url\":\"https://github.com/Ronanski/game-dev-era/releases/download/latest/KwartoTV-Remote.apk\",\"sha256\":\"" + h + "\",\"versionCode\":42},\"notes\":\"x\"}";
    var ui = Updates.Parse(Manifest());
    Check(ui is { Build: 42, Version: "1.0.42" } && ui.Apk.VersionCode == 42 && ui.Exe.Sha256 == h, "update manifest parses");
    Check(Updates.IsNewer(ui!, 41) && !Updates.IsNewer(ui!, 42) && !Updates.IsNewer(ui!, 100), "update is newer only when the build number is higher");
    Check(Updates.Parse(Manifest(exeUrl: "https://evil.example/KwartoTV.exe")) is null, "update manifest refuses other hosts");
    Check(Updates.Parse(Manifest(exeUrl: "http://github.com/Ronanski/game-dev-era/releases/download/latest/KwartoTV.exe")) is null, "update manifest refuses http");
    Check(Updates.Parse(Manifest(sha: "zz")) is null && Updates.Parse("{}") is null && Updates.Parse("junk") is null && Updates.Parse(null) is null, "update manifest refuses bad checksum and garbage");

    // download + checksum against a local server (the host check is bypassed by testing the checksum logic on a loopback file)
    var dir = Path.Combine(Path.GetTempPath(), "kwupd-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    var f = Path.Combine(dir, "a.bin"); File.WriteAllBytes(f, new byte[] { 1, 2, 3, 4 });
    Check(Updates.Sha256Of(f) == System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3, 4 }).Aggregate("", (s, b) => s + b.ToString("x2")), "sha256 of a file");
    bool refused = false;
    try { await Updates.DownloadAsync(new UpdateFile("http://127.0.0.1:1/x", h), Path.Combine(dir, "x"), new HttpClient()); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "download refuses addresses outside the project releases");
    Directory.Delete(dir, true);
}

// ---- resilient download: mirrors, timeouts, readable errors
{
    int mport = 19700 + Random.Shared.Next(200);
    using var ml = new HttpListener();
    ml.Prefixes.Add($"http://127.0.0.1:{mport}/");
    ml.Start();
    _ = Task.Run(async () =>
    {
        while (ml.IsListening)
        {
            HttpListenerContext c;
            try { c = await ml.GetContextAsync(); } catch { break; }
            _ = Task.Run(async () =>
            {
                var path = c.Request.Url!.AbsolutePath;
                if (path == "/slow.m3u") { await Task.Delay(5000); try { c.Response.Close(); } catch { } return; }
                if (path == "/mirror.m3u") { var b = Encoding.UTF8.GetBytes(Sample); await c.Response.OutputStream.WriteAsync(b); }
                else if (path == "/empty.m3u") { }
                else c.Response.StatusCode = 503;
                c.Response.Close();
            });
        }
    });
    using var rh = ResilientHttp.Create(TimeSpan.FromSeconds(30));
    string Main(string p) => $"http://127.0.0.1:{mport}{p}";

    var viaMirror = await PlaylistLoader.LoadAsync(Main("/slow.m3u"), rh, _ => new[] { Main("/slow.m3u"), Main("/down.m3u"), Main("/mirror.m3u") }, TimeSpan.FromMilliseconds(700));
    Check(viaMirror.Count == 4, "download falls back to the next address when the first one hangs or fails");

    string err = "";
    try { await PlaylistLoader.LoadAsync(Main("/slow.m3u"), rh, _ => new[] { Main("/slow.m3u"), Main("/down.m3u"), Main("/empty.m3u") }, TimeSpan.FromMilliseconds(500)); } catch (HttpRequestException ex) { err = ex.Message; }
    Check(err.Contains("no answer in") && err.Contains("server said 503") && err.Contains("the list is empty") && err.Contains("127.0.0.1"), "download error says what happened at every address: " + err);

    var direct = await PlaylistLoader.LoadAsync(Main("/mirror.m3u"), rh);
    Check(direct.Count == 4, "download works straight away from the main address");

    var mirrors = IptvOrg.Mirrors(IptvOrg.CountryUrl("PH"));
    Check(mirrors[0] == IptvOrg.CountryUrl("PH") && mirrors.Count >= 3 && mirrors.Skip(1).All(m => m.StartsWith("https://") && m.EndsWith("countries/ph.m3u")), "iptv-org playlists have https mirrors");
    Check(IptvOrg.Mirrors("https://example.com/a.m3u").Count == 1, "other playlists have no mirrors");
    ml.Stop();
}

// ---- the user chooses channels: selection, single streams, relative links, other schemes
{
    var all = M3uParser.Parse(Sample);
    var pl = new PlaylistEntry("p1", "One", "https://example.invalid/one.m3u", new List<string> { all[1].Id, all[3].Id });
    var pl2 = new PlaylistEntry("p2", "Two", "https://example.invalid/two.m3u", null);   // null = every channel
    var loaded = new Dictionary<string, IReadOnlyList<Channel>> { ["p1"] = all, ["p2"] = all.Take(2).ToList() };
    var lineupList = ChannelCatalog.BuildLineup(new[] { pl, pl2 }, loaded);
    Check(lineupList.Count == 3 && lineupList[0].Id == all[1].Id && lineupList[1].Id == all[3].Id && lineupList[2].Id == all[0].Id, "lineup has only the selected channels, then all of a playlist with no selection, no duplicates");
    Check(ChannelCatalog.BuildLineup(new[] { pl with { Selected = new List<string>() } }, loaded).Count == 0, "an empty selection gives no channels (the user has not chosen yet)");
    Check(ChannelCatalog.BuildLineup(new[] { pl }, new Dictionary<string, IReadOnlyList<Channel>>()).Count == 0, "playlist that is not loaded yet adds nothing");

    var st = new AppSettings();
    Check(st.AddPlaylist("L", "https://example.invalid/l.m3u") is null && st.Playlists[0].Selected is { Count: 0 }, "a new playlist starts with nothing selected");
    st.SetSelection(st.Playlists[0].Id, new[] { "a", "b", "a" });
    Check(st.Playlists[0].Selected!.SequenceEqual(new[] { "a", "b" }), "selection is saved without duplicates");
    var sj = Path.Combine(Path.GetTempPath(), "kwsel-" + Guid.NewGuid().ToString("N") + ".json");
    JsonFile.Save(sj, st);
    Check(JsonFile.LoadOrNew<AppSettings>(sj).Playlists[0].Selected!.SequenceEqual(new[] { "a", "b" }), "selection survives saving and loading"); File.Delete(sj);
    Check(st.AddStream("News", "https://example.invalid/live/stream.m3u8") is null && st.Playlists[1].IsStream && st.Playlists[1].Name == "News", "a single stream link can be added");
    Check(st.AddStream("x", "file:///c:/a.mp4") is not null && st.AddStream("x", "javascript:alert(1)") is not null && st.AddStream("x", "https://example.invalid/live/stream.m3u8") is not null, "single stream rejects bad schemes and duplicates");
    Check(st.AddStream("R", "rtmp://example.invalid/live/ch1") is null, "rtmp stream links are accepted");
    var one = await ChannelCatalog.LoadOneAsync(st.Playlists[1], new HttpClient());
    Check(one.Count == 1 && one[0].Url == "https://example.invalid/live/stream.m3u8", "a stream entry loads as one channel without downloading");

    var withSchemes = M3uParser.Parse("#EXTM3U\n#EXTINF:-1,R\nrtmp://example.invalid/a\n#EXTINF:-1,S\nrtsp://example.invalid/b\n#EXTINF:-1,U\nudp://@239.0.0.1:1234\n#EXTINF:-1,F\nfile:///etc/passwd\n#EXTINF:-1,J\njavascript:alert(1)\n");
    Check(withSchemes.Count == 3 && withSchemes.All(c => c.Url.Contains("example.invalid") || c.Url.StartsWith("udp:")), "parser accepts rtmp, rtsp and udp and still drops file: and javascript:");
    var rel = M3uParser.Parse("#EXTM3U\n#EXTINF:-1,Rel\nstreams/ch1.m3u8\n#EXTINF:-1,Abs\n/live/ch2.m3u8\n", new Uri("https://example.invalid/lists/all.m3u"));
    Check(rel.Count == 2 && rel[0].Url == "https://example.invalid/lists/streams/ch1.m3u8" && rel[1].Url == "https://example.invalid/live/ch2.m3u8", "relative stream addresses are resolved against the playlist link");

    const string hlsMaster = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360\nlow/index.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=2000000\nhigh/index.m3u8\n";
    var asStream = PlaylistLoader.Interpret(hlsMaster, new Uri("https://example.invalid/live/news/master.m3u8"));
    Check(asStream.Count == 1 && asStream[0].Name == "master" && asStream[0].Url == "https://example.invalid/live/news/master.m3u8", "a link that is itself an HLS stream becomes one channel");
    Check(PlaylistLoader.Interpret(Sample, new Uri("https://example.invalid/x.m3u")).Count == 4, "a normal channel list is still a list");

    var viaFallback = await PlaylistLoader.LoadAsync("https://127.0.0.1:1/never.m3u", new HttpClient { Timeout = TimeSpan.FromSeconds(3) }, _ => new[] { "https://127.0.0.1:1/never.m3u" }, TimeSpan.FromSeconds(2), default, (u, ct) => Task.FromResult<string?>(Sample));
    Check(viaFallback.Count == 4, "when direct download fails the system download fallback is used");

    Check(IptvOrg.AllChannels.Url.EndsWith("/index.m3u") && IptvOrg.LanguageUrl("ENG").EndsWith("/languages/eng.m3u") && IptvOrg.LanguageSuggestions(IptvOrg.BuiltInLanguages).Any(l => l.Name == "Tagalog"), "iptv-org: all channels and languages");
}

Check(DohResolver.ParseAnswer("{\"Status\":0,\"Answer\":[{\"name\":\"x\",\"type\":5,\"data\":\"y.\"},{\"name\":\"x\",\"type\":1,\"data\":\"185.199.108.153\"},{\"type\":1,\"data\":\"bad\"}]}").Select(i => i.ToString()).SequenceEqual(new[] { "185.199.108.153" }), "DNS-over-HTTPS answer keeps only valid A records");
Check(DohResolver.ParseAnswer("{}").Count == 0 && DohResolver.ParseAnswer("junk").Count == 0, "DNS-over-HTTPS garbage gives no addresses");

{
    var lh = new FakeHost(); var lr = new CommandRouter(new ChannelLineup(), lh);
    Check(lr.Handle(new(null, "channel.list", null)).Ok && lh.Last == "channel.list:", "router channel.list reaches the host");
}

// ---- channel checks: blocked, gone, web pages, empty, and good HLS
{
    int hport = 19950 + Random.Shared.Next(40);
    using var hl = new HttpListener();
    hl.Prefixes.Add($"http://127.0.0.1:{hport}/");
    hl.Start();
    _ = Task.Run(async () =>
    {
        while (hl.IsListening)
        {
            HttpListenerContext c;
            try { c = await hl.GetContextAsync(); } catch { break; }
            _ = Task.Run(async () =>
            {
                var path = c.Request.Url!.AbsolutePath;
                string? body = path switch
                {
                    "/good/master.m3u8" => "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1\nlow/index.m3u8\n",
                    "/good/low/index.m3u8" => "#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\nseg1.ts\n",
                    "/nosegment/index.m3u8" => "#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\nmissing.ts\n",
                    "/empty/index.m3u8" => "#EXTM3U\n#EXT-X-TARGETDURATION:6\n",
                    "/signup" => "<html><body>Please register</body></html>",
                    _ => null,
                };
                if (path == "/good/low/seg1.ts" || path == "/direct.ts") { await c.Response.OutputStream.WriteAsync(new byte[] { 0x47, 1, 2, 3, 4 }); }
                else if (path == "/blocked.m3u8") c.Response.StatusCode = 403;
                else if (path == "/geo.m3u8") c.Response.StatusCode = 451;
                else if (path == "/needsref.m3u8") { if (c.Request.Headers["Referer"] == "https://ok.example/") { var b = Encoding.UTF8.GetBytes("#EXTM3U\n#EXTINF:6,\n/direct.ts\n"); await c.Response.OutputStream.WriteAsync(b); } else c.Response.StatusCode = 403; }
                else if (body is not null) { if (path == "/signup") c.Response.ContentType = "text/html"; var b = Encoding.UTF8.GetBytes(body); await c.Response.OutputStream.WriteAsync(b); }
                else c.Response.StatusCode = 404;
                c.Response.Close();
            });
        }
    });
    using var ph = new HttpClient();
    var nowH = DateTimeOffset.UtcNow;
    Channel Ch(string path, string? referrer = null) => new(Channel.StableId(path), path, $"http://127.0.0.1:{hport}{path}", null, "T", StreamKind.Hls, referrer);
    async Task<ChannelHealth> Probe(string path, string? referrer = null) => await StreamProbe.CheckAsync(Ch(path, referrer), ph, nowH);

    Check((await Probe("/good/master.m3u8")).Status == HealthStatus.Ok, "check: HLS master -> variant -> segment works");
    Check((await Probe("/direct.ts")).Status == HealthStatus.Ok, "check: a direct video stream works");
    var blocked = await Probe("/blocked.m3u8");
    Check(blocked.Status == HealthStatus.Dead && blocked.Reason!.Contains("403"), "check: 403 is blocked / needs an account");
    Check((await Probe("/geo.m3u8")).Reason!.Contains("not available in your country"), "check: 451 is a country block");
    Check((await Probe("/gone.m3u8")).Reason!.Contains("no longer exists"), "check: 404 is gone");
    Check((await Probe("/signup")).Reason!.Contains("web page"), "check: a sign-up web page is not a stream");
    Check((await Probe("/nosegment/index.m3u8")).Reason!.Contains("no longer exists"), "check: playlist whose video segments are missing is dead");
    Check((await Probe("/empty/index.m3u8")).Reason!.Contains("empty"), "check: empty playlist means nothing on air");
    Check((await Probe("/needsref.m3u8")).Status == HealthStatus.Dead && (await Probe("/needsref.m3u8", "https://ok.example/")).Status == HealthStatus.Ok, "check: uses the playlist's referrer like the player does");
    var rtmp = await StreamProbe.CheckAsync(new Channel("r", "r", "rtmp://example.invalid/live", null, "T", StreamKind.Direct), ph, nowH);
    Check(rtmp.Status == HealthStatus.Unknown, "check: rtmp cannot be checked ahead and is kept");
    hl.Stop();

    var two = M3uParser.Parse(Sample).Take(2).ToList();
    var hp = new PlaylistEntry("h", "H", "x", null);
    var healthMap = new Dictionary<string, ChannelHealth> { [two[0].Id] = new(HealthStatus.Dead, "403", nowH), [two[1].Id] = new(HealthStatus.Ok, null, nowH) };
    var loadedH = new Dictionary<string, IReadOnlyList<Channel>> { ["h"] = two };
    Check(ChannelCatalog.BuildLineup(new[] { hp }, loadedH, healthMap).Single().Id == two[1].Id, "lineup leaves out channels that do not work here");
    Check(ChannelCatalog.BuildLineup(new[] { hp }, loadedH, null).Count == 2, "lineup keeps everything when unavailable channels are shown");
    Check(new AppSettings().HideUnavailable, "unavailable channels are hidden by default");
}

Console.WriteLine($"{pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;

static async Task SendJson(ClientWebSocket ws, object o) =>
    await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(o, new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }),
        WebSocketMessageType.Text, true, CancellationToken.None);

static async Task<string> Recv(ClientWebSocket ws)
{
    var buf = new byte[4096];
    using var cts = new CancellationTokenSource(5000);
    var r = await ws.ReceiveAsync(buf, cts.Token);
    return Encoding.UTF8.GetString(buf, 0, r.Count);
}

sealed class FakeHost : IHostActions
{
    public string Last = ""; public bool Throw;
    public void Execute(string cmd, string? arg) { if (Throw) throw new InvalidOperationException(); Last = $"{cmd}:{arg}"; }
}
