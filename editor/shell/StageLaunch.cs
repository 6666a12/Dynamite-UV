using System.Diagnostics;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Resolves installed stage relative to Shell; source discovery is development-only.</summary>
internal static class StageLaunch
{
    internal static ProcessStartInfo Create(string baseDirectory, string currentDirectory, string? developmentEngine)
    {
        var installed = Path.GetFullPath(Path.Combine(baseDirectory, "../stage/DynaMakerStage.exe"));
        var marker = Path.GetFullPath(Path.Combine(baseDirectory, "../distribution.json"));
        if (File.Exists(marker))
        {
            if (!File.Exists(installed) || !File.Exists(Path.ChangeExtension(installed, ".pck")))
                throw new FileNotFoundException("The packaged Godot stage is incomplete. Extract the complete package again.", installed);
            return CreateStart(installed, Path.GetDirectoryName(installed)!);
        }
        foreach (var candidate in new[] { baseDirectory, currentDirectory })
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DynaMakerUv.Editor.csproj")) &&
                    File.Exists(Path.Combine(directory.FullName, "project.godot")))
                {
                    var engine = developmentEngine ?? Path.GetFullPath(Path.Combine(directory.FullName,
                        "../../godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64.exe"));
                    if (!File.Exists(engine)) throw new FileNotFoundException("Godot 4.7.1 Mono executable not found", engine);
                    var start = CreateStart(engine, directory.FullName);
                    start.ArgumentList.Add("--path");
                    start.ArgumentList.Add(directory.FullName);
                    return start;
                }
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("Neither the packaged stage nor the DynaMaker UV source project was found.");
    }

    private static ProcessStartInfo CreateStart(string executable, string directory) => new(executable)
    {
        WorkingDirectory = directory,
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
        StandardOutputEncoding = System.Text.Encoding.UTF8,
        StandardErrorEncoding = System.Text.Encoding.UTF8,
    };
}
