using System.Text;

namespace Company.Wire.Core;

/// <summary>
/// 启动挂接：把更新检查调用写进项目程序入口（仅认三种标准形态，其余降级为指引，绝不盲改）。
/// 注入行统一带标记注释，重复执行幂等；Apply 改前生成 .bak 备份。
/// </summary>
public static class StartupPatcher
{
    private const string Marker = "// ===== Company UpdateCheck (auto-wired) =====";

    public static StartupPatchResult Plan(string projectDir, UiKind kind, string rootNamespace) =>
        PlanCore(projectDir, kind, rootNamespace, write: false);

    public static StartupPatchResult Apply(string projectDir, UiKind kind, string rootNamespace) =>
        PlanCore(projectDir, kind, rootNamespace, write: true);

    private static StartupPatchResult PlanCore(string projectDir, UiKind kind, string rootNamespace, bool write)
    {
        var file = kind == UiKind.Wpf
            ? Path.Combine(projectDir, "App.xaml.cs")
            : Path.Combine(projectDir, "Program.cs");

        if (!File.Exists(file))
        {
            return Downgraded(rootNamespace, $"找不到 {Path.GetFileName(file)}：");
        }

        var lines = File.ReadAllLines(file);
        if (lines.Any(l => l.TrimStart().StartsWith(Marker, StringComparison.Ordinal)))
        {
            return new StartupPatchResult(StartupPatchStatus.AlreadyPatched, file, null);
        }

        var (index, awaitCall) = Locate(lines, kind);
        if (index < 0)
        {
            return Downgraded(rootNamespace, $"已找到 {Path.GetFileName(file)} 但未识别出标准插入点：");
        }

        if (write)
        {
            File.Copy(file, file + ".bak", overwrite: true);
            File.WriteAllLines(file, BuildPatchedLines(lines, index, awaitCall, rootNamespace), new UTF8Encoding(false));
        }

        return new StartupPatchResult(StartupPatchStatus.Patched, file, null);
    }

    /// <summary>Plan 模式的预览内容：与 Apply 写盘内容完全一致的整文件文本（供向导差异预览）；无可挂接时为 null。</summary>
    public static string? PreviewContent(string projectDir, UiKind kind, string rootNamespace)
    {
        var result = Plan(projectDir, kind, rootNamespace);
        if (result.Status != StartupPatchStatus.Patched)
        {
            return null;
        }

        var lines = File.ReadAllLines(result.TargetFile!);
        var (index, awaitCall) = Locate(lines, kind);
        return string.Join(Environment.NewLine, BuildPatchedLines(lines, index, awaitCall, rootNamespace));
    }

    private static StartupPatchResult Downgraded(string rootNamespace, string reason)
    {
        var call = $"_ = {rootNamespace}.UpdateCheck.UpdateCheckRunner.RunAsync();";
        return new StartupPatchResult(StartupPatchStatus.Downgraded, null,
            reason + "请在程序启动处（Program.cs 的 Main / App.xaml.cs 的 OnStartup）手工加入这一行：\r\n" +
            $"{call}\r\n（控制台顶级语句程序可用 await 形式：await {rootNamespace}.UpdateCheck.UpdateCheckRunner.RunAsync();）");
    }

    /// <summary>定位插入点：返回 (插入行下标, 是否 await 版)。注入块插到该行之前。</summary>
    private static (int Index, bool AwaitCall) Locate(string[] lines, UiKind kind) => kind switch
    {
        UiKind.Wpf => (IndexOfAfter(lines, "base.OnStartup"), false),
        UiKind.WinForms => (IndexOfBefore(lines, "Application.Run"), false),
        UiKind.Console => IsTopLevelStatements(lines)
            ? (IndexAfterDirectives(lines), true)
            : (IndexInsideMain(lines), false),
        _ => (-1, false),
    };

    private static List<string> BuildPatchedLines(string[] lines, int index, bool awaitCall, string rootNamespace)
    {
        var call = (awaitCall ? "await " : "_ = ") + rootNamespace + ".UpdateCheck.UpdateCheckRunner.RunAsync();";
        var indent = DetectIndent(lines, index);
        var result = lines.ToList();
        result.InsertRange(index, new[] { Marker, indent + call, "" });
        return result;
    }

    private static string DetectIndent(string[] lines, int index)
    {
        var line = lines[index < lines.Length ? index : lines.Length - 1];
        var indent = line.Length - line.TrimStart().Length;
        return new string(' ', Math.Max(indent, 0));
    }

    private static int IndexOfAfter(string[] lines, string anchor)
    {
        var i = Array.FindIndex(lines, l => l.Contains(anchor));
        return i < 0 ? -1 : i + 1; // 锚点行的下一行
    }

    private static int IndexOfBefore(string[] lines, string anchor) =>
        Array.FindIndex(lines, l => l.Contains(anchor));

    private static bool IsTopLevelStatements(string[] lines) =>
        !lines.Any(l => l.Contains("static") && l.Contains("Main("));

    private static int IndexAfterDirectives(string[] lines)
    {
        var i = 0;
        // 跳过文件头：using / 预处理器指令 / 空行 / 行注释 / 块注释
        while (i < lines.Length)
        {
            var t = lines[i].TrimStart();
            if (t.Length == 0 || t.StartsWith("using ") || t.StartsWith("#") || t.StartsWith("//"))
            {
                i++;
                continue;
            }
            if (t.StartsWith("/*"))
            {
                while (i < lines.Length && !lines[i].Contains("*/")) { i++; }
                i++;
                continue;
            }
            break;
        }

        return i;
    }

    private static int IndexInsideMain(string[] lines)
    {
        var mainLine = Array.FindIndex(lines, l => l.Contains("static") && l.Contains("Main("));
        if (mainLine < 0)
        {
            return -1;
        }

        var open = Array.FindIndex(lines, mainLine, l => l.Contains("{"));
        return open < 0 ? -1 : open + 1; // Main 方法体第一行
    }
}
