using System.Collections.Generic;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Game;

public partial class GameplayMain
{
	private readonly HashSet<(int HeadId, int PointerId, double Time)> _mixerEarlyPlacements = new();
	private readonly List<MixerInput> _mixerInputs = new();
	private readonly List<TouchSample> _mixerSeeds = new();
	private readonly List<double> _mixerTickTimes = new();
	private readonly HashSet<int> _mixerTrackPointers = new();

	private void UpdateMixerState(SustainRuntime state, double time)
	{
		var mixer = state.Mixer!;
		_mixerSeeds.Clear();
		_mixerTrackPointers.Clear();
		for (var i = 0; i < _pointerBuffer.InitialTouches.Count; i++)
		{
			var sample = _pointerBuffer.InitialTouches[i];
			if (sample.Track == state.Path.Track)
			{
				_mixerSeeds.Add(sample);
				_mixerTrackPointers.Add(sample.Id);
			}
		}
		_mixerInputs.Clear();
		for (var i = 0; i < _pointerBuffer.Inputs.Count; i++)
		{
			var input = _pointerBuffer.Inputs[i];
			if (input.Time > state.Path.EndTime) break;
			var projection = ProjectHoldTouch(input.Id, input.ScreenPosition);
			var sample = projection.On(state.Path.Track);
			var phase = input.Action == PointerAction.Press || !_mixerTrackPointers.Contains(input.Id)
				? ContactPhase.Began : ContactPhase.Moved;
			if (input.Action == PointerAction.Release || sample == null) _mixerTrackPointers.Remove(input.Id);
			else _mixerTrackPointers.Add(input.Id);
			_mixerInputs.Add(new MixerInput(input.Time, input.Id,
				input.Action == PointerAction.Release ? null : sample?.Position,
				phase,
				_mixerEarlyPlacements.Contains((state.Path.HeadId, input.Id, input.Time))));
		}
		_mixerTickTimes.Clear();
		var first = state.MixerTickCursor;
		var next = first;
		while (next < state.MixerTicks.Count && state.MixerTicks[next].Time <= time)
			_mixerTickTimes.Add(state.MixerTicks[next++].Time);
		mixer.AdvanceFrame(time, _mixerSeeds, _mixerInputs, _mixerTickTimes,
			(index, grade) => state.MixerGrades[state.MixerTicks[first + index]] = grade, _auto);
		state.MixerTickCursor = next;
		state.LastContactUpdateTime = mixer.CurrentTime!.Value;
		state.Holding = mixer.Catching;
		if (time < state.Path.StartTime && !mixer.Placed) return;
		var bounds = state.Path.BoundsAtRuntime(state.LastContactUpdateTime);
		_noteRenderer.UpdateMixerHeadView(state.Path, mixer.Placed, mixer.Position, bounds);
		_noteRenderer.UpdateSustainEffect(state.Path, mixer.Catching, bounds, mixer.Position);
	}
}
