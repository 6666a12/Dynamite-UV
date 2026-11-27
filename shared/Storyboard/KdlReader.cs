using System.Globalization;
using System.Text;

namespace DynamiteUniverse.Shared.Storyboard;

internal enum KdlKind { String, Integer, Decimal, Boolean }
internal readonly record struct KdlValue(KdlKind Kind, string Text, SourcePoint Point);
internal readonly record struct KdlParameter(KdlValue Value, SourcePoint Point);
internal sealed record KdlNode(string Name, SourcePoint Point, List<KdlValue> Arguments,
    Dictionary<string, KdlParameter> Parameters, List<KdlNode>? Children);

/// <summary>Scanner for the storyboard KDL subset. No general KDL features are expanded or evaluated.</summary>
internal sealed class KdlReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _source;
    private int _index;
    private int _line = 1;
    private int _column = 1;
    private int _nodes;
    private string? _objectId;

    private KdlReader(string source) => _source = source;

    internal static List<KdlNode> Read(string source)
    {
        try
        {
            if (source.Length > StoryboardLimits.MaxSourceBytes ||
                StrictUtf8.GetByteCount(source) > StoryboardLimits.MaxSourceBytes)
                throw new StoryboardError("UVSB002", new(1, 1), null, "Script exceeds the UTF-8 byte limit.");
        }
        catch (EncoderFallbackException)
        {
            throw new StoryboardError("UVSB001", new(1, 1), null, "Script contains invalid Unicode.");
        }
        var reader = new KdlReader(source);
        if (reader.Peek() == '\uFEFF') reader._index++;
        return reader.ReadNodes(1, inBlock: false);
    }

    private List<KdlNode> ReadNodes(int depth, bool inBlock)
    {
        if (depth > StoryboardLimits.MaxDepth)
            throw Error(Point, "Node nesting limit exceeded.", "UVSB002");
        var nodes = new List<KdlNode>();
        while (true)
        {
            SkipHorizontal();
            SkipComment();
            if (End || Peek() == '}')
            {
                if (inBlock)
                {
                    if (End) throw Error(Point, "Expected closing '}'.");
                    Take();
                }
                else if (!End) throw Error(Point, "Unexpected '}'.");
                return nodes;
            }
            if (IsNewline(Peek()))
            {
                TakeNewline();
                continue;
            }
            if (++_nodes > StoryboardLimits.MaxNodes)
                throw Error(Point, "Node count limit exceeded.", "UVSB002");
            nodes.Add(ReadNode(depth));
            SkipHorizontal();
            SkipComment();
            if (Peek() == ';') Take();
            else if (IsNewline(Peek())) TakeNewline();
            else if (!End && Peek() != '}') throw Error(Point, "Expected newline or ';' between nodes.");
        }
    }

    private KdlNode ReadNode(int depth)
    {
        var point = Point;
        var name = ReadName();
        var previousObject = _objectId;
        var arguments = new List<KdlValue>();
        var parameters = new Dictionary<string, KdlParameter>(StringComparer.Ordinal);
        List<KdlNode>? children = null;
        while (true)
        {
            var separated = SkipHorizontal();
            if (Peek() == '{')
            {
                Take();
                children = ReadNodes(depth + 1, inBlock: true);
                break;
            }
            if (End || IsNewline(Peek()) || Peek() is '}' or ';' || IsComment) break;
            if (!separated) throw Error(Point, "Expected a space between node entries.");
            if (arguments.Count + parameters.Count >= StoryboardLimits.MaxEntriesPerNode)
                throw Error(Point, "Node entry limit exceeded.", "UVSB002");
            if (IsNameStart(Peek()) || Peek() == '"')
            {
                var entryPoint = Point;
                var quoted = Peek() == '"';
                var text = quoted ? ReadString() : ReadName();
                // Look through optional spaces for '=' without consuming the next entry's separator.
                var next = _index;
                while (next < _source.Length && _source[next] is ' ' or '\t') next++;
                if (next < _source.Length && _source[next] == '=')
                {
                    if (parameters.ContainsKey(text))
                        throw Error(entryPoint, $"Duplicate named parameter '{text}'.", "UVSB003");
                    SkipHorizontal();
                    Take();
                    SkipHorizontal();
                    parameters.Add(text, new(ReadValue(), entryPoint));
                }
                else if (quoted)
                {
                    if (arguments.Count == 0 && name is "asset" or "region" or "group" or "sprite" or "shape" or "clip")
                        _objectId = text;
                    arguments.Add(new(KdlKind.String, text, entryPoint));
                }
                else throw Error(entryPoint, "Expected a named parameter or a quoted string value.");
            }
            else arguments.Add(ReadValue());
        }
        var node = new KdlNode(name, point, arguments, parameters, children);
        _objectId = previousObject;
        return node;
    }

    private KdlValue ReadValue()
    {
        var point = Point;
        if (Peek() == '"') return new(KdlKind.String, ReadString(), point);
        var start = _index;
        if (Peek() == '#')
        {
            Take();
            while (IsNamePart(Peek())) TakeBounded(start, point);
            var keyword = _source[start.._index];
            if (keyword is not ("#true" or "#false") || !IsValueEnd())
                throw Error(point, "Only #true and #false are supported KDL keywords.");
            return new(KdlKind.Boolean, keyword, point);
        }
        if (Peek() is '+' or '-') Take();
        if (!IsDigit(Peek())) throw Error(point, "Expected a quoted string, decimal number or boolean.");
        while (IsDigit(Peek())) TakeBounded(start, point);
        var kind = KdlKind.Integer;
        if (Peek() == '.')
        {
            kind = KdlKind.Decimal;
            TakeBounded(start, point);
            if (!IsDigit(Peek())) throw Error(point, "Expected digits after the decimal point.");
            while (IsDigit(Peek())) TakeBounded(start, point);
        }
        if (!IsValueEnd()) throw Error(point, "Unsupported decimal syntax or missing value separator.");
        return new(kind, _source[start.._index], point);
    }

    private bool IsValueEnd() => End || Peek() is ' ' or '\t' or '{' or '}' or ';' ||
        IsNewline(Peek()) || IsComment;

    private string ReadName()
    {
        var point = Point;
        if (!IsNameStart(Peek())) throw Error(point, "Expected a simple KDL node or parameter name.");
        var start = _index;
        while (IsNamePart(Peek())) TakeBounded(start, point);
        return _source[start.._index];
    }

    private string ReadString()
    {
        var point = Point;
        var start = _index;
        Take();
        var contentStart = _index;
        StringBuilder? decoded = null;
        while (!End)
        {
            var current = Point;
            var c = TakeBounded(start, point);
            if (c == '"') return decoded?.ToString() ?? _source[contentStart..(_index - 1)];
            if (IsDisallowedRaw(c))
                throw Error(current, "Raw control characters and line breaks are not allowed in strings.");
            if (c != '\\')
            {
                decoded?.Append(c);
                continue;
            }
            decoded ??= new StringBuilder().Append(_source.AsSpan(contentStart, _index - 1 - contentStart));
            if (End) break;
            var escape = TakeBounded(start, point);
            if (escape == 'u')
            {
                if (Peek() != '{') throw Error(current, "Expected a KDL Unicode escape \\u{...}.");
                TakeBounded(start, point);
                var digits = _index;
                while (IsHex(Peek()) && _index - digits < 6) TakeBounded(start, point);
                if (digits == _index || Peek() != '}' ||
                    !int.TryParse(_source.AsSpan(digits, _index - digits), NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out var codePoint) || !Rune.IsValid(codePoint))
                    throw Error(current, "Invalid KDL Unicode escape.");
                TakeBounded(start, point);
                decoded.Append(char.ConvertFromUtf32(codePoint));
            }
            else
                decoded.Append(escape switch
                {
                    '"' => '"', '\\' => '\\', 'b' => '\b', 'f' => '\f', 'n' => '\n',
                    'r' => '\r', 't' => '\t', 's' => ' ',
                    _ => throw Error(current, "Unsupported KDL string escape.")
                });
        }
        throw Error(point, "Unterminated quoted string.");
    }

    private bool SkipHorizontal()
    {
        var start = _index;
        while (Peek() is ' ' or '\t') Take();
        return _index != start;
    }

    private bool IsComment => Peek() == '/' && _index + 1 < _source.Length && _source[_index + 1] == '/';
    private void SkipComment()
    {
        if (!IsComment) return;
        while (!End && !IsNewline(Peek()))
        {
            if (Peek() != '\t' && IsDisallowedRaw(Peek()))
                throw Error(Point, "Invalid raw character in comment.");
            Take();
        }
    }

    private static bool IsNewline(char c) => c is '\r' or '\n' or '\u0085' or '\u000B' or '\u000C'
        or '\u2028' or '\u2029';
    private static bool IsDisallowedRaw(char c) => char.IsControl(c) || IsNewline(c) ||
        c is '\u200E' or '\u200F' or >= '\u202A' and <= '\u202E'
            or >= '\u2066' and <= '\u2069' or '\uFEFF';
    private static bool IsDigit(char c) => c is >= '0' and <= '9';
    private static bool IsHex(char c) => IsDigit(c) || c is >= 'a' and <= 'f' or >= 'A' and <= 'F';
    private static bool IsNameStart(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
    private static bool IsNamePart(char c) => IsNameStart(c) || IsDigit(c) || c is '-' or '_' or '.';

    private bool End => _index == _source.Length;
    private SourcePoint Point => new(_line, _column);
    private char Peek() => End ? '\0' : _source[_index];
    private char Take()
    {
        var c = _source[_index++];
        _column++;
        return c;
    }

    private char TakeBounded(int start, SourcePoint point) => _index - start >= StoryboardLimits.MaxTokenLength
        ? throw Error(point, "Token length limit exceeded.", "UVSB002") : Take();

    private void TakeNewline()
    {
        if (Take() == '\r' && Peek() == '\n') Take();
        _line++;
        _column = 1;
    }

    private StoryboardError Error(SourcePoint point, string message, string code = "UVSB001") =>
        new(code, point, _objectId, message);
}
