using Avalonia.Controls;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Loads before live Dock attachment and saves before native-stage teardown.</summary>
internal sealed class WorkbenchLayoutSession(WorkbenchUserStore store, WorkbenchFactory factory, Action<string> report)
{
    public void Load(Window window)
    {
        var snapshot = store.Load<WorkbenchLayoutSnapshot>(WorkbenchLayoutSnapshot.FileName);
        if (snapshot is null) return;
        try
        {
            snapshot.Validate();
            foreach (var floating in snapshot.Windows)
            {
                var screens = window.Screens.All;
                var screen = screens.FirstOrDefault(s => s.WorkingArea.Contains(new Avalonia.PixelPoint((int)floating.X, (int)floating.Y)))
                    ?? window.Screens.Primary;
                if (screen is null) { floating.X = 60; floating.Y = 80; continue; }
                var area = screen.WorkingArea;
                var scale = screen.Scaling;
                floating.Width = Math.Min(floating.Width, area.Width / scale);
                floating.Height = Math.Min(floating.Height, area.Height / scale);
                floating.X = Math.Clamp(floating.X, area.X, Math.Max(area.X, area.Right - floating.Width * scale));
                floating.Y = Math.Clamp(floating.Y, area.Y, Math.Max(area.Y, area.Bottom - floating.Height * scale));
            }
            factory.RestoreLayoutBeforeAttach(snapshot);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or NullReferenceException)
        {
            store.Protect(WorkbenchLayoutSnapshot.FileName, ex.Message);
            report(store.Warning!);
        }
    }

    public void Save()
    {
        try
        {
            var snapshot = factory.CaptureLayout();
            snapshot.Validate();
            if (!store.Save(WorkbenchLayoutSnapshot.FileName, snapshot)) report(store.Warning!);        }
        catch (InvalidDataException ex) { report("Layout not saved: " + ex.Message); }
    }
}
