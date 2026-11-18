namespace ContextNotes.SelectionProbe;

internal sealed record Fixture(string Name, string Expected);
internal sealed record FixtureMatch(string Name, bool Exact);

internal static class Fixtures
{
    private static readonly Fixture[] Samples =
    [
        new("prose", "P1 中文，标点。 English, test!"),
        new("code", "P1 = \"中  文, English!\"\n  P2 = \"空格; test?\""),
        new("rapid-a", "中文，标点。"),
        new("rapid-b", "English, test!"),
        new("user-negative", "P1-USER-NEGATIVE-582"),
        new("draft-negative", "P1-EDIT-NEGATIVE-582")
    ];

    public static FixtureMatch? Match(string text)
    {
        foreach (Fixture fixture in Samples)
        {
            if (string.Equals(text, fixture.Expected, StringComparison.Ordinal))
                return new FixtureMatch(fixture.Name, Exact: true);
        }

        // The alternate forms identify the fixture without marking it as an exact match.
        string withLf = text.ReplaceLineEndings("\n");
        foreach (Fixture fixture in Samples)
        {
            if (string.Equals(withLf, fixture.Expected, StringComparison.Ordinal)
                || string.Equals(withLf, fixture.Expected + "\n", StringComparison.Ordinal))
                return new FixtureMatch(fixture.Name, Exact: false);
        }
        return null;
    }
}
