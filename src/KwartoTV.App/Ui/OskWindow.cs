using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace KwartoTV.App.Ui;

/// <summary>Types into the focused field of a web page (Netflix sign-in, search boxes, any website tile).</summary>
public sealed class WebTextTarget : ITextTarget
{
    private readonly WebView2 _web;
    private string _text = "";
    private int _caret;
    private bool _secret;

    /// <param name="kind">The field's kind as the page reported it ("password", "search", "email", ...).</param>
    public WebTextTarget(WebView2 web, string kind = "") { _web = web; _secret = kind == "password"; }

    public event Action? Changed;
    public string Text => _text;
    public int Caret => _caret;
    public bool Secret => _secret;
    public bool CanSubmit => true;

    private void Run(string js) { try { _ = _web.CoreWebView2?.ExecuteScriptAsync(js); } catch { } }

    public void Insert(string text) => Run("window.__kw&&window.__kw.insert(" + JsonSerializer.Serialize(text) + ")");
    public void Backspace() => Run("window.__kw&&window.__kw.backspace()");
    public void Clear() => Run("window.__kw&&window.__kw.clear()");
    public void Submit() => Run("window.__kw&&window.__kw.submit()");
    public void MoveCaret(int delta) => Run("window.__kw&&window.__kw.move(" + delta.ToString(CultureInfo.InvariantCulture) + ")");
    /// <summary>Asks the page to report the field's text again (it arrives through <see cref="Update"/>).</summary>
    public void Refresh() => Run("window.__kw&&window.__kw.report()");

