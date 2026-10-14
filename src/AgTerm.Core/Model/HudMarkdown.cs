using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AgTerm.Core.Model;

[Flags]
public enum HudMarkdownStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Strikethrough = 4,
    Dim = 8,
    Underline = 16,
}

/// <summary>A styled piece of a row. Link is set only for a URL HudLinkPolicy accepts.</summary>
public sealed record HudMarkdownRun(string Text, HudMarkdownStyle Style, string? Link = null);

public enum HudMarkdownLineKind
{
    /// <summary>Wraps at word boundaries.</summary>
    Text,
    /// <summary>Wraps at the width, keeping every space.</summary>
    Code,
    /// <summary>Never wraps; a row wider than the panel is clipped.</summary>
    Table,
    /// <summary>A thematic break, drawn across the width its row is laid out at.</summary>
    Rule,
}

/// <summary>One logical row. Lead prefixes its first wrapped row (indent, quote bars, list marker) and
/// Hang every continuation, so wrapped list text hangs under the item rather than under the marker.</summary>
public sealed record HudMarkdownLine(string Lead, string Hang, IReadOnlyList<HudMarkdownRun> Runs,
                                     HudMarkdownLineKind Kind = HudMarkdownLineKind.Text)
{
    internal static readonly HudMarkdownLine Blank = new("", "", Array.Empty<HudMarkdownRun>());
}

/// <summary>Port of upstream HudMarkdown (ed831ab) over the Markdig AST: Lines walks the parsed blocks
/// into prefixed logical rows of styled runs, Rows wraps them and Fitted clips them to the panel grid.
/// The page draws the result as spans, so upstream's SGR encoding has no counterpart here.</summary>
public static class HudMarkdown
{
    public const string CodeIndent = "  ";
    public const string QuoteBar = "│ ";
    public const string Bullet = "• ";
    public const int MinRuleWidth = 3;
    public const int TabWidth = 4;
    public const string Ellipsis = "…";
    public const string RuleGlyph = "─";

    // spec 5.1: task lists and bare-URL autolinks stay off, as upstream's Foundation parser has neither
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();

    public static IReadOnlyList<HudMarkdownLine> Lines(string source) =>
        Lines(source, text => Markdown.Parse(text, Pipeline));

    internal static IReadOnlyList<HudMarkdownLine> Lines(string source, Func<string, MarkdownDocument> parse)
    {
        var text = source.Normalize(NormalizationForm.FormC);
        try
        {
            var walker = new Walker();
            walker.Walk(parse(text));
            return walker.Lines;
        }
        catch (Exception)
        {
            // upstream falls back the same way when its parser gives up; Markdig also throws on block nesting
            // past its cap, and the panel must still show the text
            return PlainLines(text);
        }
    }

    private static IReadOnlyList<HudMarkdownLine> PlainLines(string text) =>
        text.Split('\n')
            .Select(line => new HudMarkdownLine("", "", new[] { new HudMarkdownRun(Sanitized(line), HudMarkdownStyle.None) }))
            .ToList();

    /// <summary>Whether the source lays out to at least one non-space cell at 60 columns. The renderer's
    /// own glyphs count (markers, bars, borders, U+FFFD), so the check runs on rows, not on the page.</summary>
    public static bool RendersVisibleText(string source) =>
        Rows(Lines(source), HudLayout.MaxColumns)
            .Any(row => row.Any(run => run.Text.EnumerateRunes().Any(rune => !Rune.IsWhiteSpace(rune))));

