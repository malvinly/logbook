using Logbook.Core.Export;
using Xunit;

namespace Logbook.Tests.Export;

/// <summary>The timeline header takes a saved-query button's name only while its filter is unedited.</summary>
public sealed class ExportTitleTests
{
    [Fact]
    public void NameStandsInWhileTheFilterIsUnedited()
    {
        Assert.Equal("This week", ExportTitle.For("last:7d", "This week", "last:7d"));
    }

    [Theory]
    [InlineData("last:7d ")]
    [InlineData("LAST:7d")]
    [InlineData("last:7d #asked")]
    [InlineData("last:7")]
    [InlineData("")]
    public void AnyEditGivesTheTypedFilterBack(string edited)
    {
        Assert.Equal(edited, ExportTitle.For(edited, "This week", "last:7d"));
    }

    [Fact]
    public void NoSavedQueryGivesTheFilterText()
    {
        Assert.Equal("last:7d", ExportTitle.For("last:7d", null, null));
    }

    [Fact]
    public void BlankFilterWithNoSavedQueryStaysBlank()
    {
        Assert.Equal("", ExportTitle.For("", null, null));
    }

    [Fact]
    public void HeaderReadsTheNameWhenGivenIt()
    {
        string title = ExportTitle.For("#staging-access", "Staging asks", "#staging-access");
        Assert.StartsWith("Staging asks — 3 entries, Oct 6 – Oct 20, 2026\n", TimelineExport.Render(title, SampleEntries.Sample, ExportOptions.None), StringComparison.Ordinal);
    }
}
