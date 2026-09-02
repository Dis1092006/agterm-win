using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class FuzzyMatcherTests
{
    private sealed record Command(string Title, string Subtitle);

    [Theory]
    [InlineData("", "anything", 0)]
    [InlineData("  \t ", "anything", 0)]
    [InlineData("new", "New Session", 0)]
    [InlineData("NEW", "new session", 0)]
    [InlineData("session", "New Session", 9)]
    public void Score_matches_empty_prefix_case_and_substring_contract(
        string query,
        string target,
        int expected)
    {
        Assert.Equal(expected, FuzzyMatcher.Score(query, target));
    }

    [Fact]
    public void Score_places_subsequence_above_substring_and_returns_null_for_no_match()
    {
        var prefix = FuzzyMatcher.Score("ne", "New Session");
        var substring = FuzzyMatcher.Score("ew", "New Session");
        var subsequence = FuzzyMatcher.Score("nss", "New Session");

        Assert.NotNull(prefix);
        Assert.NotNull(substring);
        Assert.NotNull(subsequence);
        Assert.True(prefix < substring);
        Assert.True(substring < subsequence);
        Assert.True(subsequence >= 40);
        Assert.Null(FuzzyMatcher.Score("xyz", "New Session"));
    }

    [Theory]
    [InlineData("cap dev")]
    [InlineData("dev cap")]
    [InlineData("cap   dev")]
    public void Score_matches_all_whitespace_terms_independent_of_order(string query)
    {
        Assert.NotNull(FuzzyMatcher.Score(query, "caprica-dev"));
    }

    [Fact]
    public void Score_requires_every_term()
    {
        Assert.Null(FuzzyMatcher.Score("cap xyz", "caprica-dev"));
    }

    [Fact]
    public void Rank_sorts_best_matches_first_and_drops_nonmatches()
    {
        string[] items = ["New Workspace", "New Session", "Close Session"];

        var ranked = FuzzyMatcher.Rank("new", items, static item => [item]);

        Assert.Equal(["New Session", "New Workspace"], ranked);
    }

    [Fact]
    public void Rank_prefers_the_lower_score()
    {
        string[] items = ["New Session", "Session List"];

        var ranked = FuzzyMatcher.Rank("sess", items, static item => [item]);

        Assert.Equal(["Session List", "New Session"], ranked);
    }

    [Fact]
    public void Rank_uses_the_best_score_across_all_keys()
    {
        Command[] items =
        [
            new("Toggle Split", "panes"),
            new("Reload", "split config"),
        ];

        var ranked = FuzzyMatcher.Rank(
            "split",
            items,
            static item => [item.Title, item.Subtitle]);

        Assert.Equal([items[1], items[0]], ranked);
    }

    [Fact]
    public void Rank_empty_query_alphabetizes_by_first_key_case_insensitively()
    {
        string[] items = ["beta", "Alpha", "gamma"];

        var ranked = FuzzyMatcher.Rank("", items, static item => [item]);

        Assert.Equal(["Alpha", "beta", "gamma"], ranked);
    }

    [Fact]
    public void Rank_subsequence_characters_must_match_in_order()
    {
        string[] items = ["Split Pane", "Panel Split"];

        var ranked = FuzzyMatcher.Rank("ps", items, static item => [item]);

        Assert.Equal(["Panel Split"], ranked);
    }
}