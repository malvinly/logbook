using System.Security.Cryptography;
using System.Text;
using Logbook.Core;
using Logbook.Core.Crypto;
using Logbook.Core.Storage;
using Xunit;

namespace Logbook.Tests.Storage;

public sealed class LogFileTests : IDisposable
{
    private const string Passphrase = "correct horse";
    private static readonly IReadOnlyList<TimeSpan> NoDelays = [];
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));

    private readonly TempFile _file = new();

    public void Dispose() => _file.Dispose();

    private static Entry NewEntry(string summary, int rev = 1, string? id = null, bool deleted = false) => new(
        id ?? Ulid.Generate(),
        rev,
        new DateTimeOffset(2026, 10, 6, 10, 14, 3, TimeSpan.FromHours(-5)),
        new DateTimeOffset(2026, 10, 6, 10, 14, 9, TimeSpan.FromHours(-5)),
        summary,
        null,
        ["asked"],
        ["j-doe"],
        [],
        deleted);

    private LogFile Create() => LogFile.Create(_file.Path, Passphrase, Now, NoDelays);

    private LogFile Open(string passphrase = Passphrase) => LogFile.Open(_file.Path, passphrase, NoDelays);

    private string[] FileLines() => File.ReadAllText(_file.Path).Split('\n');

    [Fact]
    public void CreateWritesOnlyAPlaintextHeaderLine()
    {
        LogFile log = Create();
        string[] lines = FileLines();
        Assert.Equal(2, lines.Length); // header, then the empty segment after its newline
        Assert.Equal("", lines[1]);
        Assert.Equal(LogbookJson.Serialize(log.Header), lines[0]);
        Assert.Equal("logbook/1", log.Header.Format);
        Assert.Equal(26, log.Header.FileId.Length);
        Assert.Equal(Now, log.Header.CreatedAt);
        Assert.Equal("pbkdf2-hmac-sha256", log.Header.Kdf.Alg);
        Assert.Equal(600_000, log.Header.Kdf.Iterations);
        Assert.Equal(16, Convert.FromBase64String(log.Header.Kdf.Salt).Length);
        Assert.Empty(log.Live);
    }

    [Fact]
    public void CreateRefusesToOverwriteAnExistingFile()
    {
        File.WriteAllText(_file.Path, "precious\n");
        Assert.Throws<IOException>(Create);
        Assert.Equal("precious\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void OpenWithRightPassphraseLoadsAnEmptyLog()
    {
        FileHeader created = Create().Header;
        LogFile log = Open();
        Assert.Equal(created, log.Header);
        Assert.Empty(log.Live);
        Assert.Equal(0, log.UnreadableLineCount);
        Assert.False(log.TrailingLineTruncated);
    }

    [Fact]
    public void OpenWithWrongPassphraseFails()
    {
        Create();
        Assert.ThrowsAny<CryptographicException>(() => Open("correct horsf"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"format\":\"logbook/1\"")] // header never terminated
    [InlineData("not json\n")]
    [InlineData("{\"format\":\"logbook/2\",\"fileId\":\"x\",\"createdAt\":\"2026-10-06T09:00:00-05:00\",\"kdf\":{\"alg\":\"pbkdf2-hmac-sha256\",\"salt\":\"AA==\",\"iterations\":1},\"wrappedKey\":\"AA==\"}\n")]
    [InlineData("{\"format\":\"logbook/1\",\"fileId\":\"x\",\"createdAt\":\"2026-10-06T09:00:00-05:00\",\"kdf\":{\"alg\":\"argon2id\",\"salt\":\"AA==\",\"iterations\":1},\"wrappedKey\":\"AA==\"}\n")]
    [InlineData("{\"format\":\"logbook/1\",\"fileId\":\"x\",\"createdAt\":\"2026-10-06T09:00:00-05:00\"}\n")]
    public void OpenRefusesABadOrUnknownHeader(string content)
    {
        File.WriteAllText(_file.Path, content);
        Assert.Throws<InvalidDataException>(() => Open());
    }

    [Fact]
    public void AppendedEntryRoundTripsThroughReopenWithOffsetIntact()
    {
        Entry entry = NewEntry("Asked for access") with { OccurredAt = new DateTimeOffset(2026, 10, 6, 10, 14, 3, TimeSpan.FromHours(9)) };
        Create().Append(entry);

        Entry loaded = Assert.Single(Open().Live);
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal("Asked for access", loaded.Summary);
        Assert.Equal(entry.OccurredAt, loaded.OccurredAt);
        Assert.Equal(TimeSpan.FromHours(9), loaded.OccurredAt.Offset);
        Assert.Equal(["asked"], loaded.Tags);
        Assert.Equal(["j-doe"], loaded.People);
    }

    [Fact]
    public void AppendAddsOneEncryptedLineAndLeavesEarlierBytesUntouched()
    {
        LogFile log = Create();
        log.Append(NewEntry("first"));
        byte[] before = File.ReadAllBytes(_file.Path);

        log.Append(NewEntry("second"));
        byte[] after = File.ReadAllBytes(_file.Path);

        Assert.Equal(before, after[..before.Length]);
        string[] lines = FileLines();
        Assert.Equal(4, lines.Length); // header, two records, empty tail
        Assert.DoesNotContain("first", File.ReadAllText(_file.Path), StringComparison.Ordinal);
        Assert.Equal(12 + 16, Convert.FromBase64String(lines[1]).Length - Encoding.UTF8.GetByteCount(LogbookJson.Serialize(log.Revisions[0])));
    }

    [Fact]
    public void AppendIsVisibleInMemoryImmediately()
    {
        LogFile log = Create();
        log.Append(NewEntry("first"));
        Assert.Equal("first", Assert.Single(log.Live).Summary);
    }

    [Fact]
    public void HighestRevisionPerIdWins()
    {
        LogFile log = Create();
        Entry first = NewEntry("original");
        log.Append(first);
        log.Append(first with { Rev = 2, Summary = "edited" });

        Assert.Equal("edited", Assert.Single(log.Live).Summary);
        Assert.Equal("edited", Assert.Single(Open().Live).Summary);
    }

    [Fact]
    public void HighestRevisionWinsEvenWhenWrittenOutOfOrder()
    {
        LogFile log = Create();
        Entry first = NewEntry("original");
        log.Append(first with { Rev = 2, Summary = "edited" });
        log.Append(first);

        Assert.Equal("edited", Assert.Single(Open().Live).Summary);
    }

    [Fact]
    public void TombstoneHidesTheEntryButKeepsItsHistory()
    {
        LogFile log = Create();
        Entry first = NewEntry("original");
        Entry other = NewEntry("other");
        log.Append(first);
        log.Append(other);
        log.Append(first with { Rev = 2, Summary = "edited" });
        log.Append(first with { Rev = 3, Deleted = true });

        LogFile reopened = Open();
        Assert.Equal("other", Assert.Single(reopened.Live).Summary);
        Assert.Equal([1, 2, 3], reopened.RevisionsOf(first.Id).Select(e => e.Rev));
        Assert.Equal(4, reopened.Revisions.Count);
        Assert.Equal([first.Id, other.Id, first.Id, first.Id], reopened.Revisions.Select(e => e.Id));
    }

    [Fact]
    public void TombstoneWithOmittedFieldsStillHidesTheEntry()
    {
        LogFile log = Create();
        Entry first = NewEntry("original");
        log.Append(first);
        // Written by hand to mimic a minimal tombstone from another writer.
        string tombstone = "{\"id\":\"" + first.Id + "\",\"rev\":2,\"deleted\":true}";
        AppendRawLine(LineCipher.Encrypt(DataKeyOf(log), Encoding.UTF8.GetBytes(tombstone)));

        Assert.Empty(Open().Live);
    }

    [Fact]
    public void CorruptMiddleLineIsCountedAndSkipped()
    {
        LogFile log = Create();
        log.Append(NewEntry("first"));
        AppendRawLine("!!! not base64 !!!");
        AppendRawLine(Convert.ToBase64String(new byte[40])); // right shape, wrong tag
        log.Append(NewEntry("second"));

        LogFile reopened = Open();
        Assert.Equal(2, reopened.UnreadableLineCount);
        Assert.False(reopened.TrailingLineTruncated);
        Assert.Equal(["first", "second"], reopened.Live.Select(e => e.Summary));
    }

    [Fact]
    public void UnterminatedTrailingLineIsSkippedSilently()
    {
        LogFile log = Create();
        log.Append(NewEntry("first"));
        log.Append(NewEntry("second"));
        string[] lines = FileLines();
        File.WriteAllText(_file.Path, lines[0] + "\n" + lines[1] + "\n" + lines[2][..10]); // crash mid-write

        LogFile reopened = Open();
        Assert.True(reopened.TrailingLineTruncated);
        Assert.Equal(0, reopened.UnreadableLineCount);
        Assert.Equal("first", Assert.Single(reopened.Live).Summary);
    }

    [Fact]
    public void CompleteUnterminatedTrailingLineIsStillSkipped()
    {
        LogFile log = Create();
        log.Append(NewEntry("first"));
        log.Append(NewEntry("second"));
        File.WriteAllText(_file.Path, File.ReadAllText(_file.Path).TrimEnd('\n'));

        LogFile reopened = Open();
        Assert.True(reopened.TrailingLineTruncated);
        Assert.Equal("first", Assert.Single(reopened.Live).Summary);
    }

    [Fact]
    public void UnreadableTerminatedTrailingLineIsTreatedAsTruncated()
    {
        LogFile log = Create();
        log.Append(NewEntry("first"));
        AppendRawLine("AAAA");

        LogFile reopened = Open();
        Assert.True(reopened.TrailingLineTruncated);
        Assert.Equal(0, reopened.UnreadableLineCount);
        Assert.Equal("first", Assert.Single(reopened.Live).Summary);
    }

    [Fact]
    public void AppendAfterATruncatedLineKeepsWorking()
    {
        LogFile log = Create();
        log.Append(NewEntry("first"));
        File.WriteAllText(_file.Path, File.ReadAllText(_file.Path) + "garbage");

        LogFile reopened = Open();
        Assert.True(reopened.TrailingLineTruncated);
        reopened.Append(NewEntry("second"));

        // The new line starts on a fresh line, so the garbage becomes a counted middle line and "second" survives.
        LogFile again = Open();
        Assert.Equal(["first", "second"], again.Live.Select(e => e.Summary));
        Assert.Equal(1, again.UnreadableLineCount);
        Assert.False(again.TrailingLineTruncated);
    }

    [Fact]
    public void CompleteLineThatLostItsNewlineIsRecoveredByTheNextAppend()
    {
        LogFile log = Create();
        log.Append(NewEntry("first"));
        log.Append(NewEntry("second"));
        File.WriteAllText(_file.Path, File.ReadAllText(_file.Path).TrimEnd('\n'));

        Open().Append(NewEntry("third"));

        LogFile again = Open();
        Assert.Equal(["first", "second", "third"], again.Live.Select(e => e.Summary));
        Assert.Equal(0, again.UnreadableLineCount);
    }

    [Fact]
    public void RecordThatDecryptsToBadJsonOrHasNoIdIsCountedAsUnreadable()
    {
        LogFile log = Create();
        byte[] key = DataKeyOf(log);
        log.Append(NewEntry("first"));
        AppendRawLine(LineCipher.Encrypt(key, "{not json"u8));
        AppendRawLine(LineCipher.Encrypt(key, "{\"rev\":1,\"summary\":\"no id\"}"u8));
        log.Append(NewEntry("second"));

        LogFile reopened = Open();
        Assert.Equal(2, reopened.UnreadableLineCount);
        Assert.Equal(["first", "second"], reopened.Live.Select(e => e.Summary));
    }

    [Fact]
    public void OnEqualRevisionsTheLaterLineWinsAndEndsTheHistory()
    {
        LogFile log = Create();
        Entry first = NewEntry("original");
        log.Append(first);
        log.Append(first with { Rev = 2, Summary = "edit a" });
        log.Append(first with { Rev = 2, Summary = "edit b" }); // a double-clicked Save

        LogFile reopened = Open();
        Entry live = Assert.Single(reopened.Live);
        Assert.Equal("edit b", live.Summary);
        Assert.Equal(live.Summary, reopened.RevisionsOf(first.Id)[^1].Summary);
    }

    [Theory]
    [InlineData("\"iterations\":600000", "\"iterations\":0")]
    [InlineData("\"iterations\":600000", "\"iterations\":-5")]
    [InlineData("\"iterations\":600000", "\"iterations\":2000000000")]
    [InlineData("\"salt\":\"", "\"salt\":\"!!")]
    [InlineData("\"wrappedKey\":\"", "\"wrappedKey\":\"!!")]
    public void DamagedHeaderValuesAreRefusedAsInvalidData(string find, string replace)
    {
        Create();
        string[] lines = FileLines();
        Assert.Contains(find, lines[0], StringComparison.Ordinal);
        lines[0] = lines[0].Replace(find, replace, StringComparison.Ordinal);
        File.WriteAllText(_file.Path, string.Join('\n', lines));
        Assert.Throws<InvalidDataException>(() => Open());
    }

    [Fact]
    public void ShortSaltIsRefusedAsInvalidData()
    {
        FileHeader header = Create().Header;
        string shortSalt = Convert.ToBase64String(Convert.FromBase64String(header.Kdf.Salt)[..8]);
        FileHeader damaged = header with { Kdf = header.Kdf with { Salt = shortSalt } };
        File.WriteAllText(_file.Path, LogbookJson.Serialize(damaged) + "\n");
        Assert.Throws<InvalidDataException>(() => Open());
    }

    [Fact]
    public void ReadOnlyFileLeavesTheLinePendingInsteadOfThrowing()
    {
        LogFile log = Create();
        File.SetAttributes(_file.Path, FileAttributes.ReadOnly);
        try
        {
            Assert.False(log.Append(NewEntry("first")));
            Assert.Equal(1, log.PendingCount);
            Assert.Equal("first", Assert.Single(log.Live).Summary);
        }
        finally
        {
            File.SetAttributes(_file.Path, FileAttributes.Normal);
        }

        Assert.True(log.RetryPending());
        Assert.Equal("first", Assert.Single(Open().Live).Summary);
    }

    [Fact]
    public async Task ReadsDoNotWaitForAWriteThatIsRetrying()
    {
        IReadOnlyList<TimeSpan> slow = [.. Enumerable.Repeat(TimeSpan.FromMilliseconds(100), 20)];
        LogFile log = LogFile.Create(_file.Path, Passphrase, Now, slow);
        log.Append(NewEntry("first"));
        using FileStream held = new(_file.Path, FileMode.Open, FileAccess.Read, FileShare.None);

        Task<bool> write = Task.Run(() => log.Append(NewEntry("second")));
        await Task.Delay(150);
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        int live = log.Live.Count;
        int pending = log.PendingCount;
        clock.Stop();

        Assert.Equal(2, live);
        Assert.Equal(1, pending);
        Assert.True(clock.ElapsedMilliseconds < 500, $"reads took {clock.ElapsedMilliseconds} ms while a write was retrying");
        held.Dispose();
        Assert.True(await write);
    }

    private void AppendRawLine(string line) => File.AppendAllText(_file.Path, line + "\n");

    private static byte[] DataKeyOf(LogFile log)
    {
        byte[] salt = Convert.FromBase64String(log.Header.Kdf.Salt);
        return DataKey.Unwrap(log.Header.WrappedKey, KeyDerivation.Derive(Passphrase, salt, log.Header.Kdf.Iterations));
    }
}
