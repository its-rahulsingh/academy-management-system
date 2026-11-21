using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Academy.Api.Infrastructure.Email;

/// <summary>
/// The formatted (HTML) version of emails. Messages keep their plain-text body too, for mail apps that prefer it.
/// The stored HTML is only the message's own content; the page around it (with the sender's name at the top) is added
/// when the email is sent, because only then is the academy's sender name known.
/// Kept to simple tables and inline styles, which is what Gmail, Outlook and phone mail apps reliably show.
/// </summary>
public static partial class EmailHtml
{
    public static string Encode(string text) => WebUtility.HtmlEncode(text);

    /// <summary>Turns a plain-text email into simple HTML: paragraphs, line breaks, and clickable links.</summary>
    public static string FromText(string text)
    {
        var html = new StringBuilder();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = paragraph.Trim('\n').Split('\n').Select(line => Link().Replace(Encode(line), m => $"""<a href="{m.Value}" style="color:#5147df">{m.Value}</a>"""));
            html.Append("""<p style="margin:0 0 14px;line-height:1.55">""").Append(string.Join("<br>", lines)).Append("</p>");
        }
        return html.ToString();
    }

    [GeneratedRegex(@"https?://[^\s<]+")]
    private static partial Regex Link();

    /// <summary>Rows of label and value, e.g. for a receipt.</summary>
    public static string Table(IEnumerable<(string Label, string Value, bool Strong)> rows)
    {
        var html = new StringBuilder("""<table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse:collapse;margin:6px 0 18px">""");
        foreach (var (label, value, strong) in rows)
        {
            var weight = strong ? "font-weight:700;font-size:16px;color:#1f2238" : "color:#2b2f45";
            html.Append($"""<tr><td style="padding:9px 0;border-bottom:1px solid #eceef5;color:#6d7186;font-size:13px">{Encode(label)}</td>""")
                .Append($"""<td align="right" style="padding:9px 0;border-bottom:1px solid #eceef5;{weight}">{Encode(value)}</td></tr>""");
        }
        return html.Append("</table>").ToString();
    }

    /// <summary>The whole email page: sender's name as the heading, the message, and a quiet footer.</summary>
    public static string Page(string brand, string subject, string content) => $$"""
        <!doctype html>
        <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{{Encode(subject)}}</title></head>
        <body style="margin:0;padding:0;background:#f4f5fa;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#2b2f45;font-size:14px">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f5fa;padding:24px 12px">
            <tr><td align="center">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border-radius:14px;border:1px solid #e6e8f0">
                <tr><td style="padding:22px 28px;border-bottom:1px solid #eceef5">
                  <div style="font-size:18px;font-weight:800;color:#1f2238">{{Encode(brand)}}</div>
                </td></tr>
                <tr><td style="padding:24px 28px 10px">{{content}}</td></tr>
              </table>
              <div style="max-width:560px;color:#9195a7;font-size:11px;padding:14px 8px">Sent by {{Encode(brand)}}. Reply to this email to contact them.</div>
            </td></tr>
          </table>
        </body></html>
        """;
}
