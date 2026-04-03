using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using System;
using System.IO;

namespace TargetBarkNotifier;

public sealed partial class MainWindow
{
    private readonly FileDialogManager fileDialogManager = new();
    private bool fileDialogOpened;

    private void SaveConfig()
    {
        plugin.Configuration.Save();
    }

    private bool DrawCheckbox(string label, bool value, Action<bool> save)
    {
        var next = value;
        if (ImGui.Checkbox(label, ref next))
        {
            save(next);
            SaveConfig();
            return true;
        }

        return false;
    }

    private bool DrawInputText(string label, ref string value, int maxLength, Action<string> save)
    {
        if (ImGui.InputText(label, ref value, maxLength))
        {
            save(value);
            SaveConfig();
            return true;
        }

        return false;
    }

    private bool DrawInputTextWithHint(string label, string hint, ref string value, int maxLength, Action<string> save)
    {
        if (ImGui.InputTextWithHint(label, hint, ref value, maxLength))
        {
            save(value);
            SaveConfig();
            return true;
        }

        return false;
    }

    private void StartOpenJsonFileDialogAsync(string title, string currentPath, Action<string?> onCompleted)
    {
        if (fileDialogOpened)
            return;

        fileDialogOpened = true;
        fileDialogManager.OpenFileDialog(
            title,
            ".json",
            (ok, paths) =>
            {
                fileDialogOpened = false;
                var selected = ok && paths.Count > 0 ? paths[0] : null;
                onCompleted(selected);
            },
            1,
            GetDialogStartPath(currentPath),
            false);
    }

    private void StartSaveJsonFileDialogAsync(string title, string currentPath, string defaultFileName, Action<string?> onCompleted)
    {
        if (fileDialogOpened)
            return;

        fileDialogOpened = true;
        fileDialogManager.SaveFileDialog(
            title,
            ".json",
            string.IsNullOrWhiteSpace(defaultFileName) ? "rules" : defaultFileName,
            ".json",
            (ok, path) =>
            {
                fileDialogOpened = false;
                onCompleted(ok ? path : null);
            },
            GetDialogStartPath(currentPath),
            false);
    }

    private static string? GetDialogStartPath(string currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
            return null;

        var directory = Path.GetDirectoryName(currentPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            return directory;

        return null;
    }

    private void DrawFileDialogs()
    {
        try
        {
            fileDialogManager.Draw();
        }
        catch (Exception ex)
        {
            fileDialogOpened = false;
            plugin.Log.Warning(ex, "File dialog draw failed.");
        }
    }
}
