using KwartoTV.Core;

namespace KwartoTV.App.Ui;

/// <summary>
/// Connects the text box waiting on the TV (whatever on-screen keyboard is open: a KwartoTV box, the channel search, a
/// field on a web page such as a Netflix sign-in) with the phones, which can type the whole text at once.
/// UI thread only.
/// </summary>
public static class TextBridge
{
    private static ITextTarget? _target;
    private static string _kind = "text";
    private static string? _title;

    /// <summary>Raised with a TextRequest message when a box starts or stops waiting for text.</summary>
    public static event Action<string>? Changed;

    /// <summary>The message for the box waiting now, or null.</summary>
    public static string? Pending { get; private set; }

    public static void Begin(ITextTarget target, string? purpose, string? title)
    {
        _target = target;
        _kind = TextRequest.KindFor(target.Secret, purpose);
        _title = title;
        Pending = TextRequest.Message(true, _kind, _title, target.Text);
        Changed?.Invoke(Pending);
    }

    public static void End(ITextTarget target)
    {
        if (!ReferenceEquals(target, _target)) return;
        _target = null; Pending = null;
        Changed?.Invoke(TextRequest.Message(false));
    }

    /// <summary>Puts [text] in the waiting box (replacing what was there) and presses Go when [submit]. False when no box is waiting.</summary>
    public static bool Set(string text, bool submit)
    {
        var t = _target;
        if (t is null) return false;
        t.Clear();
        if (text.Length > 0) t.Insert(text);
        if (submit) t.Submit();
        return true;
    }
}
