namespace Logbook.Tests.Storage;

/// <summary>A path in the temp folder that is deleted when the test finishes. The file itself is not created.</summary>
internal sealed class TempFile : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "logbook-tests", $"{Guid.NewGuid():N}.jsonl");

    public TempFile()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
    }

    public void Dispose()
    {
        File.Delete(Path);
    }
}
