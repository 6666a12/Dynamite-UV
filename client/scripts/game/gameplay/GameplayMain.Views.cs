using Godot;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;
using static DynamiteUniverse.Game.GameplayRenderGeometry;

namespace DynamiteUniverse.Game;

public partial class GameplayMain
{
    private GameplayNoteRenderer _noteRenderer = null!;

    private void ResolveNoteView(JudgeUnit unit, JudgeGrade grade,
        bool emitEffect = true, HitTiming timing = HitTiming.Exact) =>
        _noteRenderer.ResolveNoteView(unit.NoteId, unit.Category, grade, emitEffect, timing);

	private static string GradeText(JudgeResult r) => GradeText(r.Grade, r.Timing);

	private static string GradeText(JudgeGrade grade, HitTiming timing) => grade switch
	{
		JudgeGrade.Perfect => "PERFECT",
		JudgeGrade.Great => $"GREAT {(timing == HitTiming.Early ? "E" : "L")}",
		JudgeGrade.Good => $"GOOD {(timing == HitTiming.Early ? "E" : "L")}",
		_ => "MISS",
	};
}
