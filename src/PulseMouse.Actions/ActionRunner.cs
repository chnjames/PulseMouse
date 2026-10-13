using System.Diagnostics;
using System.Text.Json;
using PulseMouse.Core;
namespace PulseMouse.Actions;
public static class ActionRunner
{
    public static void Run(CommandBinding binding) => Run(binding, false);
    public static void Run(CommandBinding binding, bool workflowAuthorized)
    {
        if (string.IsNullOrWhiteSpace(binding.Target)) throw new ArgumentException("动作目标不能为空。");
        string target = binding.Target.Trim();
        if (binding.ActionKind == ActionKind.Workflow)
        {
            if (!workflowAuthorized) throw new InvalidOperationException("工作流需要用户确认。");
            var steps = JsonSerializer.Deserialize<List<WorkflowStep>>(target) ?? throw new ArgumentException("工作流不能为空");
            if (steps.Count is < 1 or > 12) throw new ArgumentException("工作流需要 1–12 个步骤");
            foreach (var step in steps)
            {
                if (step.Kind == ActionKind.Workflow) throw new ArgumentException("不允许嵌套工作流");
                if (step.DelayMs is < 0 or > 5000) throw new ArgumentException("延迟必须在 0–5000ms");
                if (step.DelayMs > 0) Thread.Sleep(step.DelayMs);
                Run(new CommandBinding { ActionKind = step.Kind, Target = step.Target }, false);
            }
            return;
        }
        switch (binding.ActionKind)
        {
            case ActionKind.Shortcut:
                if (!KeyChord.TryParse(target, out var chord)) throw new ArgumentException("无法识别的快捷键：" + target);
                KeySender.Send(chord);
                return;
            case ActionKind.Url:
                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new ArgumentException("只允许 http/https 网址。");
                break;
            case ActionKind.Folder:
                target = Path.GetFullPath(Environment.ExpandEnvironmentVariables(target));
                if (!Directory.Exists(target)) throw new DirectoryNotFoundException(target);
                break;
            case ActionKind.Application:
                target = Environment.ExpandEnvironmentVariables(target);
                break;
        }
        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
    }
}
