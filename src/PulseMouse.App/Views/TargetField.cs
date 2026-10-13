using System.IO;
using PulseMouse.Core;
using Forms = System.Windows.Forms;

namespace PulseMouse.App.Views;

/// <summary>Label, placeholder, hint and picker for the action target of each kind.</summary>
public static class TargetField
{
    public static (string Label, string Placeholder, string Hint, bool Browse) Config(ActionKind kind) => kind switch
    {
        ActionKind.Folder => ("文件夹", @"D:\Projects", "打开前会检查目录是否存在", true),
        ActionKind.Url => ("网址", "https://", "仅支持 http:// 或 https://", false),
        _ => ("程序", @"notepad.exe 或 C:\…\app.exe", "支持 .exe / .lnk，可使用 %环境变量%", true)
    };

    public static string StepPlaceholder(ActionKind kind) => kind switch
    {
        ActionKind.Folder => @"D:\Folder",
        ActionKind.Url => "https://",
        _ => "program.exe"
    };

    /// <summary>Opens the system picker; returns the chosen path or null when cancelled.</summary>
    public static string? Browse(ActionKind kind, string current)
    {
        if (kind == ActionKind.Folder)
        {
            using var dialog = new Forms.FolderBrowserDialog { Description = "选择要打开的文件夹", ShowNewFolderButton = false, UseDescriptionForTitle = true };
            if (Directory.Exists(current)) dialog.SelectedPath = current;
            return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }
        using var picker = new Forms.OpenFileDialog
        {
            Title = "选择应用程序或快捷方式", Filter = "应用或快捷方式 (*.exe;*.lnk)|*.exe;*.lnk|所有文件 (*.*)|*.*", CheckFileExists = true
        };
        return picker.ShowDialog() == Forms.DialogResult.OK ? picker.FileName : null;
    }

    public static int KindIndex(ActionKind kind) => (int)kind;
    public static ActionKind KindAt(int index) => index is >= 0 and <= 3 ? (ActionKind)index : ActionKind.Application;
}
