using DynamiteUniverse.ChartEditor.Core;
using static EditorTestSupport;
using static EditorDifficultyWorkflowTests;

internal static class EditorMediaOwnershipTests
{
    internal static void Run()
    {
        WithExternalResources(CopiedChart);
        WithExternalResources(ExplicitPackSlot);
        foreach (var path in new[]{"first", "same", "as"})
            foreach (var remove in new[]{false,true})
                WithExternalResources((root,audio,cover)=>DiscardedImport(root,audio,cover,path,remove));
        WithExternalResources(StillUsed);
    }

    private static void CopiedChart(string root, string audio, string cover)
    {
        var session = new EditorDocumentSession(); var d = session.CreateDraft(BuildDraftRequest(audio,cover));
        AddTap(d,"note"); var oldAudio = DifferentWav(root,audio,"original.wav",42);
        var newAudio = DifferentWav(root,audio,"changed.wav",85);
        Apply(d,Settings(d) with {ChartAudioSource=oldAudio});
        var dir=Path.Combine(root,"pack"); session.Save(dir);
        d.Execute(new CopyChartCommand("hard","copy","charts/copy.json"));
        var shared = d.SelectedChart.Audio!;
        // An unrelated user file must not be mistaken for an unused chart-private slot.
        var user = Path.Combine(dir,"audio","copy.wav"); File.WriteAllText(user,"keep user file");
        Apply(d,Settings(d) with {ChartAudioSource=newAudio}); var detached = d.SelectedChart.Audio!;
        Check(!string.Equals(shared,detached,StringComparison.OrdinalIgnoreCase) && detached!="audio/copy.wav", "copy-on-write avoids original and unrelated user file");
        d.SelectChart("hard"); Check(File.ReadAllBytes(session.ResolveDigestAudio()!).SequenceEqual(File.ReadAllBytes(oldAudio)), "unmodified original previews original bytes");
        d.Undo(); Check(d.SelectedChartId=="hard" && d.Charts.All(chart=>chart.Id!="copy") && !d.PendingResourceReplacements.Any(),
            "undo removes the copied chart, not its later recording replacement");
        d.Redo(); Check(d.SelectedChartId=="copy" && d.SelectedChart.Audio==detached && session.ResolveDigestAudio()==newAudio,
            "redo of the copy preserves the latest independent recording");
        session.Save(dir); d=session.Open(dir); var copy = d.Charts.Single(c=>c.Id=="copy");
        Check(File.ReadAllBytes(Path.Combine(dir,d.SelectedChart.Audio!)).SequenceEqual(File.ReadAllBytes(oldAudio)) &&
              File.ReadAllBytes(Path.Combine(dir,copy.Audio!)).SequenceEqual(File.ReadAllBytes(newAudio)) &&
              File.ReadAllText(user)=="keep user file", "copy-on-write persists distinct recordings and preserves extra file bytes");
    }

    private static void ExplicitPackSlot(string root, string audio, string cover)
    {
        var s=new EditorDocumentSession();var d=s.CreateDraft(BuildDraftRequest(audio,cover));AddTap(d,"one");
        // Valid imported package: chart explicitly names the very same path as the package default.
        d.SelectedChart.Audio=d.Audio;var dir=Path.Combine(root,"pack");s.Save(dir);d=s.Open(dir);
        s.AddDifficulty(Prepare(s,Request("default",DifficultyAudioSource.PackageDefault)));AddTap(d,"two");d.SelectChart("hard");
        var newAudio=DifferentWav(root,audio,"new.wav",16);Apply(d,Settings(d) with {ChartAudioSource=newAudio});
        Check(d.SelectedChart.Audio!=d.Audio,"chart explicitly sharing package default detaches on chart-only replacement");
        s.Save(dir);d=s.Open(dir);d.SelectChart("default");
        Check(File.ReadAllBytes(s.ResolveDigestAudio()!).SequenceEqual(File.ReadAllBytes(audio)),"inheriting sibling still plays original package recording after round trip");
    }

    private static void DiscardedImport(string root,string audio,string cover,string path,bool remove)
    {
        var s=new EditorDocumentSession(); var d=s.CreateDraft(BuildDraftRequest(audio,cover)); AddTap(d,"base");
        var dir=Path.Combine(root,"pack"); if(path!="first")s.Save(dir);
        var newAudio=DifferentWav(root,audio,"discarded.wav",3);
        s.AddDifficulty(Prepare(s,Request("temporary",DifficultyAudioSource.External,newAudio)));var slot=d.SelectedChart.Audio!;
        if(remove)d.Execute(new DeleteChartCommand("temporary"));
        else {Apply(d,Settings(d) with {ClearChartAudio=true});AddTap(d,"temporary-note");}
        if(path!="first")File.WriteAllText(Path.Combine(dir,"my-notes.txt"),"keep");
        // Former source must not be read by any writer, even after it has been moved away.
        File.Move(newAudio,newAudio+".moved");var destination=path=="as"?Path.Combine(root,"save-as"):dir;
        s.Save(destination);d=s.Open(destination);
        Check(!File.Exists(Path.Combine(destination,slot)) && d.Charts.All(c=>c.Audio is null) &&
              d.Charts.Count==(remove?1:2) && (path=="first"||File.ReadAllText(Path.Combine(destination,"my-notes.txt"))=="keep"),
            $"{path} save after {(remove?"delete":"clear")} ignores abandoned import and preserves user extras");
    }

    private static void StillUsed(string root,string audio,string cover)
    {
        var s=new EditorDocumentSession(); var d=s.CreateDraft(BuildDraftRequest(audio,cover)); AddTap(d,"base");
        var alt=DifferentWav(root,audio,"shared.wav",90);
        s.AddDifficulty(Prepare(s,Request("explicit",DifficultyAudioSource.External,alt)));AddTap(d,"one");
        s.AddDifficulty(Prepare(s,Request("sibling")));AddTap(d,"two");
        d.Execute(new DeleteChartCommand("explicit"));var dir=Path.Combine(root,"pack");s.Save(dir);d=s.Open(dir);
        d.SelectChart("sibling");Check(File.ReadAllBytes(s.ResolveDigestAudio()!).SequenceEqual(File.ReadAllBytes(alt)),
            "deleting the original importer retains its still-referenced pending audio for the sibling");
    }
}
