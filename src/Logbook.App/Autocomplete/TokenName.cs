using Logbook.Core.Grammar;

namespace Logbook.App.Autocomplete;

/// <summary>
/// Whether a stored tag or person name reads back as that same token when typed with its sigil. Names entered in the
/// detail pane or the capture box's "more" fields need not follow the grammar (one starting with a digit, say), and
/// typing <c>#123</c> stores summary text, not a tag. The parser itself answers, so the rule lives in one place.
/// </summary>
internal static class TokenName
{
    public static bool ParsesBackAsTag(string name) => ParsesBack("#" + name, name, line => line.Tags);

    public static bool ParsesBackAsPerson(string name) => ParsesBack("@" + name, name, line => line.People);

    private static bool ParsesBack(string token, string name, Func<CaptureLine, IReadOnlyList<string>> pick)
    {
        IReadOnlyList<string> names = pick(LineParser.ParseCapture(token, DateTimeOffset.Now));
        return names.Count == 1 && names[0] == name;
    }
}
