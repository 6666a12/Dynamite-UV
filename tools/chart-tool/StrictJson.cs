using System.Text;
using System.Text.Json;

namespace ChartTool;

internal sealed class StrictJsonDocument : IDisposable
{
    private readonly JsonDocument _document;

    private StrictJsonDocument(JsonDocument document, bool hadBom)
    {
        _document = document;
        HadBom = hadBom;
    }

    public JsonElement Root => _document.RootElement;
    public bool HadBom { get; }

    public static StrictJsonDocument? Load(string path, string displayFile,
        DiagnosticBag diagnostics)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Error(displayFile, "", $"cannot read file: {ex.Message}");
            return null;
        }

        var hadBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        if (hadBom)
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            diagnostics.Error(displayFile, "", $"invalid UTF-8: {ex.Message}");
            return null;
        }

        var duplicates = new List<(string Pointer, string Name)>();
        try
        {
            FindDuplicateKeys(bytes, duplicates);
        }
        catch (JsonException ex)
        {
            diagnostics.Error(displayFile, "", $"invalid JSON: {Describe(ex)}");
            return null;
        }
        foreach (var (pointer, name) in duplicates)
        {
            diagnostics.Error(displayFile, JsonPointer.Property(pointer, name),
                $"duplicate property name '{name}'");
        }
        if (duplicates.Count > 0)
            return null;

        try
        {
            var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 256,
            });
            ValidateStrings(document.RootElement, displayFile, "", diagnostics);
            if (diagnostics.HasErrors)
            {
                document.Dispose();
                return null;
            }
            return new StrictJsonDocument(document, hadBom);
        }
        catch (JsonException ex)
        {
            diagnostics.Error(displayFile, "", $"invalid JSON: {Describe(ex)}");
            return null;
        }
    }

    public void Dispose() => _document.Dispose();

    private static void FindDuplicateKeys(ReadOnlySpan<byte> bytes,
        List<(string Pointer, string Name)> duplicates)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 256,
        });
        var stack = new Stack<ScanFrame>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                {
                    var pointer = ValuePointer(stack);
                    stack.Push(new ScanFrame(FrameKind.Object, pointer));
                    break;
                }
                case JsonTokenType.StartArray:
                {
                    var pointer = ValuePointer(stack);
                    stack.Push(new ScanFrame(FrameKind.Array, pointer));
                    break;
                }
                case JsonTokenType.PropertyName:
                {
                    var frame = stack.Peek();
                    var name = reader.GetString() ?? string.Empty;
                    if (!frame.Properties!.Add(name))
                        duplicates.Add((frame.Pointer, name));
                    frame.PendingProperty = name;
                    break;
                }
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    stack.Pop();
                    CompleteValue(stack);
                    break;
                case JsonTokenType.String:
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    CompleteValue(stack);
                    break;
            }
        }
    }

    private static string ValuePointer(Stack<ScanFrame> stack)
    {
        if (stack.Count == 0)
            return string.Empty;
        var parent = stack.Peek();
        return parent.Kind == FrameKind.Object
            ? JsonPointer.Property(parent.Pointer, parent.PendingProperty ?? string.Empty)
            : JsonPointer.Index(parent.Pointer, parent.ArrayIndex);
    }

    private static void CompleteValue(Stack<ScanFrame> stack)
    {
        if (stack.Count == 0)
            return;
        var parent = stack.Peek();
        if (parent.Kind == FrameKind.Object)
            parent.PendingProperty = null;
        else
            parent.ArrayIndex++;
    }

    private static void ValidateStrings(JsonElement element, string file, string pointer,
        DiagnosticBag diagnostics)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                ValidateUtf16(element.GetString() ?? string.Empty, file, pointer, diagnostics);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ValidateUtf16(property.Name, file,
                        JsonPointer.Property(pointer, property.Name), diagnostics);
                    ValidateStrings(property.Value, file,
                        JsonPointer.Property(pointer, property.Name), diagnostics);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    ValidateStrings(item, file, JsonPointer.Index(pointer, index++), diagnostics);
                break;
        }
    }

    private static void ValidateUtf16(string value, string file, string pointer,
        DiagnosticBag diagnostics)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i]))
                continue;
            if (!char.IsHighSurrogate(value[i]) || i + 1 >= value.Length ||
                !char.IsLowSurrogate(value[i + 1]))
            {
                diagnostics.Error(file, pointer, "string contains an unpaired UTF-16 surrogate");
                return;
            }
            i++;
        }
    }

    private static string Describe(JsonException ex) =>
        ex.LineNumber is null
            ? ex.Message
            : $"line {ex.LineNumber.Value + 1}, byte {ex.BytePositionInLine}: {ex.Message}";

    private enum FrameKind
    {
        Object,
        Array,
    }

    private sealed class ScanFrame(FrameKind kind, string pointer)
    {
        public FrameKind Kind { get; } = kind;
        public string Pointer { get; } = pointer;
        public HashSet<string>? Properties { get; } = kind == FrameKind.Object
            ? new HashSet<string>(StringComparer.Ordinal)
            : null;
        public string? PendingProperty { get; set; }
        public int ArrayIndex { get; set; }
    }
}

