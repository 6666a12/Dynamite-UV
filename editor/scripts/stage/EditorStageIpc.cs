using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Godot;

namespace DynamiteUniverse.Editor;

public sealed class EditorStageIpc : IDisposable
{
    private const string EventPrefix = "DYNA_STAGE ";
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly object _outputLock = new();
    private StreamReader? _reader;
    private Thread? _readerThread;
    private volatile bool _stopping;

    public bool Ended { get; private set; }

    public void Start()
    {
        _reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "DynaEditorStdin" };
        _readerThread.Start();
    }

    public void Drain(Action<string, JsonElement> handle)
    {
        while (_pending.TryDequeue(out var line))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("command", out var command) || command.ValueKind != JsonValueKind.String)
                {
                    SendError("missing string command");
                    continue;
                }
                handle(command.GetString()!, root);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SendError(ex.Message);
            }
        }
    }

    public void SendReady(long hwnd, Vector2I size) => Emit("ready", writer =>
    {
        writer.WriteNumber("hwnd", hwnd);
        writer.WriteNumber("width", size.X);
        writer.WriteNumber("height", size.Y);
    });

    public void SendTick(double second, double bar, bool playing, double playerSpeed, double playbackRate) =>
        Emit("tick", writer =>
        {
            writer.WriteNumber("second", second);
            writer.WriteNumber("bar", bar);
            writer.WriteBoolean("playing", playing);
            writer.WriteNumber("playerSpeed", playerSpeed);
            writer.WriteNumber("playbackRate", playbackRate);
        });

    public void SendSelection(IReadOnlyList<string> ids, string primary, string parentId,
        IReadOnlyList<string> noteIds, string? nodeId = null) =>
        Emit("selection", writer =>
        {
            writer.WriteStartArray("ids");
            foreach (var id in ids) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteStartArray("noteIds");
            foreach (var id in noteIds) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteString("primary", primary);
            writer.WriteString("parentId", parentId);
            if (nodeId is not null) writer.WriteString("nodeId", nodeId);
        });

    public void SendPlacement(StagePlacementRequest request) => Emit("place", writer =>
    {
        writer.WriteNumber("type", request.Type);
        writer.WriteNumber("track", request.Track);
        writer.WriteNumber("bar", request.Bar);
        writer.WriteNumber("center", request.Center);
        writer.WriteNumber("width", request.Width);
        writer.WriteNumber("tailBar", request.TailBar);
        writer.WriteBoolean("snap", request.Snap);
    });

    public void SendPathAdd(StagePathAddRequest request) => Emit("pathAdd", writer =>
    {
        writer.WriteString("noteId", request.NoteId);
        writer.WriteNumber("bar", request.Bar);
    });

    public void SendPathMove(StagePathMoveRequest request) => Emit("pathMove", writer =>
    {
        writer.WriteString("noteId", request.NoteId);
        if (request.NodeId is not null) writer.WriteString("nodeId", request.NodeId);
        writer.WriteNumber("startBar", request.StartBar);
        writer.WriteNumber("endBar", request.EndBar);
        writer.WriteNumber("startCenter", request.StartCenter);
        writer.WriteNumber("endCenter", request.EndCenter);
        writer.WriteNumber("positionStep", request.PositionStep);
        writer.WriteBoolean("snap", request.Snap);
    });

    public void SendNoteMove(StageNoteMoveRequest request) => Emit("noteMove", writer =>
    {
        writer.WriteString("noteId", request.NoteId);
        writer.WriteNumber("startBar", request.StartBar);
        writer.WriteNumber("endBar", request.EndBar);
        writer.WriteNumber("startCenter", request.StartCenter);
        writer.WriteNumber("endCenter", request.EndCenter);
        writer.WriteNumber("positionStep", request.PositionStep);
        writer.WriteBoolean("snap", request.Snap);
    });

    public void SendNoteResize(StageNoteResizeRequest request) => Emit("noteResize", writer =>
    {
        writer.WriteString("noteId", request.NoteId);
        writer.WriteBoolean("moveStartEdge", request.MoveStartEdge);
        writer.WriteNumber("edge", request.Edge);
        writer.WriteNumber("widthStep", request.WidthStep);
    });

    public void SendKey(Key key, bool ctrl, bool shift, bool alt) => Emit("key", writer =>
    {
        writer.WriteString("key", key.ToString());
        writer.WriteBoolean("ctrl", ctrl);
        writer.WriteBoolean("shift", shift);
        writer.WriteBoolean("alt", alt);
    });

    public void SendAudioSnapshot(long requestId, double second, double audioSecond,
        bool playing, bool paused, bool panSuspended, int loads, int restarts) => Emit("audioSnapshot", writer =>
        {
            writer.WriteNumber("requestId", requestId);
            writer.WriteNumber("second", second);
            writer.WriteNumber("audioSecond", audioSecond);
            writer.WriteBoolean("audioPlaying", playing);
            writer.WriteBoolean("audioPaused", paused);
            writer.WriteBoolean("panSuspended", panSuspended);
            writer.WriteNumber("audioLoads", loads);
            writer.WriteNumber("audioRestarts", restarts);
        });

    public void SendContextAction(string kind, string value) => Emit("contextAction", writer =>
    {
        writer.WriteString("kind", kind);
        writer.WriteString("value", value);
    });

    public void SendInput(string detail) => Emit("input", writer => writer.WriteString("detail", detail));
    public void SendError(string message) => Emit("error", writer => writer.WriteString("message", message));

    public void Dispose()
    {
        _stopping = true;
        try { _reader?.Dispose(); }
        catch (Exception) { }
        _reader = null;
        _readerThread = null;
    }

    private void ReadLoop()
    {
        try
        {
            while (!_stopping)
            {
                var line = _reader?.ReadLine();
                if (line is null) break;
                _pending.Enqueue(line);
            }
        }
        catch (Exception) { }
        Ended = !_stopping;
    }

    private void Emit(string name, Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("event", name);
            body(writer);
            writer.WriteEndObject();
        }
        var line = EventPrefix + Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
        lock (_outputLock)
        {
            Console.Out.Write(line);
            Console.Out.Flush();
        }
    }
}
