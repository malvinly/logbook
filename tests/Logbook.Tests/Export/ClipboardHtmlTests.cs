using System.Globalization;
using System.Text;
using Logbook.Core.Export;
using Xunit;

namespace Logbook.Tests.Export;

/// <summary>The CF_HTML wrapping Windows requires for HTML on the clipboard. Its offsets count UTF-8 bytes, not characters.</summary>
public sealed class ClipboardHtmlTests
{
    // Every multi-byte width: two (é, ï), three (—, ✓, 日本) and four (😀) bytes per character in UTF-8.
    private const string NonAsciiFragment = "<ul>\n<li><b>Tue Oct 6, 2026 10:14 AM</b> — Café naïve ✓ 日本 😀</li>\n</ul>";

    private static int Offset(string wrapped, string name)
    {
        string line = wrapped.Split("\r\n").Single(l => l.StartsWith(name + ":", StringComparison.Ordinal));
        return int.Parse(line[(name.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void StartsWithTheVersionAndTheFourOffsets()
    {
        string[] lines = ClipboardHtml.Wrap("<p>x</p>").Split("\r\n");
        Assert.Equal("Version:0.9", lines[0]);
        Assert.Matches("^StartHTML:[0-9]{10}$", lines[1]);
        Assert.Matches("^EndHTML:[0-9]{10}$", lines[2]);
        Assert.Matches("^StartFragment:[0-9]{10}$", lines[3]);
        Assert.Matches("^EndFragment:[0-9]{10}$", lines[4]);
    }

    [Fact]
    public void FragmentOffsetsAreUtf8ByteOffsetsOfTheFragment()
    {
        string wrapped = ClipboardHtml.Wrap(NonAsciiFragment);
        byte[] bytes = Encoding.UTF8.GetBytes(wrapped);
        int start = Offset(wrapped, "StartFragment");
        int end = Offset(wrapped, "EndFragment");
        Assert.Equal(NonAsciiFragment, Encoding.UTF8.GetString(bytes, start, end - start));

        // Counting characters instead of bytes would land elsewhere for this fragment; make sure the test can tell.
        Assert.NotEqual(Encoding.UTF8.GetByteCount(NonAsciiFragment), NonAsciiFragment.Length);
    }

    [Fact]
    public void MarkersSitRightAroundTheFragment()
    {
        string wrapped = ClipboardHtml.Wrap(NonAsciiFragment);
        byte[] bytes = Encoding.UTF8.GetBytes(wrapped);
        byte[] startMarker = Encoding.ASCII.GetBytes("<!--StartFragment-->");
        byte[] endMarker = Encoding.ASCII.GetBytes("<!--EndFragment-->");
        int start = Offset(wrapped, "StartFragment");
        int end = Offset(wrapped, "EndFragment");
        Assert.Equal(startMarker, bytes[(start - startMarker.Length)..start]);
        Assert.Equal(endMarker, bytes[end..(end + endMarker.Length)]);
    }

    [Fact]
    public void HtmlOffsetsSpanTheDocumentToTheLastByte()
    {
        string wrapped = ClipboardHtml.Wrap(NonAsciiFragment);
        byte[] bytes = Encoding.UTF8.GetBytes(wrapped);
        int startHtml = Offset(wrapped, "StartHTML");
        int endHtml = Offset(wrapped, "EndHTML");
        Assert.Equal("<html>", Encoding.UTF8.GetString(bytes, startHtml, 6));
        Assert.Equal(bytes.Length, endHtml);
        Assert.EndsWith("</html>", wrapped, StringComparison.Ordinal);
        Assert.True(startHtml < Offset(wrapped, "StartFragment"));
    }

    [Fact]
    public void DeclaresUtf8AndPassesTheFragmentThroughVerbatim()
    {
        string wrapped = ClipboardHtml.Wrap("<p>a &amp; b</p>");
        Assert.Contains("<meta charset=\"utf-8\">", wrapped, StringComparison.Ordinal);
        Assert.Contains("<!--StartFragment--><p>a &amp; b</p><!--EndFragment-->", wrapped, StringComparison.Ordinal);
    }

    [Fact]
    public void WrapsARenderedTimeline()
    {
        string fragment = TimelineHtml.Render("#staging-access", SampleEntries.Sample, ExportOptions.None);
        string wrapped = ClipboardHtml.Wrap(fragment);
        byte[] bytes = Encoding.UTF8.GetBytes(wrapped);
        int start = Offset(wrapped, "StartFragment");
        int end = Offset(wrapped, "EndFragment");
        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes, start, end - start));
    }

    [Fact]
    public void EmptyFragmentHasEqualOffsets()
    {
        string wrapped = ClipboardHtml.Wrap("");
        Assert.Equal(Offset(wrapped, "StartFragment"), Offset(wrapped, "EndFragment"));
    }
}
