namespace DynamiteUniverse.Editor.Shell;

internal static class StageLaunchVerification
{
    internal static void Run(string root, Action<string, bool> check)
    {
        var source = Path.Combine(root, "source", "editor");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "DynaMakerUv.Editor.csproj"), "");
        File.WriteAllText(Path.Combine(source, "project.godot"), "");
        var engine = Path.Combine(root, "dev-godot.exe");
        File.WriteAllText(engine, "");
        var dev = StageLaunch.Create(source, root, engine);
        check("Development stage keeps project path and engine override",
            dev.FileName == engine && dev.ArgumentList.SequenceEqual(new[] { "--path", source }));
        var package = Path.Combine(root, "中文 package");
        var shell = Path.Combine(package, "shell");
        var stage = Path.Combine(package, "stage");
        Directory.CreateDirectory(shell);
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(package, "distribution.json"), "{}");
        var executable = Path.Combine(stage, "DynaMakerStage.exe");
        File.WriteAllText(executable, "");
        File.WriteAllText(Path.ChangeExtension(executable, ".pck"), "");
        var installed = StageLaunch.Create(shell, source, engine);
        check("Installed stage ignores working directory and development override",
            installed.FileName == executable && installed.WorkingDirectory == stage && installed.ArgumentList.Count == 0);
        File.Delete(Path.ChangeExtension(executable, ".pck"));
        try
        {
            StageLaunch.Create(shell, source, engine);
            check("Broken installed package cannot silently fall back to source", false);
        }
        catch (FileNotFoundException)
        {
            check("Broken installed package cannot silently fall back to source", true);
        }
    }
}
