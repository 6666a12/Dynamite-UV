using Godot;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;
using static DynamiteUniverse.Game.GameplayRenderGeometry;

namespace DynamiteUniverse.Game;

public sealed partial class GameplayNoteRenderer
{
    private readonly Dictionary<int, NoteView> _mixerHeadViews = new();

    public void ReleaseMixerHeadView(int headId)
    {
        if (_mixerHeadViews.Remove(headId, out var view)) ReturnNoteView(view);
        _effects.ReleaseSustainEffect(headId);
    }

    public void UpdateSustainEffect(SustainPath path, bool connected, NoteBounds bounds, double position) =>
        _effects.UpdateSustainEffect(path, connected, bounds, position);

    public void UpdateHoldHeadContactView(SustainPath path, bool connected,
        NoteBounds bounds)
    {
        if (!_viewByNoteId.TryGetValue(path.HeadId, out var view))
            return;
        if (connected)
            view.RestoreHoldContact();
        else
            view.BeginRecoverableHoldFallthrough();
        _geometry.UpdateSustainHeadGeometry(view, bounds, bounds.Center,
            view.IsRecoverableHoldFalling ? view.MissDistancePx : 0f);
    }

    public void UpdateMixerHeadView(SustainPath path, bool placed, double position, NoteBounds bounds)
    {
        // A placed receiver survives release and temporary loss until this path ends.
        RecycleNoteViewImmediately(path.HeadId);
        _mixerHeadViews.TryGetValue(path.HeadId, out var view);
        if (!placed)
        {
            if (view != null)
                view.Visible = false;
            return;
        }

        if (view == null)
        {
            view = NoteView.Create(path.Head,
                SizeFor(path.Head), NoteVisualSpec.Mixer, brightness: ViewBrightness);
            view.Name = $"MixerSlider_{path.HeadId}";
            view.ZIndex = 2;
            _noteRoot.AddChild(view);
            _mixerHeadViews[path.HeadId] = view;
        }

        view.Visible = true;
        view.SetContactLight();
        _geometry.UpdateSustainHeadGeometry(view, bounds, position);
    }

    public void FinishHoldViews(SustainPath path, bool failed)
    {
        _effects.ReleaseSustainEffect(path.HeadId);
        if (failed)
        {
            // 判定结束后仍保留下落所需的视图和连接段，透明度统一由退场层控制。
            var fadeRoot = new Node2D { Name = $"HoldMissFade_{path.HeadId}" };
            _noteRoot.AddChild(fadeRoot);
            var fading = new FadingHoldView { Root = fadeRoot };
            foreach (var node in path.Nodes)
            {
                // 未来尚未入场的节点和连接段也不能再生成。
                _failedHoldNoteIds.Add(node.Id);
                if (_viewByNoteId.Remove(node.Id, out var view))
                {
                    _active.Remove(view);
                    view.Reparent(fadeRoot);
                    fading.Notes.Add(view);
                }
            }
            for (var i = _links.Count - 1; i >= 0; i--)
            {
                var link = _links[i];
                if (!link.IsHold || !_failedHoldNoteIds.Contains(link.FromId))
                    continue;
                link.Poly.Reparent(fadeRoot);
                link.Frame?.Reparent(fadeRoot);
                fading.Links.Add(link);
                _links.RemoveAt(i);
            }
            if (fadeRoot.GetChildCount() > 0)
                _fadingHoldViews.Add(fading);
            else
                fadeRoot.QueueFree();
            return;
        }

        if (_viewByNoteId.TryGetValue(path.HeadId, out var headView))
        {
            var bounds = path.BoundsAt(path.EndTime);
            _geometry.UpdateSustainHeadGeometry(headView, bounds, bounds.Center);
            headView.RestoreHoldContact();
            headView.MarkJudged(NoteVisualSpec.Hold);
        }
    }
}
