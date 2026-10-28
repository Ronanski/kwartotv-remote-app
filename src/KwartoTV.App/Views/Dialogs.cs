using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KwartoTV.App.Services;
using KwartoTV.App.Ui;
using KwartoTV.Core;
using QRCoder;

namespace KwartoTV.App.Views;

/// <summary>Modal forms shown over any page. Text fields accept the phone remote's keyboard.</summary>
public static class Dialogs
{
    public sealed record Field(string Label, string Initial = "", Func<string?>? Browse = null);

    /// <summary>Generic form. onSave returns an error message to keep the dialog open, or null to close it.</summary>
    public static void Form(MainWindow w, string title, string hint, IReadOnlyList<Field> fields, string saveLabel, Func<string[], string?> onSave)
    {
        var panel = new StackPanel { Width = 760 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 32, FontWeight = FontWeights.Bold });
        panel.Children.Add(UiKit.Dimmed(hint, 18));
        var boxes = new List<TextBox>();
        foreach (var f in fields)
        {
            panel.Children.Add(new TextBlock { Text = f.Label, FontSize = 18, Foreground = UiKit.B("Dim"), Margin = new Thickness(0, 18, 0, 6) });
            var row = new DockPanel();
            var box = new TextBox { Text = f.Initial, Style = (Style)Application.Current.FindResource("Field") };
            box.CaretIndex = box.Text.Length;
            if (f.Browse is not null)
            {
                var b = UiKit.ActionButton("Browse…", () => { var p = f.Browse(); if (p is not null) box.Text = p; });
                DockPanel.SetDock(b, Dock.Right); b.Margin = new Thickness(12, 0, 0, 0);
                row.Children.Add(b);
            }
            row.Children.Add(box);
            boxes.Add(box);
            panel.Children.Add(row);
        }
        var error = new TextBlock { FontSize = 18, Foreground = UiKit.B("Danger"), Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
        void Save()
        {
            var err = onSave(boxes.Select(b => b.Text).ToArray());
            if (err is null) w.CloseDialog(); else error.Text = err;
        }
        buttons.Children.Add(UiKit.ActionButton(saveLabel, Save));
        buttons.Children.Add(UiKit.ActionButton("Cancel", w.CloseDialog));
        panel.Children.Add(buttons);
        // OK/Enter on a field opens the on-screen keyboard (a physical keyboard can still be typed on directly).
        foreach (var b in boxes) b.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { w.ShowKeyboard(b); e.Handled = true; } };
        panel.Children.Insert(2, UiKit.Dimmed("Select a box and press OK to type with the on-screen keyboard.", 15));
        w.ShowDialog(panel, boxes.FirstOrDefault());
    }

    public static void Confirm(MainWindow w, string text, string yesLabel, Action onYes)
    {
        var panel = new StackPanel { Width = 640 };
        panel.Children.Add(new TextBlock { Text = text, FontSize = 28, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 26, 0, 0) };
        buttons.Children.Add(UiKit.ActionButton("Cancel", w.CloseDialog));
        buttons.Children.Add(UiKit.ActionButton(yesLabel, () => { w.CloseDialog(); onYes(); }, danger: true));
        panel.Children.Add(buttons);
        w.ShowDialog(panel);
    }

    private static readonly HttpClient Web = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>
    /// Pick a public playlist from iptv-org (github.com/iptv-org/iptv) by country or category. Type to filter.
    /// Nothing is bundled: this only fills in the playlist link, which is downloaded like any other.
    /// </summary>
    public static void IptvOrgPicker(MainWindow w, Action<PlaylistSuggestion> onPick)
    {
        var countries = IptvOrg.CountrySuggestions(IptvOrg.BuiltInCountries);
        var categories = IptvOrg.CategorySuggestions();
        bool showCountries = true;

        var panel = new StackPanel { Width = 820 };
        panel.Children.Add(new TextBlock { Text = "Add channels from iptv-org", FontSize = 32, FontWeight = FontWeights.Bold });
        panel.Children.Add(UiKit.Dimmed("Public playlists from github.com/iptv-org/iptv. KwartoTV includes no channels: you pick a list and it is downloaded to this PC. Some streams may be offline or blocked in your country.", 17));

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 6) };
        var filter = new TextBox { Style = (Style)Application.Current.FindResource("Field"), Margin = new Thickness(0, 4, 0, 8) };
        var results = new StackPanel();
        var shown = new TextBlock { FontSize = 15, Foreground = UiKit.B("Dim"), Margin = new Thickness(0, 6, 0, 0) };

        void Render()
        {
            results.Children.Clear();
            var matches = IptvOrg.Filter(showCountries ? countries : categories, filter.Text);
            foreach (var m in matches.Take(8))
            {
                var pick = m;
                var b = new Button { Style = (Style)Application.Current.FindResource("Pill"), Tag = "action", HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 2) };
                b.Content = new TextBlock { Text = pick.Name, FontSize = 22 };
                b.Click += (_, _) => { w.CloseDialog(); onPick(pick); };
                results.Children.Add(b);
            }
            shown.Text = matches.Count == 0 ? "Nothing matches. Check the spelling." : matches.Count > 8 ? $"Showing 8 of {matches.Count}. Type more letters to narrow it down." : "";
        }

        Button TabButton(string label, bool countriesTab)
        {
            var b = UiKit.ActionButton(label, () => { showCountries = countriesTab; filter.Clear(); Render(); });
            return b;
        }
        tabs.Children.Add(TabButton("Countries", true));
        tabs.Children.Add(TabButton("Categories", false));
        filter.TextChanged += (_, _) => Render();
        // Enter picks the first match; Down moves into the list (a text box would otherwise keep both keys).
        filter.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && filter.Text.Length == 0) { w.ShowKeyboard(filter); e.Handled = true; return; }
            if (results.Children.Count == 0 || results.Children[0] is not Button first) return;
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (filter.Text.Length == 0) w.ShowKeyboard(filter); else first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Down) { first.Focus(); e.Handled = true; }
        };
        panel.Children.Add(tabs);
        panel.Children.Add(new TextBlock { Text = "Type to search (for example: phil)", FontSize = 16, Foreground = UiKit.B("Dim") });
        panel.Children.Add(filter);
        panel.Children.Add(results);
        panel.Children.Add(shown);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0), Children = { UiKit.ActionButton("Cancel", w.CloseDialog) } });
        Render();
        w.ShowDialog(panel, focus: filter);

        // Use the current country list from iptv-org when online; the built-in list is the fallback.
        _ = Task.Run(async () =>
        {
            try
            {
                var json = await Web.GetStringAsync(IptvOrg.CountriesApi);
                var live = IptvOrg.ParseCountries(json);
                if (live.Count > 20) { var s = IptvOrg.CountrySuggestions(live); await panel.Dispatcher.InvokeAsync(() => { countries = s; if (showCountries) Render(); }); }
            }
            catch { }
        });
    }

    /// <summary>Sleep, restart or shut down the whole PC (not only KwartoTV), or close the app.</summary>
    public static void PowerMenu(MainWindow w)
    {
        var panel = new StackPanel { Width = 620 };
        panel.Children.Add(new TextBlock { Text = "Power", FontSize = 32, FontWeight = FontWeights.Bold });
        panel.Children.Add(UiKit.Dimmed("These control the whole PC, not just KwartoTV.", 18));
        void Option(string label, string question, string yes, Action act, bool danger)
        {
            var b = UiKit.ActionButton(label, () => { w.CloseDialog(); Confirm(w, question, yes, act); }, danger);
            b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(b);
        }
        Option("🌙  Sleep the PC", "Put the PC to sleep? You will need to wake it with its power button.", "Sleep", PowerActions.Sleep, false);
        Option("↻  Restart the PC", "Restart the whole PC? KwartoTV will start again if it is set to start with Windows.", "Restart PC", PowerActions.Restart, true);
        Option("⏻  Shut down the PC", "Shut down the whole PC? You will need to switch it on by hand.", "Shut down PC", PowerActions.Shutdown, true);
        Option("✕  Exit KwartoTV only", "Close the KwartoTV app? The PC stays on.", "Exit app", () => Application.Current.Shutdown(), false);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0), Children = { UiKit.ActionButton("Cancel", w.CloseDialog) } });
        w.ShowDialog(panel);
    }

    /// <summary>Offers to install a newer KwartoTV. The installer closes the app and opens it again.</summary>
    public static void UpdateDialog(MainWindow w, UpdateInfo info, Updater updater)
    {
        var status = UiKit.Dimmed("", 18);
        var panel = new StackPanel { Width = 700 };
        panel.Children.Add(new TextBlock { Text = "Update available", FontSize = 32, FontWeight = FontWeights.Bold });
        panel.Children.Add(UiKit.Dimmed($"Version {info.Version} is ready (you have 1.0.{Updater.CurrentBuild}). KwartoTV will download it, close, install and open again. Windows will ask permission once.", 18));
        if (!string.IsNullOrWhiteSpace(info.Notes)) panel.Children.Add(UiKit.Dimmed(info.Notes!, 16));
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        Button? installBtn = null;
        installBtn = UiKit.ActionButton("Install now", async () =>
        {
            installBtn!.IsEnabled = false;
            status.Text = "Downloading…";
            var progress = new Progress<double>(p => status.Text = $"Downloading… {(int)(p * 100)}%");
            var err = await updater.InstallAsync(info, progress);
            if (err is null)
            {
                status.Text = "Installing. KwartoTV will close and open again.";
                await Task.Delay(2500);
                Application.Current.Shutdown();
            }
            else { status.Text = "⚠ " + err; status.Foreground = UiKit.B("Danger"); installBtn.IsEnabled = true; }
        });
        buttons.Children.Add(installBtn);
        buttons.Children.Add(UiKit.ActionButton("Later", w.CloseDialog));
        panel.Children.Add(buttons);
        w.ShowDialog(panel);
    }

    public static string? PickFolder()
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder" };
        return d.ShowDialog() == true ? d.FolderName : null;
    }

    public static string? PickPlaylistFile()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = "Choose a playlist", Filter = "Playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|All files|*.*" };
        return d.ShowDialog() == true ? d.FileName : null;
    }

    /// <summary>Re-applies the URL reservation and firewall rules (asks Windows for permission once).</summary>
    public static async void FixNetwork(MainWindow w, int port)
    {
        w.ShowToast("Windows will ask for permission…");
        var ok = await Task.Run(() => UrlAcl.RunElevated(port));
        w.ShowToast(ok ? "Network access fixed. Try again on the phone." : "Not changed: permission was declined.");
    }

    /// <summary>Shows a PIN and a QR code. Closes by itself when a phone completes pairing.</summary>
    public static void Pair(MainWindow w)
    {
        var tv = w.Tv;
        var addrs = TvServices.LanAddresses();
        var host = addrs.FirstOrDefault() ?? Environment.MachineName;
        int port = tv.ActivePort > 0 ? tv.ActivePort : tv.Settings.Port;
        int knownDevices = tv.Pairing.Devices.Count;

        var pinText = new TextBlock { FontSize = 76, FontWeight = FontWeights.Bold, Foreground = UiKit.B("Accent"), FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        var timeText = UiKit.Dimmed("", 20);
        var qr = new Image { Width = 300, Height = 300 };
        var qrBox = new Border { Background = System.Windows.Media.Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(10), Child = qr, Margin = new Thickness(0, 0, 40, 0), VerticalAlignment = VerticalAlignment.Top };
        DateTimeOffset expiry = default;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        void NewCode()
        {
            var pin = tv.Pairing.StartPairing(DateTimeOffset.UtcNow);
            expiry = DateTimeOffset.UtcNow + PairingService.PinLifetime;
            pinText.Text = pin.Insert(3, " ");
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(PairLink.Build(host, port, pin), QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(10);
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.StreamSource = new MemoryStream(png); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
            qr.Source = bmp;
        }
        void Tick()
        {
            if (!w.DialogOpen) { timer.Stop(); return; }
            var left = expiry - DateTimeOffset.UtcNow;
            timeText.Text = left > TimeSpan.Zero ? $"Code valid for {(int)left.TotalMinutes}:{left.Seconds:D2}" : "Code expired. Press New code.";
            if (tv.Pairing.Devices.Count > knownDevices)
            {
                timer.Stop(); w.CloseDialog();
                w.ShowToast($"{tv.Pairing.Devices.Last().Name} is now paired");
            }
        }

        var right = new StackPanel { Width = 480 };
        right.Children.Add(new TextBlock { Text = "Pair a phone", FontSize = 32, FontWeight = FontWeights.Bold });
        if (tv.Server is null)
            right.Children.Add(new TextBlock { Text = "⚠ " + tv.RemoteStatus + " Phones cannot connect until this is fixed.", FontSize = 18, Foreground = UiKit.B("Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) });
        right.Children.Add(UiKit.Dimmed("Open the KwartoTV Remote app and scan the QR code, or type this code:", 18));
        right.Children.Add(pinText);
        right.Children.Add(timeText);
        right.Children.Add(new TextBlock { Text = "Address", FontSize = 16, Foreground = UiKit.B("Dim"), Margin = new Thickness(0, 22, 0, 2) });
        right.Children.Add(new TextBlock { Text = $"{host}:{port}", FontSize = 26, FontFamily = new System.Windows.Media.FontFamily("Consolas") });
        if (addrs.Count > 1) right.Children.Add(UiKit.Dimmed("Other addresses of this PC: " + string.Join(", ", addrs.Skip(1)), 15));
        right.Children.Add(UiKit.Dimmed("Phone says it cannot reach the TV? Press Fix network access.", 15));
        right.Children.Add(UiKit.Dimmed($"No app? Open http://{host}:{port} in any phone or PC browser on this Wi-Fi.", 16));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(UiKit.ActionButton("New code", () => { NewCode(); Tick(); }));
        buttons.Children.Add(UiKit.ActionButton("Fix network access", () => FixNetwork(w, port)));
        buttons.Children.Add(UiKit.ActionButton("Close", () => { timer.Stop(); w.CloseDialog(); }));
        right.Children.Add(buttons);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(qrBox); row.Children.Add(right);
        NewCode(); Tick();
        timer.Tick += (_, _) => Tick();
        timer.Start();
        w.ShowDialog(row);
    }
}
