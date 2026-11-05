using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AgTerm.Core.Model;

/// <summary>Why a session info source was refused, worded for the control response.</summary>
internal sealed record SessionInfoParseError(string Message)
{
    public static readonly SessionInfoParseError Empty = new("info text is empty");
    public static readonly SessionInfoParseError LineBreak = new("info text must be one line");
    public static readonly SessionInfoParseError ControlCharacter = new("info text contains a control character");
    public static readonly SessionInfoParseError InvalidMarkdown =
        new("invalid info markdown; quote attribute values: ^[text](color: \"green\")");

    public static SessionInfoParseError UnknownColor(string name) =>
        new($"unknown info color: {name} ({TintColors.ValidNamesList})");
}

internal sealed class SessionInfoParseException(SessionInfoParseError error) : Exception(error.Message)
{
    public SessionInfoParseError Error { get; } = error;
}

/// <summary>Port of upstream SessionInfoMarkup (80f822bf). Upstream reads markdown with Foundation's
/// inline-only parser that keeps whitespace, plus <c>^[text](color: "NAME")</c> and <c>^[text](dim: true)</c>
/// whose attributes are JSON5. Here Markdig runs its inline parsers over the line as one paragraph with
/// no block parsing, so block syntax stays literal, and a custom parser reads the <c>^[…](…)</c> blocks. A
/// link keeps its label; an unknown attribute key renders unstyled.</summary>
internal static class SessionInfoMarkup
{
    private static readonly InlineParserList Parsers = BuildParsers();

    /// <summary>Parses <paramref name="source"/> as markdown when <paramref name="markdown"/> is set,
    /// otherwise as literal text. The visible text must be non-blank and one line with no control
    /// characters, checked after entities are decoded.</summary>
    public static IReadOnlyList<SessionInfoSpan> Spans(string source, bool markdown)
    {
        var spans = markdown ? ParseMarkdown(source) : new[] { new SessionInfoSpan(source) };
        Validate(string.Concat(spans.Select(span => span.Text)));
        // Foundation keeps every line break of the source; a paragraph drops a leading or trailing one
        if (markdown && source.AsSpan().IndexOfAny('\n', '\r') >= 0)
            throw new SessionInfoParseException(SessionInfoParseError.LineBreak);
        return spans;
    }

    private static SessionInfoSpan[] ParseMarkdown(string source)
    {
        var spans = new List<SessionInfoSpan>();
        foreach (var raw in RawSpans(source))
        {
            if (raw.Text.Length == 0) continue;
            TintColor? color = null;
            if (raw.Color is { } name)
            {
                if (!TintColors.TryParse(name.ToLowerInvariant(), out var known))
                    throw new SessionInfoParseException(SessionInfoParseError.UnknownColor(name));
                color = known;
            }
            var span = new SessionInfoSpan(raw.Text, raw.Bold, raw.Italic, raw.Code, raw.Dim ?? false, color);
            if (spans.Count > 0 && spans[^1].SameStyle(span))
            {
                spans[^1] = spans[^1] with { Text = spans[^1].Text + span.Text };
                continue;
            }
            spans.Add(span);
        }
        return spans.ToArray();
    }