internal sealed class StrictObject
{
    private readonly JsonElement _element;
    private readonly string _file;
    private readonly string _pointer;
    private readonly DiagnosticBag _diagnostics;

    public StrictObject(JsonElement element, string file, string pointer,
        DiagnosticBag diagnostics)
    {
        _element = element;
        _file = file;
        _pointer = pointer;
        _diagnostics = diagnostics;
    }

    public bool EnsureObject()
    {
        if (_element.ValueKind == JsonValueKind.Object)
            return true;
        _diagnostics.Error(_file, _pointer, "expected JSON object");
        return false;
    }

    public void RejectUnknown(params string[] allowed)
    {
        if (_element.ValueKind != JsonValueKind.Object)
            return;
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in _element.EnumerateObject())
        {
            if (!set.Contains(property.Name))
            {
                _diagnostics.Error(_file, JsonPointer.Property(_pointer, property.Name),
                    $"unknown property '{property.Name}'");
            }
        }
    }

    public bool TryGet(string name, out JsonElement value) =>
        _element.TryGetProperty(name, out value);

    public string? RequiredString(string name)
    {
        var pointer = JsonPointer.Property(_pointer, name);
        if (!_element.TryGetProperty(name, out var value))
        {
            _diagnostics.Error(_file, pointer, "required property is missing");
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            _diagnostics.Error(_file, pointer, "expected string");
            return null;
        }
        return value.GetString() ?? string.Empty;
    }

    public string? OptionalString(string name)
    {
        if (!_element.TryGetProperty(name, out var value))
            return null;
        var pointer = JsonPointer.Property(_pointer, name);
        if (value.ValueKind != JsonValueKind.String)
        {
            _diagnostics.Error(_file, pointer, "expected string");
            return null;
        }
        return value.GetString() ?? string.Empty;
    }

    public int? RequiredInt32(string name)
    {
        var pointer = JsonPointer.Property(_pointer, name);
        if (!_element.TryGetProperty(name, out var value))
        {
            _diagnostics.Error(_file, pointer, "required property is missing");
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            _diagnostics.Error(_file, pointer, "expected 32-bit integer");
            return null;
        }
        return result;
    }

    public int? OptionalInt32(string name)
    {
        if (!_element.TryGetProperty(name, out var value))
            return null;
        var pointer = JsonPointer.Property(_pointer, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            _diagnostics.Error(_file, pointer, "expected 32-bit integer");
            return null;
        }
        return result;
    }

    public double? RequiredDouble(string name)
    {
        var pointer = JsonPointer.Property(_pointer, name);
        if (!_element.TryGetProperty(name, out var value))
        {
            _diagnostics.Error(_file, pointer, "required property is missing");
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
        {
            _diagnostics.Error(_file, pointer, "expected finite IEEE 754 binary64 number");
            return null;
        }
        return result;
    }

    public JsonElement? RequiredArray(string name)
    {
        var pointer = JsonPointer.Property(_pointer, name);
        if (!_element.TryGetProperty(name, out var value))
        {
            _diagnostics.Error(_file, pointer, "required property is missing");
            return null;
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            _diagnostics.Error(_file, pointer, "expected array");
            return null;
        }
        return value;
    }

    public JsonElement? OptionalArray(string name)
    {
        if (!_element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.Array)
        {
            _diagnostics.Error(_file, JsonPointer.Property(_pointer, name), "expected array");
            return null;
        }
        return value;
    }

    public JsonElement? OptionalObject(string name)
    {
        if (!_element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            _diagnostics.Error(_file, JsonPointer.Property(_pointer, name), "expected object");
            return null;
        }
        return value;
    }
}
