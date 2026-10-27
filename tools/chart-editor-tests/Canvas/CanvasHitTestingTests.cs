using DynamiteUniverse.ChartEditor.Core;
using static EditorTestSupport;

internal static class CanvasHitTestingTests
{
    private readonly record struct Note(int Side, CanvasHitRect Head, CanvasHitRect Tail,
        CanvasHitRect Node, bool HasNode, bool HasTail, bool HasBody);

    private readonly struct Geometry : ICanvasHitGeometry
    {
        private readonly Note[] _notes;
        public Geometry(params Note[] notes) => _notes = notes;
        public int NoteCount => _notes.Length;
        public int SideAt(int noteIndex) => _notes[noteIndex].Side;
        public int NodeCount(int noteIndex) => _notes[noteIndex].HasNode ? 1 : 0;
        public CanvasHitRect NodeRect(int noteIndex, int nodeIndex) => _notes[noteIndex].Node;
        public CanvasHitRect HeadRect(int noteIndex) => _notes[noteIndex].Head;
        public CanvasHitRect TailRect(int noteIndex) => _notes[noteIndex].Tail;
        public bool HasTail(int noteIndex) => _notes[noteIndex].HasTail;
        public bool HasPathBody(int noteIndex) => _notes[noteIndex].HasBody;
    }

    internal static void Run()
    {
        var overlapping = new Geometry(
            new Note(0, new(10, 10, 20, 10), default, default, false, false, false),
            new Note(0, new(10, 10, 20, 10), default, default, false, false, false));
        Check(CanvasHitTesting.Hit(in overlapping, 0, 15, 15).NoteIndex == 1,
            "later notes obscure earlier notes");

        var priority = new Geometry(new Note(0, new(0, 0, 20, 20), new(0, 0, 20, 20),
            new(0, 0, 20, 20), true, true, true));
        var node = CanvasHitTesting.Hit(in priority, 0, 10, 10);
        Check(node.NodeIndex == 0, "path node precedes head, tail, and body");
        var head = new Geometry(new Note(0, new(0, 0, 20, 20), new(0, 0, 20, 20), default,
            false, true, true));
        Check(CanvasHitTesting.Hit(in head, 0, 10, 10).NoteIndex == 0,
            "head precedes tail and path body");
        var tail = new Geometry(new Note(0, default, new(0, 0, 20, 20), default,
            false, true, true));
        Check(CanvasHitTesting.Hit(in tail, 0, 10, 10).NoteIndex == 0,
            "tail precedes path body");
        var body = new Geometry(new Note(0, default, default, default, false, false, true));
        Check(CanvasHitTesting.Hit(in body, 0, 10, 10).NoteIndex == 0,
            "path body is the final candidate");

        var edgeRect = new Geometry(new Note(0, new(20, 20, 20, 20), default, default,
            false, false, false));
        var leading = CanvasHitTesting.Hit(in edgeRect, 0, 20, 30);
        var trailing = CanvasHitTesting.Hit(in edgeRect, 0, 40, 30);
        Check(leading.Resize && leading.ResizeLeading && !trailing.ResizeLeading && trailing.Resize,
            "head leading and trailing edges set width flags in the right order");
        var nodeEdgeRect = new Geometry(new Note(0, default, default, new(20, 20, 20, 20),
            true, false, false));
        Check(CanvasHitTesting.Hit(in nodeEdgeRect, 0, 40, 30).ResizeNodeWidth,
            "path node trailing edge sets node-width flag");

        var tracks = new Geometry(new Note(1, new(0, 0, 20, 20), default, default, false, false, false),
            new Note(2, new(0, 0, 20, 20), default, default, false, false, false));
        Check(CanvasHitTesting.Hit(in tracks, 0, 10, 10) == CanvasHit.None &&
              CanvasHitTesting.Hit(in tracks, 3, 10, 10) == CanvasHit.None,
            "other tracks and event side produce no hit");
        var empty = new Geometry([]);
        Check(CanvasHitTesting.Hit(in empty, 0, 10, 10) == CanvasHit.None,
            "empty canvas produces no hit");
    }
}