    /// <summary>The page's report after each change: {"v": text, "c": caret, "p": password}. Passwords arrive as dots.</summary>
    public void Update(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            _text = r.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            _caret = r.TryGetProperty("c", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var ci) ? ci : _text.Length;
            _secret = r.TryGetProperty("p", out var p) && p.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return; }
        Changed?.Invoke();
    }

    /// <summary>
    /// Injected into every page: reports focus on text fields (with the field's kind), reports the field's text and cursor
    /// after each change, and edits the focused field like a keyboard would. Values go through the input's own setter
    /// so React-style pages notice the change.
    /// </summary>
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
  function plain(e) { var t = (e.tagName || '').toLowerCase(); return t === 'input' || t === 'textarea'; }
  function kind(e) {
    var t = (e.tagName || '').toLowerCase();
    if (t === 'textarea') return 'textarea';
    if (t !== 'input') return 'editable';
    var ty = (e.type || 'text').toLowerCase();
    if (ty === 'password') return 'password';
    var hint = [e.name, e.id, e.placeholder, e.getAttribute('aria-label'), e.getAttribute('autocomplete')].join(' ').toLowerCase();
    if (ty === 'search' || hint.indexOf('search') >= 0) return 'search';
    if (ty === 'email' || hint.indexOf('email') >= 0 || hint.indexOf('username') >= 0) return 'email';
    if (ty === 'url') return 'url';
    return ty === 'text' ? 'text' : ty;
  }
  function setValue(e, v) {
    try { var d = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(e), 'value'); if (d && d.set) d.set.call(e, v); else e.value = v; } catch (x) { e.value = v; }
  }
  // [start, end] of the selection; email and number fields have none, so the end of the text.
  function sel(e) { var a = null, b = null; try { a = e.selectionStart; b = e.selectionEnd; } catch (x) { } if (a == null) a = e.value.length; if (b == null) b = a; return [a, b]; }
  function caretOf(e, v) {
    if (plain(e)) return sel(e)[1];
    try {
      var s = window.getSelection(); if (!s.rangeCount) return v.length;
      var r = s.getRangeAt(0), pre = r.cloneRange(); pre.selectNodeContents(e); pre.setEnd(r.endContainer, r.endOffset); return pre.toString().length;
    } catch (x) { return v.length; }
  }
  function report() {
    var e = field(); if (!e) return;
    var v = plain(e) ? (e.value || '') : (e.innerText || ''), c = caretOf(e, v), p = kind(e) === 'password';
    if (v.length > 4000) { var st = Math.max(0, Math.min(c - 2000, v.length - 4000)); v = v.slice(st, st + 4000); c -= st; }
    if (p) v = v.replace(/./g, '●');
    window.chrome.webview.postMessage('textstate:' + JSON.stringify({ v: v, c: c, p: p }));
  }
  function fire(e) { e.dispatchEvent(new Event('input', { bubbles: true })); e.dispatchEvent(new Event('change', { bubbles: true })); }
  window.__kw = {
    insert: function (s) { var e = field(); if (!e) return; if (!plain(e)) { document.execCommand('insertText', false, s); return; }
      var r = sel(e);
      try { e.setRangeText(s, r[0], r[1], 'end'); } catch (x) { setValue(e, e.value + s); } fire(e); },
    backspace: function () { var e = field(); if (!e) return; if (!plain(e)) { document.execCommand('delete'); return; }
      var r = sel(e), a = r[0], b = r[1];
      if (a === b && a > 0) { try { e.setRangeText('', a - 1, a, 'end'); } catch (x) { setValue(e, e.value.slice(0, -1)); } }
      else if (a !== b) { try { e.setRangeText('', a, b, 'end'); } catch (x) { setValue(e, ''); } }
      fire(e); },
    clear: function () { var e = field(); if (!e) return; if (!plain(e)) { e.textContent = ''; } else { setValue(e, ''); } fire(e); },
    move: function (d) { var e = field(); if (!e) return;
      if (plain(e)) { try { var r = sel(e), to = r[0] !== r[1] ? (d < 0 ? r[0] : r[1]) : r[0] + d; to = Math.max(0, Math.min(e.value.length, to)); e.setSelectionRange(to, to); } catch (x) { } }
      else { try { window.getSelection().modify('move', d < 0 ? 'backward' : 'forward', 'character'); } catch (x) { } }
      report(); },
    // Enter: the page's own key handlers first (search boxes), then the form, as a real Enter would.
    submit: function () { var e = field(); if (!e) return;
      var o = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true }, go = true;
      try { go = e.dispatchEvent(new KeyboardEvent('keydown', o)); e.dispatchEvent(new KeyboardEvent('keypress', o)); e.dispatchEvent(new KeyboardEvent('keyup', o)); } catch (x) { }
      if (go && e.form && (e.tagName || '').toLowerCase() === 'input') { try { if (e.form.requestSubmit) e.form.requestSubmit(); else e.form.submit(); } catch (x) { } } },
    report: report
  };
  document.addEventListener('focusin', function () { var e = field(); if (e) { window.chrome.webview.postMessage('textfocus:' + kind(e)); report(); } }, true);
  document.addEventListener('input', function () { if (field()) report(); }, true);
})();";
}

/// <summary>The on-screen keyboard as its own window along the bottom of the screen, above a web page.</summary>
public sealed class OskWindow : Window
{
    private readonly OnScreenKeyboard _osk;

    public event Action? Closing_;

    /// <param name="purpose">The kind of field, for its recent entries (null for passwords).</param>
    /// <param name="title">A short name for the field, shown above the preview.</param>
    public OskWindow(ITextTarget target, string? purpose = null, string? title = null)
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        Background = Brushes.Transparent; SizeToContent = SizeToContent.Height;
        Width = SystemParameters.PrimaryScreenWidth; Left = 0;
        _osk = new OnScreenKeyboard(target, Finish, Finish, purpose, title);
        Content = _osk;
        Loaded += (_, _) =>
        {
            Top = SystemParameters.PrimaryScreenHeight - ActualHeight;
            Activate();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, _osk.FocusFirstKey);
        };
        // The recent-entries row can appear once the page reports the field: stay on the bottom edge.
        SizeChanged += (_, _) => Top = SystemParameters.PrimaryScreenHeight - ActualHeight;
    }

    private void Finish() { Closing_?.Invoke(); Close(); }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // Arrows, OK, Escape/Back and Backspace: the keyboard's own handling (wrapping, hold to repeat).
        if (_osk.HandleKey(e)) e.Handled = true;
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        // A physical keyboard or the phone's keyboard tab types through to the page, even when no key has the focus.
        if (_osk.TypeText(e.Text)) e.Handled = true;
    }
}
