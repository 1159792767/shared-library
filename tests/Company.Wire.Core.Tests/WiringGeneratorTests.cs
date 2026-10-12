using Company.Wire.Core;
using Xunit;

namespace Company.Wire.Core.Tests;

/// <summary>WiringGenerator 行为规格：接线文件的内容契约（namespace、清单地址、依赖登记、形态分支）。</summary>
public class WiringGeneratorTests : IDisposable
{
    private readonly TestAssets _assets = new();

    private static string ProjectDir(string csprojPath) => Path.GetDirectoryName(csprojPath)!;

    [Fact]
    public void Generate_生成三个文件且服务文件含命名空间与清单地址()
    {
        var csproj = _assets.CreateStandardProject("BClient");

        var files = WiringGenerator.Generate(ProjectDir(csproj), "BClient", UiKind.Console,
            "https://updates.company.com/manifest.json", new[] { "Company.Drivers", "Company.UpdateChecker" });

        Assert.Equal(3, files.Count);
        Assert.All(files, f => Assert.True(File.Exists(f)));
        var service = File.ReadAllText(Path.Combine(ProjectDir(csproj), "UpdateCheck", "UpdateCheckService.cs"));
        Assert.Contains("namespace BClient.UpdateCheck;", service);
        Assert.Contains("\"https://updates.company.com/manifest.json\"", service);
    }

    [Fact]
    public void Generate_依赖清单逐包写成FromAssemblyName形式()
    {
        var csproj = _assets.CreateStandardProject("BClient");

        WiringGenerator.Generate(ProjectDir(csproj), "BClient", UiKind.Console,
            "https://x/manifest.json", new[] { "Company.Drivers", "Company.UpdateChecker" });

        var service = File.ReadAllText(Path.Combine(ProjectDir(csproj), "UpdateCheck", "UpdateCheckService.cs"));
        Assert.Contains("new PackageDependency(\"Company.Drivers\", AssemblyVersions.FromAssemblyName(\"Company.Drivers\"))", service);
        Assert.Contains("new PackageDependency(\"Company.UpdateChecker\", AssemblyVersions.FromAssemblyName(\"Company.UpdateChecker\"))", service);
    }

    [Fact]
    public void Generate_WinForms形态_Runner为MessageBox弹窗且critical用警示图标()
    {
        var csproj = _assets.CreateStandardProject("BClient");

        WiringGenerator.Generate(ProjectDir(csproj), "BClient", UiKind.WinForms,
            "https://x/manifest.json", new[] { "Company.UpdateChecker" });

        var runner = File.ReadAllText(Path.Combine(ProjectDir(csproj), "UpdateCheck", "UpdateCheckRunner.cs"));
        Assert.Contains("System.Windows.Forms.MessageBox.Show", runner);
        Assert.Contains("MessageBoxIcon.Warning", runner);     // critical 强提示
        Assert.Contains("MessageBoxIcon.Information", runner); // 常规提示
    }

    [Fact]
    public void Generate_WPF形态_Runner用WPFMessageBox()
    {
        var csproj = _assets.CreateStandardProject("BClient");

        WiringGenerator.Generate(ProjectDir(csproj), "BClient", UiKind.Wpf,
            "https://x/manifest.json", new[] { "Company.UpdateChecker" });

        var runner = File.ReadAllText(Path.Combine(ProjectDir(csproj), "UpdateCheck", "UpdateCheckRunner.cs"));
        Assert.Contains("System.Windows.MessageBox.Show", runner);
        Assert.Contains("MessageBoxImage.Warning", runner);
    }

    [Fact]
    public void Generate_控制台形态_Runner打印提示且捕获清单异常()
    {
        var csproj = _assets.CreateStandardProject("BClient");

        WiringGenerator.Generate(ProjectDir(csproj), "BClient", UiKind.Console,
            "https://x/manifest.json", new[] { "Company.UpdateChecker" });

        var runner = File.ReadAllText(Path.Combine(ProjectDir(csproj), "UpdateCheck", "UpdateCheckRunner.cs"));
        Assert.Contains("Console.WriteLine", runner);
        Assert.Contains("catch (UpdateManifestException)", runner); // 检测失败静默降级
    }

    [Fact]
    public void Generate_接线说明md存在且含令牌环境变量说明()
    {
        var csproj = _assets.CreateStandardProject("BClient");

        WiringGenerator.Generate(ProjectDir(csproj), "BClient", UiKind.WinForms,
            "https://x/manifest.json", new[] { "Company.UpdateChecker" });

        var readme = File.ReadAllText(Path.Combine(ProjectDir(csproj), "UpdateCheck", "接线说明.md"));
        Assert.Contains("UPDATE_MANIFEST_TOKEN", readme);
        Assert.Contains("PR", readme);
    }

    public void Dispose() => _assets.Dispose();
}
