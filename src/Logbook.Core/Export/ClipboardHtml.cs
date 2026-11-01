using System.Text;

namespace Logbook.Core.Export;

/// <summary>
/// Wraps an HTML fragment in the CF_HTML envelope Windows requires for the "HTML Format" clipboard format: a short
/// header of byte offsets, then a document with the fragment between StartFragment and EndFragment comments. The
/// clipboard carries it as UTF-8, so every offset counts UTF-8 bytes from the start of the header, not characters.
/// </summary>
public static class ClipboardHtml
{
    private const string Before = "<html><head><meta charset=\"utf-8\"></head><body>\r\n<!--StartFragment-->";
    private const string After = "<!--EndFragment-->\r\n</body></html>";

    public static string Wrap(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        int headerLength = Encoding.UTF8.GetByteCount(Header(0, 0, 0, 0));
        int startHtml = headerLength;
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(Before);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(After);
        return Header(startHtml, endHtml, startFragment, endFragment) + Before + fragment + After;
    }

    // Each offset is written as ten digits, so the header has the same length whatever the numbers are and can be
    // measured before they are known.
    private static string Header(int startHtml, int endHtml, int startFragment, int endFragment) => FormattableString.Invariant(
        $"Version:0.9\r\nStartHTML:{startHtml:D10}\r\nEndHTML:{endHtml:D10}\r\nStartFragment:{startFragment:D10}\r\nEndFragment:{endFragment:D10}\r\n");
}
