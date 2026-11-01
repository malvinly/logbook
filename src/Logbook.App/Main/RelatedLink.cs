namespace Logbook.App.Main;

/// <summary>
/// One "Follows up on" or "Followed up by" line in the detail pane: the text to show and the id to select when it is clicked.
/// <see cref="Exists"/> is false when the original has since been deleted, so there is nothing to select.
/// </summary>
public sealed record RelatedLink(string Id, string Text, bool Exists);
