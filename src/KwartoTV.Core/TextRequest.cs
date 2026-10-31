using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>
/// A text box on the TV is waiting for text (a sign-in e-mail or password, a search...). Phones get a message, open a
/// text field of the right kind (so the phone's password manager can fill it) and send the whole text back with
/// "text.set" ("1:" + text also presses Go).
/// </summary>
public static class TextRequest
{
    /// <summary>Longest text a phone may send in one go.</summary>
    public const int MaxLength = 512;

    /// <summary>The kind of field, for the phone's keyboard and autofill: password, email, search, url or text.</summary>
    public static string KindFor(bool secret, string? purpose) => secret ? "password" : purpose switch
    {
        "web-email" => "email",
        "web-search" or "channel-search" or "live-channel-search" => "search",
        "link" => "url",
        _ => "text",
    };

    /// <summary>{"type":"text","active":true,"kind":"email","title":"E-mail or username","value":"..."}; value is never sent for a password.</summary>
    public static string Message(bool active, string kind = "text", string? title = null, string? value = null) =>
        JsonSerializer.Serialize(new
        {
            type = "text", active, kind, title,
            value = kind == "password" || value is null ? null : value.Length <= MaxLength ? value : value[..MaxLength],
        });
}
