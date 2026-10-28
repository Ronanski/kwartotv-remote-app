using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KwartoTV.App.Services;
using Microsoft.Web.WebView2.Wpf;

namespace KwartoTV.App.Ui;

/// <summary>Types into the focused field of a web page (Netflix sign-in, search boxes, any website tile).</summary>
public sealed class WebTextTarget : ITextTarget
{
    private readonly WebView2 _web;
    public WebTextTarget(WebView2 web) => _web = web;

    private void Run(string js) { try { _ = _web.CoreWebView2?.ExecuteScriptAsync(js); } catch { } }

    public void Insert(string text) => Run("window.__kw&&window.__kw.insert(" + System.Text.Json.JsonSerializer.Serialize(text) + ")");
    public void Backspace() => Run("window.__kw&&window.__kw.backspace()");
    public void Clear() => Run("window.__kw&&window.__kw.clear()");
    public void Submit() => Run("window.__kw&&window.__kw.submit()");

    /// <summary>Injected into every page: reports focus on text fields and edits the focused field like a keyboard would.</summary>
    public const string PageScript = @"
(function () {
  if (window.__kw) return;
  function field() {
    var e = document.activeElement;
    if (!e) return null;
    var t = (e.tagName || '').toLowerCase();
    if (t === 'textarea') return e;
    if (t === 'input') { var ty = (e.type || 'text').toLowerCase(); if (['text','email','password','search','url','tel','number'].indexOf(ty) >= 0) return e; }
    if (e.isContentEditable) return e;
    return null;
  }
  function fire(e) { e.dispatchEvent(new Event('input', { bubbles: true })); e.dispatchEvent(new Event('change', { bubbles: true })); }
  window.__kw = {
    insert: function (s) { var e = field(); if (!e) return; if (e.isContentEditable) { document.execCommand('insertText', false, s); return; }
      var a = e.selectionStart == null ? e.value.length : e.selectionStart, b = e.selectionEnd == null ? a : e.selectionEnd;
      try { e.setRangeText(s, a, b, 'end'); } catch (x) { e.value = e.value + s; } fire(e); },
    backspace: function () { var e = field(); if (!e) return; if (e.isContentEditable) { document.execCommand('delete'); return; }
      var a = e.selectionStart == null ? e.value.length : e.selectionStart, b = e.selectionEnd == null ? a : e.selectionEnd;
      if (a === b && a > 0) { try { e.setRangeText('', a - 1, a, 'end'); } catch (x) { e.value = e.value.slice(0, -1); } } else if (a !== b) { e.setRangeText('', a, b, 'end'); }
      fire(e); },
    clear: function () { var e = field(); if (!e) return; if (e.isContentEditable) { e.textContent = ''; } else { e.value = ''; } fire(e); },
    submit: function () { var e = field(); if (!e) return; if (e.form) { if (e.form.requestSubmit) e.form.requestSubmit(); else e.form.submit(); } }
  };
  document.addEventListener('focusin', function () { if (field()) window.chrome.webview.postMessage('textfocus'); }, true);
})();";
}

/// <summary>The on-screen keyboard as its own window along the bottom of the screen, above a web page.</summary>
public sealed class OskWindow : Window
{
    private readonly ITextTarget _target;
    private readonly OnScreenKeyboard _osk;

    public event Action? Closing_;

    public OskWindow(ITextTarget target)
    {
        _target = target;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        Background = Brushes.Transparent; SizeToContent = SizeToContent.Height;
        Width = SystemParameters.PrimaryScreenWidth; Left = 0;
        _osk = new OnScreenKeyboard(target, Finish);
        Content = _osk;
        Loaded += (_, _) =>
        {
            Top = SystemParameters.PrimaryScreenHeight - ActualHeight;
            Activate();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, _osk.FocusFirstKey);
        };
    }

    private void Finish() { Closing_?.Invoke(); Close(); }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // A physical keyboard or the phone's keyboard tab types through to the page.
        if (e.Key is Key.Escape or Key.BrowserBack) { Finish(); e.Handled = true; }
        else if (e.Key == Key.Back) { _target.Backspace(); e.Handled = true; }
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (!string.IsNullOrEmpty(e.Text) && !e.Text.Any(char.IsControl)) { _target.Insert(e.Text); e.Handled = true; }
    }
}
