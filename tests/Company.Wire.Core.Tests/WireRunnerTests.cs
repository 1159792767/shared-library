using Company.Wire.Core;
using Xunit;

namespace Company.Wire.Core.Tests;

/// <summary>WireRunner 行为规格：编排全流程、依赖自动补齐、关闭自动挂接时给指引、回滚完整还原。</summary>
public class WireRunnerTests : IDisposable
{
    private readonly TestAssets _assets = new();

    private const string Manifest = "https://updates.company.com/manifest.json";

    private static string ProjectDir(string csprojPath) => Path.GetDirectoryName(csprojPath)!;

    [Fact]
    public void Run_完整接线_各步齐备且日志有序()
    {
        var csproj = _assets.CreateStandardProject("BClient", "Company.Drivers");
        var program = _assets.WriteSource(csproj, "Program.cs", "using System;\r\n\r\nConsole.WriteLine(\"hi\");\r\n");
        var originalProgram = File.ReadAllText(program);
        var logs = new List<string>();

        var result = WireRunner.Run(new WireOptions(csproj, UiKind.Console, Manifest, AutoAttach: true),
            line => logs.Add(line));

        Assert.Equal(new[] { "Company.Drivers" }, result.ScannedPackages);
        Assert.Equal(3, result.GeneratedFiles.Count);
        Assert.True(result.CsprojModified);
        Assert.Equal(StartupPatchStatus.Patched, result.StartupPatch.Status);
        Assert.Contains(csproj + ".bak", result.Backups.Select(b => b.Backup));   // csproj 备份
        Assert.Contains(program + ".bak", result.Backups.Select(b => b.Backup));  // 挂接文件备份
        Assert.Contains("RunAsync();", File.ReadAllText(program));                 // 真挂上了
        Assert.Contains("Company.UpdateChecker", File.ReadAllText(csproj));        // 引用加上了
        Assert.NotEqual(originalProgram, File.ReadAllText(program));               // 入口被改（已备份）
        Assert.Contains("扫描", logs[0]);                                          // 日志从扫描开始
    }

    [Fact]
    public void Run_依赖未引UpdateChecker_自动补进登记集合()
    {
        var csproj = _assets.CreateStandardProject("BClient", "Company.Drivers");

        var result = WireRunner.Run(new WireOptions(csproj, UiKind.Console, Manifest, AutoAttach: true));

        var service = File.ReadAllText(Path.Combine(ProjectDir(csproj), "UpdateCheck", "UpdateCheckService.cs"));
        Assert.Contains("\"Company.UpdateChecker\", AssemblyVersions.FromAssemblyName(\"Company.UpdateChecker\")", service);
        Assert.DoesNotContain("Company.UpdateChecker", result.ScannedPackages); // 扫描结果只含真实引用；自动补齐只发生在登记集合
    }

    [Fact]
    public void Run_关闭自动挂接_降级为指引且不改入口文件()
    {
        var csproj = _assets.CreateStandardProject("BClient", "Company.Drivers");
        var program = _assets.WriteSource(csproj, "Program.cs", "Console.WriteLine(\"hi\");\r\n");
        var before = File.ReadAllText(program);

        var result = WireRunner.Run(new WireOptions(csproj, UiKind.Console, Manifest, AutoAttach: false));

        Assert.Equal(StartupPatchStatus.Downgraded, result.StartupPatch.Status);
        Assert.Contains("RunAsync();", result.StartupPatch.Instructions);
        Assert.Equal(before, File.ReadAllText(program));   // 入口文件绝不动
    }

    [Fact]
    public void Rollback_删除生成文件并从bak完整还原被改文件()
    {
        var csproj = _assets.CreateStandardProject("BClient", "Company.Drivers");
        var program = _assets.WriteSource(csproj, "Program.cs", "Console.WriteLine(\"hi\");\r\n");
        var originalProgram = File.ReadAllText(program);
        var originalCsproj = File.ReadAllText(csproj);

        var result = WireRunner.Run(new WireOptions(csproj, UiKind.Console, Manifest, AutoAttach: true));
        WireRunner.Rollback(result);

        Assert.Equal(originalProgram, File.ReadAllText(program));
        Assert.Equal(originalCsproj, File.ReadAllText(csproj));
        Assert.False(Directory.Exists(Path.Combine(ProjectDir(csproj), "UpdateCheck"))); // 生成目录清掉
        Assert.False(File.Exists(program + ".bak"));                                    // 备份清理
        Assert.False(File.Exists(csproj + ".bak"));
    }

    public void Dispose() => _assets.Dispose();
}