    private static void Validate(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case 0x0A or 0x0D or 0x2028 or 0x2029 or 0x85:
                    throw new SessionInfoParseException(SessionInfoParseError.LineBreak);
                default:
                    if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
                        throw new SessionInfoParseException(SessionInfoParseError.ControlCharacter);
                    break;
            }
        }
        // Foundation trims CharacterSet.whitespaces: the Zs category and a tab, which is refused above
        if (text.EnumerateRunes().All(rune => Rune.GetUnicodeCategory(rune) == UnicodeCategory.SpaceSeparator))
            throw new SessionInfoParseException(SessionInfoParseError.Empty);
    }

    /// <summary>A run before its color name is checked. Dim and Color stay null when no block set them,
    /// so a nested block's value wins over an outer one only where it sets it.</summary>
    internal readonly record struct RawSpan(string Text, bool Bold, bool Italic, bool Code, bool? Dim, string? Color);

    /// <summary>The source as runs. Markdig trims a paragraph's outer spaces and tabs, which Foundation keeps,
    /// so they go around the parsed runs as plain text.</summary>
    internal static List<RawSpan> RawSpans(string source)
    {
        var leading = source.Length - source.AsSpan().TrimStart(" \t").Length;
        var body = source.AsSpan(leading).TrimEnd(" \t").ToString();
        var trailing = source.Length - leading - body.Length;
        var spans = new List<RawSpan>();
        if (leading > 0) spans.Add(new RawSpan(source[..leading], false, false, false, null, null));
        if (body.Length > 0) Collect(ParseInlines(body), spans);
        if (trailing > 0) spans.Add(new RawSpan(source[^trailing..], false, false, false, null, null));
        return spans;
    }

    private static ContainerInline? ParseInlines(string text)
    {
        var document = new MarkdownDocument();
        var paragraph = new ParagraphBlock();
        var slice = new StringSlice(text);
        paragraph.AppendLine(ref slice, 0, 0, 0, false);
        document.Add(paragraph);
        new InlineProcessor(document, Parsers, false, null, false).ProcessInlineLeaf(paragraph);
        return paragraph.Inline;
    }

    private readonly record struct Pending(Inline Inline, bool Bold, bool Italic);

    private static void Collect(ContainerInline? root, List<RawSpan> into)
    {
        if (root is null) return;
        var pending = new Stack<Pending>();
        PushChildren(root, false, false, pending);
        while (pending.TryPop(out var next))
        {
            var (bold, italic) = (next.Bold, next.Italic);
            switch (next.Inline)
            {
                case LiteralInline literal:
                    into.Add(new RawSpan(literal.Content.ToString(), bold, italic, false, null, null));
                    break;
                case HtmlEntityInline entity:
                    into.Add(new RawSpan(entity.Transcoded.ToString(), bold, italic, false, null, null));
                    break;
                case CodeInline code:
                    into.Add(new RawSpan(code.Content, bold, italic, true, null, null));
                    break;
                case HtmlInline html:
                    into.Add(new RawSpan(html.Tag, bold, italic, false, null, null));
                    break;
                case LineBreakInline:
                    into.Add(new RawSpan("\n", bold, italic, false, null, null));
                    break;
                case AutolinkInline autolink:
                    into.Add(new RawSpan(autolink.Url, bold, italic, false, null, null));
                    break;
                case AttributedInline attributed:
                    foreach (var span in attributed.Spans)
                        into.Add(span with { Bold = span.Bold || bold, Italic = span.Italic || italic });
                    break;
                case EmphasisInline emphasis:
                    // strikethrough has no counterpart in the row's spans; its text stays
                    PushChildren(emphasis,
                        bold || (emphasis.DelimiterChar != '~' && emphasis.DelimiterCount >= 2),
                        italic || (emphasis.DelimiterChar != '~' && emphasis.DelimiterCount == 1),
                        pending);
                    break;
                case DelimiterInline delimiter:
                    into.Add(new RawSpan(delimiter.ToLiteral(), bold, italic, false, null, null));
                    PushChildren(delimiter, bold, italic, pending);
                    break;
                case ContainerInline container:
                    PushChildren(container, bold, italic, pending);
                    break;
            }
        }
    }

    // last child first, so the first child pops next and the output keeps document order
    private static void PushChildren(ContainerInline container, bool bold, bool italic, Stack<Pending> pending)
    {
        for (var child = container.LastChild; child is not null; child = child.PreviousSibling)
            pending.Push(new Pending(child, bold, italic));
    }

    private static InlineParserList BuildParsers()
    {
        var builder = new MarkdownPipelineBuilder().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough);
        builder.InlineParsers.Insert(0, new AttributedInlineParser());
        // Build runs each extension's setup against the builder's parser list, which is what is read here
        builder.Build();
        return new InlineParserList(builder.InlineParsers);
    }

    /// <summary>A <c>^[text](attributes)</c> block, its text already parsed with the attributes applied.</summary>
    private sealed class AttributedInline(IReadOnlyList<RawSpan> spans) : LeafInline
    {
        public IReadOnlyList<RawSpan> Spans { get; } = spans;
    }

    /// <summary>Reads <c>^[text](attributes)</c>. Brackets nest and skip escapes and code spans, as link
    /// text does; the attributes end at the first unquoted, unnested <c>)</c>. Anything that does not close
    /// stays literal text. Attributes that close but are not a JSON5 object body refuse the whole line.</summary>
    private sealed class AttributedInlineParser : InlineParser
    {
        public AttributedInlineParser() => OpeningCharacters = ['^'];

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            var text = slice.Text;
            var start = slice.Start;
            var end = slice.End;
            if (start + 1 > end || text[start + 1] != '[') return false;
            if (CloseBracket(text, start + 2, end) is not { } close) return false;
            if (close + 1 > end || text[close + 1] != '(') return false;
            if (CloseParen(text, close + 2, end) is not { } closeParen) return false;
            var attributes = Json5Members.Parse(text.AsSpan(close + 2, closeParen - close - 2))
                             ?? throw new SessionInfoParseException(SessionInfoParseError.InvalidMarkdown);
            var (dim, color) = Styles(attributes);
            var inner = RawSpans(text.Substring(start + 2, close - start - 2))
                .Select(span => span with { Dim = span.Dim ?? dim, Color = span.Color ?? color })
                .ToList();
            processor.Inline = new AttributedInline(inner);
            slice.Start = closeParen + 1;
            return true;
        }

        private static (bool? Dim, string? Color) Styles(IReadOnlyDictionary<string, object?> attributes)
        {
            bool? dim = null;
            string? color = null;
            if (attributes.TryGetValue("dim", out var rawDim))
                dim = rawDim as bool? ?? throw new SessionInfoParseException(SessionInfoParseError.InvalidMarkdown);
            if (attributes.TryGetValue("color", out var rawColor))
                color = rawColor as string ?? throw new SessionInfoParseException(SessionInfoParseError.InvalidMarkdown);
            return (dim, color);
        }

        private static int? CloseBracket(string text, int index, int end)
        {
            var depth = 1;
            while (index <= end)
            {
                switch (text[index])
                {
                    case '\\':
                        index += 2;
                        continue;
                    case '`':
                        index = PastCodeSpan(text, index, end);
                        continue;
                    case '[':
                        depth++;
                        break;
                    case ']':
                        if (--depth == 0) return index;
                        break;
                }
                index++;
            }
            return null;
        }

        /// <summary>The index past a code span starting at <paramref name="index"/>, or past its opening
        /// backticks when no run of the same length closes it.</summary>
        private static int PastCodeSpan(string text, int index, int end)
        {
            var open = RunLength(text, index, end);
            var scan = index + open;
            while (scan <= end)
            {
                if (text[scan] != '`')
                {
                    scan++;
                    continue;
                }
                var run = RunLength(text, scan, end);
                if (run == open) return scan + run;
                scan += run;
            }
            return index + open;
        }

        private static int RunLength(string text, int index, int end)
        {
            var length = 0;
            while (index + length <= end && text[index + length] == '`') length++;
            return length;
        }

        private static int? CloseParen(string text, int index, int end)
        {
            char? quote = null;
            var depth = 0;
            for (; index <= end; index++)
            {
                var c = text[index];
                if (quote is { } open)
                {
                    if (c == '\\') index++;
                    else if (c == open) quote = null;
                    continue;
                }
                switch (c)
                {
                    case '"' or '\'':
                        quote = c;
                        break;
                    case '(' or '{' or '[':
                        depth++;
                        break;
                    case ']' or '}':
                        depth--;
                        break;
                    case ')':
                        if (depth == 0) return index;
                        depth--;
                        break;
                }
            }
            return null;
        }
    }

    /// <summary>The members of a JSON5 object written without its braces, as Foundation reads an attribute
    /// block. Strings come back as string, true and false as bool, numbers as double, null as DBNull, and
    /// nested objects and arrays as object. Null when the text is not valid JSON5.</summary>
    internal ref struct Json5Members
    {
        private readonly ReadOnlySpan<char> _text;
        private int _index;

        private Json5Members(ReadOnlySpan<char> text)
        {
            _text = text;
            _index = 0;
        }

        public static IReadOnlyDictionary<string, object?>? Parse(ReadOnlySpan<char> text)
        {
            var reader = new Json5Members(text);
            var members = new Dictionary<string, object?>(StringComparer.Ordinal);
            return reader.Members(members, closing: null) && reader.AtEnd() ? members : null;
        }

        private bool AtEnd()
        {
            SkipTrivia();
            return _index == _text.Length;
        }

        private bool Members(Dictionary<string, object?>? into, char? closing)
        {
            while (true)
            {
                SkipTrivia();
                if (_index == _text.Length) return closing is null;
                if (_text[_index] == closing)
                {
                    _index++;
                    return true;
                }
                if (Key() is not { } key) return false;
                SkipTrivia();
                if (!Take(':')) return false;
                if (!Value(out var value)) return false;
                if (into is not null) into[key] = value;
                SkipTrivia();
                if (Take(',')) continue;
                if (_index == _text.Length) return closing is null;
                if (_text[_index] != closing) return false;
                _index++;
                return true;
            }
        }

        private bool Elements()
        {
            while (true)
            {
                SkipTrivia();
                if (Take(']')) return true;
                if (!Value(out _)) return false;
                SkipTrivia();
                if (Take(',')) continue;
                return Take(']');
            }
        }

        private string? Key()
        {
            if (_index < _text.Length && _text[_index] is '"' or '\'') return QuotedString();
            var start = _index;
            while (_index < _text.Length && (char.IsAsciiLetterOrDigit(_text[_index]) || _text[_index] is '_' or '$'))
            {
                if (_index == start && char.IsAsciiDigit(_text[_index])) return null;
                _index++;
            }
            return _index > start ? _text[start.._index].ToString() : null;
        }

        private bool Value(out object? value)
        {
            value = null;
            SkipTrivia();
            if (_index == _text.Length) return false;
            var c = _text[_index];
            switch (c)
            {
                case '"' or '\'':
                    value = QuotedString();
                    return value is not null;
                case '{':
                    _index++;
                    value = new object();
                    return Members(null, '}');
                case '[':
                    _index++;
                    value = new object();
                    return Elements();
            }
            if (Word("true")) { value = true; return true; }
            if (Word("false")) { value = false; return true; }
            if (Word("null")) { value = DBNull.Value; return true; }
            if (Number() is { } number) { value = number; return true; }
            return false;
        }

        private bool Word(string word)
        {
            if (!_text[_index..].StartsWith(word, StringComparison.Ordinal)) return false;
            var after = _index + word.Length;
            if (after < _text.Length && (char.IsAsciiLetterOrDigit(_text[after]) || _text[after] is '_' or '$')) return false;
            _index = after;
            return true;
        }

        private double? Number()
        {
            var start = _index;
            var sign = 1.0;
            if (_index < _text.Length && _text[_index] is '+' or '-')
            {
                if (_text[_index] == '-') sign = -1;
                _index++;
            }
            if (Word("Infinity")) return sign * double.PositiveInfinity;
            if (Word("NaN")) return double.NaN;
            if (_index + 1 < _text.Length && _text[_index] == '0' && _text[_index + 1] is 'x' or 'X')
            {
                _index += 2;
                var hexStart = _index;
                while (_index < _text.Length && char.IsAsciiHexDigit(_text[_index])) _index++;
                if (_index == hexStart) return Fail(start);
                return sign * long.Parse(_text[hexStart.._index], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            var digitsStart = _index;
            var integerDigits = Digits();
            var fractionDigits = 0;
            if (_index < _text.Length && _text[_index] == '.')
            {
                _index++;
                fractionDigits = Digits();
            }
            if (integerDigits + fractionDigits == 0) return Fail(start);
            if (_index < _text.Length && _text[_index] is 'e' or 'E')
            {
                _index++;
                if (_index < _text.Length && _text[_index] is '+' or '-') _index++;
                if (Digits() == 0) return Fail(start);
            }
            var literal = _text[digitsStart.._index].ToString();
            if (literal.EndsWith('.')) literal += "0";
            if (literal.StartsWith('.')) literal = "0" + literal;
            return sign * double.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private double? Fail(int start)
        {
            _index = start;
            return null;
        }

        private int Digits()
        {
            var start = _index;
            while (_index < _text.Length && char.IsAsciiDigit(_text[_index])) _index++;
            return _index - start;
        }

        private string? QuotedString()
        {
            var quote = _text[_index++];
            var builder = new StringBuilder();
            while (_index < _text.Length)
            {
                var c = _text[_index++];
                if (c == quote) return builder.ToString();
                if (c is '\n' or '\r') return null;
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }
                if (_index == _text.Length) return null;
                var escaped = _text[_index++];
                switch (escaped)
                {
                    case 'n': builder.Append('\n'); break;
                    case 't': builder.Append('\t'); break;
                    case 'r': builder.Append('\r'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'v': builder.Append('\v'); break;
                    case '0': builder.Append('\0'); break;
                    case 'u':
                        if (_index + 4 > _text.Length
                            || !ushort.TryParse(_text.Slice(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unit))
                            return null;
                        builder.Append((char)unit);
                        _index += 4;
                        break;
                    case '\n' or '\r': break;
                    default: builder.Append(escaped); break;
                }
            }
            return null;
        }

        private bool Take(char c)
        {
            if (_index >= _text.Length || _text[_index] != c) return false;
            _index++;
            return true;
        }

        private void SkipTrivia()
        {
            while (_index < _text.Length)
            {
                var c = _text[_index];
                if (char.IsWhiteSpace(c))
                {
                    _index++;
                    continue;
                }
                if (c == '/' && _index + 1 < _text.Length && _text[_index + 1] == '/')
                {
                    while (_index < _text.Length && _text[_index] is not ('\n' or '\r')) _index++;
                    continue;
                }
                if (c == '/' && _index + 1 < _text.Length && _text[_index + 1] == '*')
                {
                    var close = _text[(_index + 2)..].IndexOf("*/", StringComparison.Ordinal);
                    if (close < 0) return;
                    _index += close + 4;
                    continue;
                }
                return;
            }
        }
    }
}
