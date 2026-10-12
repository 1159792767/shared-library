using Company.Wire.Core;
using Xunit;

namespace Company.Wire.Core.Tests;

/// <summary>StartupPatcher 行为规格：三种标准形态的启动挂接、幂等、备份与降级。</summary>
public class StartupPatcherTests : IDisposable
{
    private readonly TestAssets _assets = new();

    private static string ProjectDir(string csprojPath) => Path.GetDirectoryName(csprojPath)!;

    [Fact]
    public void Plan_控制台顶级语句_在首条语句前计划插入await版调用()
    {
        var csproj = _assets.CreateStandardProject("BClient");
        _assets.WriteSource(csproj, "Program.cs",
            "using System;\r\n\r\nConsole.WriteLine(\"hello\");\r\n");

        var plan = StartupPatcher.Plan(ProjectDir(csproj), UiKind.Console, "BClient");

        Assert.Equal(StartupPatchStatus.Patched, plan.Status);
        Assert.EndsWith("Program.cs", plan.TargetFile);
        // Plan 不写盘：内容断言用 PreviewContent（与 Apply 写盘内容完全一致）
        var content = StartupPatcher.PreviewContent(ProjectDir(csproj), UiKind.Console, "BClient");
        Assert.NotNull(content);
        Assert.Contains("// ===== Company UpdateCheck (auto-wired) =====", content);
        Assert.Contains("await BClient.UpdateCheck.UpdateCheckRunner.RunAsync();", content);
        // 注入点必须在首条业务语句之前
        Assert.True(content!.IndexOf("RunAsync();") < content.IndexOf("Console.WriteLine"));
    }

    [Fact]
    public void Plan_WinForms_在ApplicationRun前计划插入fireAndForget版()
    {
        var csproj = _assets.CreateStandardProject("BClient");
        _assets.WriteSource(csproj, "Program.cs",
            "using System;\r\nusing System.Windows.Forms;\r\n\r\nstatic class Program\r\n{\r\n    [STAThread]\r\n    static void Main()\r\n    {\r\n        Application.EnableVisualStyles();\r\n        Application.Run(new Form1());\r\n    }\r\n}\r\n");

        var plan = StartupPatcher.Plan(ProjectDir(csproj), UiKind.WinForms, "BClient");

        Assert.Equal(StartupPatchStatus.Patched, plan.Status);
        var content = StartupPatcher.PreviewContent(ProjectDir(csproj), UiKind.WinForms, "BClient");
        Assert.NotNull(content);
        Assert.Contains("_ = BClient.UpdateCheck.UpdateCheckRunner.RunAsync();", content);
        Assert.True(content!.IndexOf("RunAsync();") < content.IndexOf("Application.Run"));
    }

    [Fact]
    public void Plan_WPF_在baseOnStartup后计划插入fireAndForget版()
    {
        var csproj = _assets.CreateStandardProject("BClient");
        _assets.WriteSource(csproj, "App.xaml.cs",
            "public partial class App : System.Windows.Application\r\n{\r\n    protected override void OnStartup(StartupEventArgs e)\r\n    {\r\n        base.OnStartup(e);\r\n        new MainWindow().Show();\r\n    }\r\n}\r\n");

        var plan = StartupPatcher.Plan(ProjectDir(csproj), UiKind.Wpf, "BClient");

        Assert.Equal(StartupPatchStatus.Patched, plan.Status);
        Assert.EndsWith("App.xaml.cs", plan.TargetFile);
        var content = StartupPatcher.PreviewContent(ProjectDir(csproj), UiKind.Wpf, "BClient");
        Assert.NotNull(content);
        Assert.Contains("_ = BClient.UpdateCheck.UpdateCheckRunner.RunAsync();", content);
        Assert.True(content!.IndexOf("RunAsync();") > content.IndexOf("base.OnStartup"));
    }

