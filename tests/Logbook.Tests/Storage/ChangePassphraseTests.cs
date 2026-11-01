using System.Security.Cryptography;
using System.Text;
using Logbook.Core;
using Logbook.Core.Crypto;
using Logbook.Core.Storage;
using Xunit;

namespace Logbook.Tests.Storage;

/// <summary>Changing the passphrase rewrites only line 1, in place; records are untouched (DESIGN.md section 8).</summary>
public sealed class ChangePassphraseTests : IDisposable
{
    private const string OldPassphrase = "correct horse";
    private const string NewPassphrase = "battery staple";
    private const string EscapedPlus = "\\" + "u002B"; // the six characters as they appear in the JSON line
    private static readonly IReadOnlyList<TimeSpan> NoDelays = [];
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));

    private readonly TempFile _file = new();

    public void Dispose() => _file.Dispose();

    private static Entry NewEntry(string summary) => new(
        Ulid.Generate(), 1, Now, Now, summary, null, ["asked"], ["j-doe"], [], [], null, false);

    private LogFile CreateWithEntries()
    {
        LogFile log = LogFile.Create(_file.Path, OldPassphrase, Now, NoDelays);
        Entry first = NewEntry("first");
        log.Append(first);
        log.Append(NewEntry("second"));
        log.Append(first with { Rev = 2, Summary = "first, edited" });
        return log;
    }

    private LogFile Open(string passphrase) => LogFile.Open(_file.Path, passphrase, NoDelays);

    private static int HeaderEnd(byte[] bytes) => Array.IndexOf(bytes, (byte)'\n');

    [Fact]
    public void OldPassphraseNoLongerOpensTheFile()
    {
        CreateWithEntries().ChangePassphrase(OldPassphrase, NewPassphrase);
        Assert.ThrowsAny<CryptographicException>(() => Open(OldPassphrase));
    }

    [Fact]
    public void NewPassphraseOpensTheFileWithEveryRecord()
    {
        LogFile log = CreateWithEntries();
        log.ChangePassphrase(OldPassphrase, NewPassphrase);

        LogFile reopened = Open(NewPassphrase);
        Assert.Equal(log.Revisions.Select(LogbookJson.Serialize), reopened.Revisions.Select(LogbookJson.Serialize));
        Assert.Equal(["first, edited", "second"], reopened.Live.Select(e => e.Summary).Order());
        Assert.Equal(0, reopened.UnreadableLineCount);
        Assert.False(reopened.TrailingLineTruncated);
    }

    [Fact]
    public void RecordLinesAreByteIdenticalAndOnlyLineOneChanges()
    {
        LogFile log = CreateWithEntries();
        byte[] before = File.ReadAllBytes(_file.Path);

        log.ChangePassphrase(OldPassphrase, NewPassphrase);
        byte[] after = File.ReadAllBytes(_file.Path);

        Assert.Equal(before.Length, after.Length);
        int headerEnd = HeaderEnd(before);
        Assert.Equal(headerEnd, HeaderEnd(after));
        Assert.Equal(before[headerEnd..], after[headerEnd..]); // the newline after line 1 and every record line
        Assert.NotEqual(before[..headerEnd], after[..headerEnd]);
    }

    [Fact]
    public void HeaderKeepsItsIdentityAndParametersButGetsANewSaltAndWrappedKey()
    {
        LogFile log = CreateWithEntries();
        FileHeader before = LogFile.ReadHeader(_file.Path);

        log.ChangePassphrase(OldPassphrase, NewPassphrase);
        FileHeader after = LogFile.ReadHeader(_file.Path);

        Assert.Equal(before.Format, after.Format);
        Assert.Equal(before.FileId, after.FileId);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal(before.CreatedAt.Offset, after.CreatedAt.Offset);
        Assert.Equal(before.Kdf.Alg, after.Kdf.Alg);
        Assert.Equal(before.Kdf.Iterations, after.Kdf.Iterations);
        Assert.NotEqual(before.Kdf.Salt, after.Kdf.Salt);
        Assert.Equal(16, Convert.FromBase64String(after.Kdf.Salt).Length);
        Assert.NotEqual(before.WrappedKey, after.WrappedKey);
        Assert.Equal(after, log.Header);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void HeaderWithAnyNumberOfEscapedPlusSignsIsReplacedAtTheSameLength(int plusSigns)
    {
        // System.Text.Json writes '+' in the base64 values as \u002B, so the old header's length depends on its random
        // values. The new one must come out exactly as long whatever the old one held.
        LogFile.Create(_file.Path, OldPassphrase, Now, NoDelays).Append(NewEntry("first"));
        RewriteHeader(OldPassphrase, KeyDerivation.DefaultIterations, line => CountOf(line, EscapedPlus) == plusSigns);
        byte[] before = File.ReadAllBytes(_file.Path);
        LogFile log = Open(OldPassphrase);

        log.ChangePassphrase(OldPassphrase, NewPassphrase);

        byte[] after = File.ReadAllBytes(_file.Path);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before[HeaderEnd(before)..], after[HeaderEnd(after)..]);
        Assert.Equal(plusSigns, CountOf(Encoding.UTF8.GetString(after, 0, HeaderEnd(after)), EscapedPlus));
        Assert.Equal("first", Assert.Single(Open(NewPassphrase).Live).Summary);
    }

    [Fact]
    public void KeepsTheIterationCountTheFileAlreadyHas()
    {
        // A file written with a different count (an older default, say) keeps it: the change touches only salt and wrap.
        LogFile.Create(_file.Path, OldPassphrase, Now, NoDelays).Append(NewEntry("first"));
        RewriteHeader(OldPassphrase, 600_001, _ => true);
        LogFile log = Open(OldPassphrase);

        log.ChangePassphrase(OldPassphrase, NewPassphrase);

        Assert.Equal(600_001, LogFile.ReadHeader(_file.Path).Kdf.Iterations);
        Assert.Equal("first", Assert.Single(Open(NewPassphrase).Live).Summary);
    }

    [Fact]
    public void OpenLogKeepsAppendingWithTheSameDataKeyAfterTheChange()
    {
        LogFile log = CreateWithEntries();
        log.ChangePassphrase(OldPassphrase, NewPassphrase);

        Assert.True(log.Append(NewEntry("after the change")));

        LogFile reopened = Open(NewPassphrase);
        Assert.Equal(4, reopened.Revisions.Count);
        Assert.Equal("after the change", reopened.Revisions[^1].Summary);
        Assert.Equal(0, reopened.UnreadableLineCount);
    }

    [Fact]
    public void CanChangeAgainFromTheNewPassphrase()
    {
        LogFile log = CreateWithEntries();
        log.ChangePassphrase(OldPassphrase, NewPassphrase);
        log.ChangePassphrase(NewPassphrase, "third one");

        Assert.ThrowsAny<CryptographicException>(() => Open(NewPassphrase));
        Assert.Equal(3, Open("third one").Revisions.Count);
    }

    [Fact]
    public void EmptyNewPassphraseIsRefusedAndTheFileIsUntouched()
    {
        LogFile log = CreateWithEntries();
        byte[] before = File.ReadAllBytes(_file.Path);

        Assert.Throws<ArgumentException>(() => log.ChangePassphrase(OldPassphrase, ""));

        Assert.Equal(before, File.ReadAllBytes(_file.Path));
    }

    [Fact]
    public void WrongCurrentPassphraseIsRefusedAndTheFileIsUntouched()
    {
        LogFile log = CreateWithEntries();
        byte[] before = File.ReadAllBytes(_file.Path);

        Assert.ThrowsAny<CryptographicException>(() => log.ChangePassphrase("correct horsf", NewPassphrase));

        Assert.Equal(before, File.ReadAllBytes(_file.Path));
        Assert.Equal(3, Open(OldPassphrase).Revisions.Count);
    }

    [Fact]
    public void HeaderThisAppDidNotWriteIsRefusedRatherThanRewrittenWhole()
    {
        // Same fields, one space more than this app writes: it opens, but no header this app writes can be one byte
        // longer (an escaped '+' adds five), so it cannot be replaced in place. The change is refused, nothing is
        // written, and there is no fallback to rewriting the whole file.
        LogFile.Create(_file.Path, OldPassphrase, Now, NoDelays).Append(NewEntry("first"));
        byte[] original = File.ReadAllBytes(_file.Path);
        File.WriteAllBytes(_file.Path, [.. "{ "u8.ToArray(), .. original[1..]]);
        byte[] before = File.ReadAllBytes(_file.Path);
        LogFile log = Open(OldPassphrase);

        Assert.Throws<InvalidDataException>(() => log.ChangePassphrase(OldPassphrase, NewPassphrase));

        Assert.Equal(before, File.ReadAllBytes(_file.Path));
        Assert.Equal("first", Assert.Single(Open(OldPassphrase).Live).Summary);
    }

    [Fact]
    public void HeaderWithAnUnknownPropertyIsRefusedRatherThanRewrittenWithoutIt()
    {
        // The extra property is ten bytes, a multiple of the five an escaped '+' adds, so the length arithmetic alone
        // would accept it and the rebuilt header would silently drop the property.
        LogFile.Create(_file.Path, OldPassphrase, Now, NoDelays).Append(NewEntry("first"));
        byte[] original = File.ReadAllBytes(_file.Path);
        int headerEnd = HeaderEnd(original);
        byte[] extra = ",\"n\":\"xxx\""u8.ToArray();
        Assert.Equal(10, extra.Length);
        File.WriteAllBytes(_file.Path, [.. original[..(headerEnd - 1)], .. extra, .. original[(headerEnd - 1)..]]);
        byte[] before = File.ReadAllBytes(_file.Path);
        LogFile log = Open(OldPassphrase);

        Assert.Throws<InvalidDataException>(() => log.ChangePassphrase(OldPassphrase, NewPassphrase));

        Assert.Equal(before, File.ReadAllBytes(_file.Path));
        Assert.Equal("first", Assert.Single(Open(OldPassphrase).Live).Summary);
    }

    [Fact]
    public void FileReplacedByOneWithAnotherDataKeyIsRefused()
    {
        // A sync client could swap in a different file at the same path; its header must not be rewritten for our key.
        LogFile log = CreateWithEntries();
        File.Delete(_file.Path);
        LogFile.Create(_file.Path, OldPassphrase, Now, NoDelays);
        byte[] before = File.ReadAllBytes(_file.Path);

        Assert.Throws<InvalidDataException>(() => log.ChangePassphrase(OldPassphrase, NewPassphrase));

        Assert.Equal(before, File.ReadAllBytes(_file.Path));
    }

    [Fact]
    public void RefusedWhileACaptureIsPending()
    {
        LogFile log = CreateWithEntries();
        using (new FileStream(_file.Path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(log.Append(NewEntry("pending")));
        }

        byte[] before = File.ReadAllBytes(_file.Path);

        Assert.Throws<InvalidOperationException>(() => log.ChangePassphrase(OldPassphrase, NewPassphrase));

        Assert.Equal(before, File.ReadAllBytes(_file.Path));
        Assert.Equal(1, log.PendingCount);
    }

    [Fact]
    public void FileHeldByAnotherProcessFailsWithIOExceptionAndChangesNothing()
    {
        LogFile log = CreateWithEntries();
        byte[] before = File.ReadAllBytes(_file.Path);
        FileHeader header = log.Header;

        using (new FileStream(_file.Path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => log.ChangePassphrase(OldPassphrase, NewPassphrase));
        }

        Assert.Equal(before, File.ReadAllBytes(_file.Path));
        Assert.Equal(header, log.Header);
        Assert.Equal(3, Open(OldPassphrase).Revisions.Count);
    }

    private static int CountOf(string text, string part) => (text.Length - text.Replace(part, "", StringComparison.Ordinal).Length) / part.Length;

    // Writes a fresh header with the given iteration count over line 1, keeping the data key and the records. The key is
    // wrapped again until the serialized line satisfies the condition.
    private void RewriteHeader(string passphrase, int iterations, Func<string, bool> accept)
    {
        byte[] original = File.ReadAllBytes(_file.Path);
        int headerEnd = HeaderEnd(original);
        FileHeader header = LogFile.ReadHeader(_file.Path);
        byte[] kek = KeyDerivation.Derive(passphrase, Convert.FromBase64String(header.Kdf.Salt), header.Kdf.Iterations);
        byte[] dataKey = DataKey.Unwrap(header.WrappedKey, kek);
        while (true)
        {
            byte[] salt = KeyDerivation.NewSalt();
            if (Convert.ToBase64String(salt).Contains('+', StringComparison.Ordinal))
            {
                continue; // keep the count of '+' to the wrapped key, which is cheap to draw again
            }

            byte[] newKek = KeyDerivation.Derive(passphrase, salt, iterations);
            for (int attempt = 0; attempt < 100_000; attempt++)
            {
                FileHeader changed = header with
                {
                    Kdf = header.Kdf with { Salt = Convert.ToBase64String(salt), Iterations = iterations },
                    WrappedKey = DataKey.Wrap(dataKey, newKek),
                };
                string line = LogbookJson.Serialize(changed);
                if (accept(line))
                {
                    File.WriteAllBytes(_file.Path, [.. Encoding.UTF8.GetBytes(line), .. original[headerEnd..]]);
                    return;
                }
            }
        }
    }
}