    /// <summary>Control characters decoded from entities (&amp;#27;, &amp;#10;) must not split a row or
    /// reach the page as control text. A tab in running text becomes a space; code blocks expand theirs
    /// before this runs.</summary>
    public static string Sanitized(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == 0x09)
                builder.Append(' ');
            else if (rune.Value < 0x20 || rune.Value == 0x7F)
                builder.Append('\uFFFD');
            else
                builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    /// <summary>Replaces each tab with spaces up to the next TabWidth stop, counted from the line start.</summary>
    public static string ExpandTabs(string line)
    {
        var builder = new StringBuilder(line.Length);
        var column = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (rune.Value == '\t')
            {
                var pad = TabWidth - column % TabWidth;
                builder.Append(' ', pad);
                column += pad;
                continue;
            }
            builder.Append(rune.ToString());
            column++;
        }
        return builder.ToString();
    }

    private enum SegmentKind { Text, Code, Html, SoftBreak, HardBreak }

    /// <summary>One inline leaf with the emphasis and link of the inlines around it. Upstream's runs are
    /// Foundation attribute runs, so two segments join only where Foundation sees the same attributes:
    /// plain text with the same emphasis and link. A code span, raw HTML and a soft break stay apart.</summary>
    private readonly record struct Segment(string Text, HudMarkdownStyle Emphasis, string? Url, SegmentKind Kind)
    {
        public bool JoinsWith(Segment next) =>
            Kind == SegmentKind.Text && next.Kind == SegmentKind.Text && Emphasis == next.Emphasis && Url == next.Url;
    }

    private sealed class Walker
    {
        public readonly List<HudMarkdownLine> Lines = new();
        private readonly HashSet<ListItemBlock> _seenItems = new();
        private HashSet<ListBlock>? _lastLists;

        /// <summary>Visits the blocks in document order. The walk keeps its own stack of open containers:
        /// Markdig caps block nesting at 128 today, but a stack overflow cannot be caught, so the walk does
        /// not lean on that cap.</summary>
        public void Walk(MarkdownDocument document)
        {
            var path = new List<ContainerBlock> { document };
            var nextChild = new List<int> { 0 };
            while (path.Count > 0)
            {
                var container = path[^1];
                var index = nextChild[^1];
                if (index == container.Count)
                {
                    path.RemoveAt(path.Count - 1);
                    nextChild.RemoveAt(nextChild.Count - 1);
                    continue;
                }
                nextChild[^1] = index + 1;
                switch (container[index])
                {
                    case LinkReferenceDefinitionGroup:
                        break;
                    case Table table:
                        Emit(TableLines(table, path), path);
                        break;
                    case ContainerBlock child:
                        path.Add(child);
                        nextChild.Add(0);
                        break;
                    case LeafBlock leaf:
                        Emit(LeafLines(leaf, path), path);
                        break;
                }
            }
        }

        // tight and loose lists render alike, as upstream, whose parser does not say which a list was
        private void Emit(IReadOnlyList<HudMarkdownLine> lines, IReadOnlyList<ContainerBlock> path)
        {
            if (lines.Count == 0)
                return;
            var lists = path.OfType<ListBlock>().ToHashSet();
            if (_lastLists is { } last && (lists.Count == 0 || !last.Overlaps(lists)))
                Lines.Add(HudMarkdownLine.Blank);
            _lastLists = lists;
            Lines.AddRange(lines);
        }

        private IReadOnlyList<HudMarkdownLine> LeafLines(LeafBlock leaf, IReadOnlyList<ContainerBlock> path)
        {
            if (leaf is LinkReferenceDefinition)
                return Array.Empty<HudMarkdownLine>();
            var (lead, hang) = Prefixes(path);
            return leaf switch
            {
                ThematicBreakBlock => new[] { new HudMarkdownLine(lead, hang, Array.Empty<HudMarkdownRun>(), HudMarkdownLineKind.Rule) },
                HtmlBlock html => TextLines(LiteralRows(html), lead, hang),
                CodeBlock code => CodeLines(code, lead, hang),
                HeadingBlock heading => TextLines(InlineRows(heading.Inline, HudMarkdownStyle.Bold), lead, hang),
                { Inline: { } inline } => TextLines(InlineRows(inline, HudMarkdownStyle.None), lead, hang),
                _ => TextLines(LiteralRows(leaf), lead, hang),
            };
        }

        /// <summary>Frames the table in box drawing, with a rule under the header when it has content.
        /// The top border takes the container's lead, so a table opening a list item carries its marker once.</summary>
        private IReadOnlyList<HudMarkdownLine> TableLines(Table table, IReadOnlyList<ContainerBlock> path)
        {
            var tableRows = table.OfType<TableRow>().ToList();
            var headerRow = tableRows.FirstOrDefault(row => row.IsHeader);
            var columns = headerRow?.Count ?? tableRows.Select(row => row.Count).DefaultIfEmpty(0).Max();
            var header = headerRow is null ? null : Cells(headerRow, columns, HudMarkdownStyle.Bold);
            var hasHeader = header is not null && header.Any(cell => cell.Count > 0);
            var body = tableRows.Where(row => !row.IsHeader).Select(row => Cells(row, columns, HudMarkdownStyle.None)).ToList();
            // upstream drops a trailing all-empty row, because Foundation emits no run for it
            while (body.Count > 0 && body[^1].All(cell => cell.Count == 0))
                body.RemoveAt(body.Count - 1);
            var grid = (hasHeader ? new[] { header! } : Array.Empty<IReadOnlyList<HudMarkdownRun>[]>()).Concat(body).ToList();
            if (grid.Count == 0 || columns == 0)
                return Array.Empty<HudMarkdownLine>();

            var widths = Enumerable.Range(0, columns).Select(column => grid.Max(row => Width(row[column]))).ToArray();
            var (lead, hang) = Prefixes(path);
            var frame = new List<IReadOnlyList<HudMarkdownRun>> { Border("┌", "┬", "┐", widths) };
            for (var index = 0; index < grid.Count; index++)
            {
                frame.Add(Framed(grid[index], widths));
                if (index == 0 && hasHeader)
                    frame.Add(Border("├", "┼", "┤", widths));
            }
            frame.Add(Border("└", "┴", "┘", widths));
            return frame.Select((runs, index) => new HudMarkdownLine(index == 0 ? lead : hang, hang, runs, HudMarkdownLineKind.Table)).ToList();
        }

        private static IReadOnlyList<HudMarkdownRun>[] Cells(TableRow row, int columns, HudMarkdownStyle style) =>
            Enumerable.Range(0, columns)
                .Select(column => column < row.Count && row[column] is TableCell cell ? CellRuns(cell, style) : Array.Empty<HudMarkdownRun>())
                .ToArray();

        private static IReadOnlyList<HudMarkdownRun> CellRuns(TableCell cell, HudMarkdownStyle style) =>
            cell.OfType<LeafBlock>().SelectMany(leaf => InlineRows(leaf.Inline, style).SelectMany(row => row)).ToList();

        private static IReadOnlyList<HudMarkdownRun> Border(string left, string join, string right, int[] widths) =>
            new[] { new HudMarkdownRun(left + string.Join(join, widths.Select(width => new string('─', width + 2))) + right, HudMarkdownStyle.None) };

        private static IReadOnlyList<HudMarkdownRun> Framed(IReadOnlyList<HudMarkdownRun>[] row, int[] widths)
        {
            var runs = new List<HudMarkdownRun> { new("│ ", HudMarkdownStyle.None) };
            for (var column = 0; column < row.Length; column++)
            {
                if (column > 0)
                    runs.Add(new HudMarkdownRun(" ", HudMarkdownStyle.None));
                runs.AddRange(row[column]);
                runs.Add(new HudMarkdownRun(new string(' ', widths[column] - Width(row[column]) + 1) + "│", HudMarkdownStyle.None));
            }
            return runs;
        }

        private static IReadOnlyList<HudMarkdownLine> CodeLines(CodeBlock code, string lead, string hang) =>
            SourceLines(code)
                .Select((raw, index) => new HudMarkdownLine(
                    (index == 0 ? lead : hang) + CodeIndent,
                    hang + CodeIndent,
                    new[] { new HudMarkdownRun(Sanitized(ExpandTabs(raw)), HudMarkdownStyle.None) },
                    HudMarkdownLineKind.Code))
                .ToList();

        private static IReadOnlyList<IReadOnlyList<HudMarkdownRun>> LiteralRows(LeafBlock leaf)
        {
            var lines = SourceLines(leaf);
            while (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            return lines.Select(line => (IReadOnlyList<HudMarkdownRun>)new[] { new HudMarkdownRun(Sanitized(line), HudMarkdownStyle.None) }).ToList();
        }

        private static List<string> SourceLines(LeafBlock leaf)
        {
            var lines = new List<string>(leaf.Lines.Count);
            for (var index = 0; index < leaf.Lines.Count; index++)
                lines.Add(leaf.Lines.Lines[index].Slice.ToString());
            return lines;
        }

        private static IReadOnlyList<HudMarkdownLine> TextLines(IReadOnlyList<IReadOnlyList<HudMarkdownRun>> rows, string lead, string hang) =>
            rows.Select((runs, index) => new HudMarkdownLine(index == 0 ? lead : hang, hang, runs)).ToList();

        /// <summary>Maps inline emphasis onto styles and splits at hard breaks; a soft break is a space.
        /// A link is its label, underlined only when a click can open it.</summary>
        private static IReadOnlyList<IReadOnlyList<HudMarkdownRun>> InlineRows(ContainerInline? inline, HudMarkdownStyle baseStyle)
        {
            var segments = inline is null ? new List<Segment>() : Segments(inline);
            var rows = new List<List<HudMarkdownRun>> { new() };
            Segment? previous = null;
            foreach (var segment in segments)
            {
                if (segment.Kind == SegmentKind.HardBreak)
                {
                    rows.Add(new List<HudMarkdownRun>());
                    previous = null;
                    continue;
                }
                var text = segment.Kind == SegmentKind.SoftBreak ? " " : Sanitized(segment.Text);
                if (text.Length == 0)
                    continue;
                var target = segment.Url is null ? null : HudLinkPolicy.Target(segment.Url);
                var style = baseStyle | segment.Emphasis | (target is null ? HudMarkdownStyle.None : HudMarkdownStyle.Underline);
                var row = rows[^1];
                if (previous is { } last && last.JoinsWith(segment) && row.Count > 0)
                    row[^1] = row[^1] with { Text = row[^1].Text + text };
                else
                    row.Add(new HudMarkdownRun(text, style, target));
                previous = segment;
            }
            return rows;
        }

        private readonly record struct PendingInline(Inline Inline, HudMarkdownStyle Emphasis, string? Url);

        /// <summary>The leaves of inline in document order. Markdig keeps each unmatched "[" or "![" as a
        /// container holding the rest of the paragraph, so the nesting grows with the source; the walk keeps
        /// its own stack, because a stack overflow cannot be caught.</summary>
        private static List<Segment> Segments(ContainerInline inline)
        {
            var segments = new List<Segment>();
            var pending = new Stack<PendingInline>();
            PushChildren(inline, HudMarkdownStyle.None, null, pending);
            while (pending.TryPop(out var next))
                Collect(next.Inline, next.Emphasis, next.Url, segments, pending);
            return segments;
        }

        private static void Collect(Inline inline, HudMarkdownStyle emphasis, string? url, List<Segment> into, Stack<PendingInline> pending)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    into.Add(new Segment(literal.Content.ToString(), emphasis, url, SegmentKind.Text));
                    break;
                case HtmlEntityInline entity:
                    into.Add(new Segment(entity.Transcoded.ToString(), emphasis, url, SegmentKind.Text));
                    break;
                case CodeInline code:
                    into.Add(new Segment(code.Content, emphasis, url, SegmentKind.Code));
                    break;
                case HtmlInline html:
                    into.Add(new Segment(html.Tag, emphasis, url, SegmentKind.Html));
                    break;
                case LineBreakInline lineBreak:
                    into.Add(new Segment("", emphasis, url, lineBreak.IsHard ? SegmentKind.HardBreak : SegmentKind.SoftBreak));
                    break;
                case AutolinkInline autolink:
                    into.Add(new Segment(autolink.Url, emphasis, autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url, SegmentKind.Text));
                    break;
                case LinkInline { IsImage: true } image:
                    // an image is its alt text, linked only when a link holds it
                    PushChildren(image, emphasis, url, pending);
                    break;
                case LinkInline link:
                    PushChildren(link, emphasis, link.Url ?? "", pending);
                    break;
                case EmphasisInline emphasisInline:
                    PushChildren(emphasisInline, emphasis | EmphasisStyle(emphasisInline), url, pending);
                    break;
                case DelimiterInline delimiter:
                    into.Add(new Segment(delimiter.ToLiteral(), emphasis, url, SegmentKind.Text));
                    PushChildren(delimiter, emphasis, url, pending);
                    break;
                case ContainerInline container:
                    PushChildren(container, emphasis, url, pending);
                    break;
            }
        }

        // last child first, so the first child pops next and the output keeps document order
        private static void PushChildren(ContainerInline container, HudMarkdownStyle emphasis, string? url, Stack<PendingInline> pending)
        {
            for (var child = container.LastChild; child is not null; child = child.PreviousSibling)
                pending.Push(new PendingInline(child, emphasis, url));
        }

        private static HudMarkdownStyle EmphasisStyle(EmphasisInline emphasis) =>
            emphasis.DelimiterChar == '~' ? HudMarkdownStyle.Strikethrough
            : emphasis.DelimiterCount >= 2 ? HudMarkdownStyle.Bold
            : HudMarkdownStyle.Italic;

        /// <summary>Walks the containers outermost first: a quote adds a bar to both prefixes, and a list
        /// item adds its marker to Lead only for the item's first block, spaces of the same width otherwise.</summary>
        private (string Lead, string Hang) Prefixes(IReadOnlyList<ContainerBlock> containers)
        {
            var lead = new StringBuilder();
            var hang = new StringBuilder();
            foreach (var container in containers)
            {
                switch (container)
                {
                    case QuoteBlock:
                        lead.Append(QuoteBar);
                        hang.Append(QuoteBar);
                        break;
                    case ListItemBlock item:
                        var marker = item.Parent is ListBlock { IsOrdered: true }
                            ? item.Order.ToString(CultureInfo.InvariantCulture) + ". "
                            : Bullet;
                        var pad = new string(' ', HudLayout.CellCount(marker));
                        lead.Append(_seenItems.Add(item) ? marker : pad);
                        hang.Append(pad);
                        break;
                }
            }
            return (lead.ToString(), hang.ToString());
        }
    }

    private readonly record struct Cell(Rune Scalar, HudMarkdownStyle Style, string? Link);

    /// <summary>Wraps the lines at width cells. Every row starts with its line's Lead (first row) or
    /// Hang (continuations); a blank line is one empty row. A rule spans the widest other row, and its
    /// glyphs number at least MinRuleWidth after any container prefix.</summary>
    public static IReadOnlyList<IReadOnlyList<HudMarkdownRun>> Rows(IReadOnlyList<HudMarkdownLine> lines, int width)
    {
        var widest = lines.Where(line => line.Kind != HudMarkdownLineKind.Rule)
            .SelectMany(line => Wrapped(line, width)).Select(Width).DefaultIfEmpty(0).Max();
        var ruleWidth = Math.Min(Math.Max(widest, MinRuleWidth), width);
        return lines.SelectMany(line => Wrapped(line, line.Kind == HudMarkdownLineKind.Rule ? ruleWidth : width)).ToList();
    }

    /// <summary>Clips rows to a columns by rowLimit budget. A row too wide keeps columns - 1 cells and
    /// ends in the ellipsis; too many rows keep rowLimit - 1 and end in a dim "… N more", N counting
    /// every hidden row.</summary>
    public static IReadOnlyList<IReadOnlyList<HudMarkdownRun>> Fitted(IReadOnlyList<IReadOnlyList<HudMarkdownRun>> rows, int columns, int rowLimit)
    {
        if (rowLimit <= 0 || columns <= 0)
            return Array.Empty<IReadOnlyList<HudMarkdownRun>>();
        var kept = rows.ToList();
        if (rows.Count > rowLimit)
        {
            kept = rows.Take(rowLimit - 1).ToList();
            kept.Add(new[] { new HudMarkdownRun($"{Ellipsis} {rows.Count - kept.Count} more", HudMarkdownStyle.Dim) });
        }
        return kept.Select(row => Clipped(row, columns)).ToList();
    }

    public static IReadOnlyList<HudMarkdownRun> Clipped(IReadOnlyList<HudMarkdownRun> row, int columns)
    {
        if (Width(row) <= columns)
            return row;
        if (columns <= 0)
            return Array.Empty<HudMarkdownRun>();
        var runs = Merged(Cells(row).Take(columns - 1));
        runs.Add(new HudMarkdownRun(Ellipsis, HudMarkdownStyle.None));
        return runs;
    }

    public static int Width(IReadOnlyList<HudMarkdownRun> row) => row.Sum(run => HudLayout.CellCount(run.Text));

    private static IEnumerable<IReadOnlyList<HudMarkdownRun>> Wrapped(HudMarkdownLine line, int width)
    {
        var leadWidth = HudLayout.CellCount(line.Lead);
        switch (line.Kind)
        {
            case HudMarkdownLineKind.Rule:
                var glyphs = string.Concat(Enumerable.Repeat(RuleGlyph, Math.Max(width - leadWidth, MinRuleWidth)));
                return new[] { Prefixed(line.Lead, new[] { new HudMarkdownRun(glyphs, HudMarkdownStyle.None) }) };
            case HudMarkdownLineKind.Table:
                return new[] { Prefixed(line.Lead, line.Runs) };
            default:
                var first = Math.Max(width - leadWidth, 1);
                var rest = Math.Max(width - HudLayout.CellCount(line.Hang), 1);
                var cells = Cells(line.Runs).ToList();
                var broken = line.Kind == HudMarkdownLineKind.Code ? HardWrapped(cells, first, rest) : WordWrapped(cells, first, rest);
                return broken.Select((row, index) => Prefixed(index == 0 ? line.Lead : line.Hang, Merged(row))).ToList();
        }
    }

    private static List<List<Cell>> HardWrapped(List<Cell> cells, int first, int rest)
    {
        var rows = new List<List<Cell>>();
        var index = 0;
        do
        {
            var limit = rows.Count == 0 ? first : rest;
            rows.Add(cells.Skip(index).Take(limit).ToList());
            index += limit;
        } while (index < cells.Count);
        return rows;
    }

    /// <summary>Breaks at spaces, carrying each word's styles across the break, and splits a word longer
    /// than the row. Spaces are kept as written, the line's leading ones included, since code spans and
    /// raw HTML carry their spacing as content; they are dropped at a row break and at the line's end.</summary>
    private static List<List<Cell>> WordWrapped(List<Cell> cells, int first, int rest)
    {
        var rows = new List<List<Cell>>();
        var current = new List<Cell>();
        int Limit() => rows.Count == 0 ? first : rest;
        var words = Words(cells);
        for (var index = 0; index < words.Count; index++)
        {
            var (entry, separator) = words[index];
            var word = index == 0 ? separator.Concat(entry).ToList() : entry;
            if (current.Count > 0)
            {
                if (current.Count + separator.Count + word.Count <= Limit())
                {
                    current.AddRange(separator);
                    current.AddRange(word);
                    continue;
                }
                rows.Add(current);
                current = new List<Cell>();
            }
            while (word.Count > Limit())
            {
                var limit = Limit();
                rows.Add(word.Take(limit).ToList());
                word = word.Skip(limit).ToList();
            }
            current = word;
        }
        if (current.Count > 0 || rows.Count == 0)
            rows.Add(current);
        return rows;
    }

    private static List<(List<Cell> Word, List<Cell> Separator)> Words(List<Cell> cells)
    {
        var words = new List<(List<Cell> Word, List<Cell> Separator)>();
        var word = new List<Cell>();
        var separator = new List<Cell>();
        foreach (var cell in cells)
        {
            if (cell.Scalar.Value == ' ')
            {
                if (word.Count > 0)
                {
                    words.Add((word, separator));
                    word = new List<Cell>();
                    separator = new List<Cell>();
                }
                separator.Add(cell);
                continue;
            }
            word.Add(cell);
        }
        if (word.Count > 0)
            words.Add((word, separator));
        return words;
    }

    private static IEnumerable<Cell> Cells(IReadOnlyList<HudMarkdownRun> runs) =>
        runs.SelectMany(run => run.Text.EnumerateRunes().Select(rune => new Cell(rune, run.Style, run.Link)));

    private static List<HudMarkdownRun> Merged(IEnumerable<Cell> cells)
    {
        var runs = new List<HudMarkdownRun>();
        var text = new StringBuilder();
        (HudMarkdownStyle Style, string? Link)? current = null;
        foreach (var cell in cells)
        {
            if (current is { } open && (open.Style != cell.Style || open.Link != cell.Link))
            {
                runs.Add(new HudMarkdownRun(text.ToString(), open.Style, open.Link));
                text.Clear();
                current = null;
            }
            current ??= (cell.Style, cell.Link);
            text.Append(cell.Scalar.ToString());
        }
        if (current is { } tail)
            runs.Add(new HudMarkdownRun(text.ToString(), tail.Style, tail.Link));
        return runs;
    }

    private static IReadOnlyList<HudMarkdownRun> Prefixed(string prefix, IReadOnlyList<HudMarkdownRun> runs) =>
        prefix.Length == 0 ? runs : new[] { new HudMarkdownRun(prefix, HudMarkdownStyle.None) }.Concat(runs).ToList();
}
