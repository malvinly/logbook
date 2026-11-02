using Logbook.Core.Export;
using Xunit;

namespace Logbook.Tests.Export;

public sealed class PlaintextFolderTests
{
    private const string LogFolder = @"C:\Users\j-doe\Logbook";

    [Fact]
    public void TheFirstCandidateOutsideTheLogFolderWins()
    {
        Assert.Equal(@"C:\Users\j-doe\Exports", PlaintextFolder.Choose([@"C:\Users\j-doe\Exports", @"C:\Users\j-doe\Documents"], LogFolder));
    }

    [Fact]
    public void ACandidateEqualToTheLogFolderIsSkipped()
    {
        Assert.Equal(@"C:\Users\j-doe\Documents", PlaintextFolder.Choose([LogFolder, @"C:\Users\j-doe\Documents"], LogFolder));
    }

    [Fact]
    public void ASubfolderOfTheLogFolderIsSkipped()
    {
        Assert.Equal(@"C:\Users\j-doe\Documents", PlaintextFolder.Choose([@"C:\Users\j-doe\Logbook\exports", @"C:\Users\j-doe\Documents"], LogFolder));
    }

    [Fact]
    public void AllCandidatesInsideTheLogFolderIsNull()
    {
        Assert.Null(PlaintextFolder.Choose([LogFolder, @"C:\Users\j-doe\Logbook\exports"], LogFolder));
    }

    [Fact]
    public void NoCandidatesIsNull()
    {
        Assert.Null(PlaintextFolder.Choose([], LogFolder));
    }

    [Fact]
    public void ALogAtADriveRootKeepsTheWholeDriveOut()
    {
        Assert.Equal(@"C:\Users\j-doe\Documents", PlaintextFolder.Choose([@"D:\Exports", @"C:\Users\j-doe\Documents"], @"D:\"));
    }
}
