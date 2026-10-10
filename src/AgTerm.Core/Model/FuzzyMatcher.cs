using System.Globalization;

namespace AgTerm.Core.Model;

public static class FuzzyMatcher
{
    public static int? Score(string query, string target)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(target);

        var terms = query
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0)
        {
            return 0;
        }

        var normalizedTarget = target.ToLowerInvariant();
        var total = 0;
        foreach (var term in terms)
        {
            var score = ScoreTerm(term, normalizedTarget);
            if (score is null)
            {
                return null;
            }

            total = checked(total + score.Value);
        }

        return total;
    }

    public static IReadOnlyList<T> Rank<T>(
        string query,
        IReadOnlyList<T> items,
        Func<T, IReadOnlyList<string>> keys)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keys);

        return items
            .Select(item => RankItem(query, item, keys(item)))
            .Where(match => match is not null)
            .Select(match => match!.Value)
            .OrderBy(match => match.Score)
            .ThenBy(match => match.Label, StringComparer.CurrentCultureIgnoreCase)
            .Select(match => match.Item)
            .ToArray();
    }

    private static (T Item, int Score, string Label)? RankItem<T>(
        string query,
        T item,
        IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        int? best = null;
        foreach (var key in keys)
        {
            var score = Score(query, key);
            if (score is not null && (best is null || score < best))
            {
                best = score;
            }
        }

        return best is null
            ? null
            : (item, best.Value, keys.Count > 0 ? keys[0] : string.Empty);
    }

    private static int? ScoreTerm(string term, string target)
    {
        if (target.StartsWith(term, StringComparison.Ordinal))
        {
            return 0;
        }

        var substringIndex = target.IndexOf(term, StringComparison.Ordinal);
        if (substringIndex >= 0)
        {
            return 5 + Math.Min(CountTextElements(target.AsSpan(0, substringIndex)), 34);
        }

        var termElements = TextElements(term);
        var targetElements = TextElements(target);
        var termIndex = 0;
        foreach (var targetElement in targetElements)
        {
            if (targetElement == termElements[termIndex])
            {
                termIndex++;
                if (termIndex == termElements.Count)
                {
                    return 40 + targetElements.Count - termElements.Count;
                }
            }
        }

        return null;
    }

    private static int CountTextElements(ReadOnlySpan<char> value) =>
        StringInfo.ParseCombiningCharacters(value.ToString()).Length;

    private static IReadOnlyList<string> TextElements(string value)
    {
        var starts = StringInfo.ParseCombiningCharacters(value);
        var elements = new string[starts.Length];
        for (var index = 0; index < starts.Length; index++)
        {
            var length = index + 1 < starts.Length
                ? starts[index + 1] - starts[index]
                : value.Length - starts[index];
            elements[index] = value.Substring(starts[index], length);
        }

        return elements;
    }
}