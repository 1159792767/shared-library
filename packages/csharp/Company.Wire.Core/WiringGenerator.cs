using System.Text;

namespace Company.Wire.Core;

/// <summary>接线文件生成器：在目标工程下生成 UpdateCheck/ 目录（属向导所有，重复执行覆盖）。</summary>
public static class WiringGenerator
{
    public static IReadOnlyList<string> Generate(
        string projectDir, string rootNamespace, UiKind uiKind,
        string manifestUrl, IReadOnlyList<string> dependencies)
    {
        var dir = Path.Combine(projectDir, "UpdateCheck");
        Directory.CreateDirectory(dir);

        var deps = string.Join("\n", dependencies.Select(d =>
            $"        new PackageDependency(\"{d}\", AssemblyVersions.FromAssemblyName(\"{d}\")),"));

        var service = Templates.Service
            .Replace("__NS__", rootNamespace)
            .Replace("__MANIFEST_URL__", manifestUrl)
            .Replace("__DEPS__", deps);
        var runner = (uiKind switch
        {
            UiKind.WinForms => Templates.RunnerWinForms,
            UiKind.Wpf => Templates.RunnerWpf,
            _ => Templates.RunnerConsole,
        }).Replace("__NS__", rootNamespace);
        var readme = Templates.Readme.Replace("__NS__", rootNamespace);

        var files = new[]
        {
            Path.Combine(dir, "UpdateCheckService.cs"),
            Path.Combine(dir, "UpdateCheckRunner.cs"),
            Path.Combine(dir, "接线说明.md"),
        };
        File.WriteAllText(files[0], service, new UTF8Encoding(false));
        File.WriteAllText(files[1], runner, new UTF8Encoding(false));
        File.WriteAllText(files[2], readme, new UTF8Encoding(false));
        return files;
    }
}