    [Fact]
    public void Plan_控制台传统Main_方法体开头插入fireAndForget版()
    {
        var csproj = _assets.CreateStandardProject("BClient");
        _assets.WriteSource(csproj, "Program.cs",
            "class Program\r\n{\r\n    static void Main(string[] args)\r\n    {\r\n        System.Console.WriteLine(\"hello\");\r\n    }\r\n}\r\n");

        var plan = StartupPatcher.Plan(ProjectDir(csproj), UiKind.Console, "BClient");

        Assert.Equal(StartupPatchStatus.Patched, plan.Status);
        var content = StartupPatcher.PreviewContent(ProjectDir(csproj), UiKind.Console, "BClient");
        Assert.NotNull(content);
        Assert.Contains("_ = BClient.UpdateCheck.UpdateCheckRunner.RunAsync();", content);
        Assert.True(content!.IndexOf("RunAsync();") < content.IndexOf("WriteLine"));
    }

    [Fact]
    public void Plan_已含标记_返回AlreadyPatched且文件不被改()
    {
        var csproj = _assets.CreateStandardProject("BClient");
        var file = _assets.WriteSource(csproj, "Program.cs",
            "// ===== Company UpdateCheck (auto-wired) =====\r\n_ = BClient.UpdateCheck.UpdateCheckRunner.RunAsync();\r\nConsole.WriteLine(\"hello\");\r\n");
        var before = File.ReadAllText(file);

        var plan = StartupPatcher.Plan(ProjectDir(csproj), UiKind.Console, "BClient");

        Assert.Equal(StartupPatchStatus.AlreadyPatched, plan.Status);
        Assert.Equal(before, File.ReadAllText(file)); // Plan 本就不写盘
        Assert.Null(StartupPatcher.PreviewContent(ProjectDir(csproj), UiKind.Console, "BClient"));
    }

    [Fact]
    public void Plan_形态不认识_降级为指引文本()
    {
        var csproj = _assets.CreateStandardProject("BClient");
        // WPF 形态但找不到 App.xaml.cs
        var plan = StartupPatcher.Plan(ProjectDir(csproj), UiKind.Wpf, "BClient");

        Assert.Equal(StartupPatchStatus.Downgraded, plan.Status);
        Assert.NotNull(plan.Instructions);
        Assert.Contains("_ = BClient.UpdateCheck.UpdateCheckRunner.RunAsync();", plan.Instructions);
    }

    [Fact]
    public void Apply_写入文件并生成bak备份()
    {
        var csproj = _assets.CreateStandardProject("BClient");
        var file = _assets.WriteSource(csproj, "Program.cs",
            "using System;\r\n\r\nConsole.WriteLine(\"hello\");\r\n");
        var original = File.ReadAllText(file);

        var result = StartupPatcher.Apply(ProjectDir(csproj), UiKind.Console, "BClient");

        Assert.Equal(StartupPatchStatus.Patched, result.Status);
        Assert.True(File.Exists(file + ".bak"));
        Assert.Equal(original, File.ReadAllText(file + ".bak"));
        Assert.Contains("RunAsync();", File.ReadAllText(file));
    }

    [Fact]
    public void Apply_重复执行_幂等不二次插入()
    {
        var csproj = _assets.CreateStandardProject("BClient");
        _assets.WriteSource(csproj, "Program.cs", "Console.WriteLine(\"hello\");\r\n");
        StartupPatcher.Apply(ProjectDir(csproj), UiKind.Console, "BClient");
        var program = Path.Combine(ProjectDir(csproj), "Program.cs");
        var afterFirst = File.ReadAllText(program);

        var second = StartupPatcher.Apply(ProjectDir(csproj), UiKind.Console, "BClient");

        Assert.Equal(StartupPatchStatus.AlreadyPatched, second.Status);
        Assert.Equal(afterFirst, File.ReadAllText(program));
    }

    public void Dispose() => _assets.Dispose();
}
