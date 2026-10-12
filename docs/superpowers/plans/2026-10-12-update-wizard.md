# 公共库接线安装向导 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付双击式 WinForms 安装向导（Company.UpdateChecker.Setup），自动为既有 C# 项目接线"程序内更新检查"，全程鼠标操作零命令行。

**Architecture:** 纯库 `Company.Wire.Core`（扫描依赖 → 生成接线文件 → 幂等改 csproj → 三形态启动挂接 → 失败回滚，全 TDD）+ WinForms 薄壳 `Company.UpdateChecker.Setup`（5 页向导，调 Core + dotnet build 冒烟）+ `Company.UpdateChecker` 1.2.0 新增 `FromAssemblyName`（生成的代码按程序集名注册依赖，向导无需知道各包入口类型）。

**Tech Stack:** .NET 10（Core=net10.0 纯库 / Setup=net10.0-windows WinForms）、xUnit、GitHub Actions（复用现有 release 流水线，Setup 走 publish+Release 附件）。

**Spec:** `docs/superpowers/specs/2026-10-12-update-wizard-design.md`（本计划从规格出发，执行者须同时读规格）

## Global Constraints

- **TDD 铁律**：Task 1~6 的每个行为先写失败测试、亲眼看它失败（RED），再写最小实现（GREEN）。WinForms 壳（Task 7）与 CI 配置（Task 9）按规格 §5 豁免，验证方式为构建+冒烟。
- **命令一律在 `c:\Users\帅b\Desktop\公共库项目库\shared-library` 下执行**；无 sln，`dotnet test --nologo` 按目录发现测试工程。单测过滤：`dotnet test tests\<工程名> --nologo --filter "FullyQualifiedName~<关键字>"`。
- **Shell 是 Windows PowerShell 5.1**：语句分隔用 `;` 不用 `&&`；**中文 git 消息必须走 UTF-8 文件**：
  ```powershell
  $msg = "<中文消息>"; [IO.File]::WriteAllText("$env:TEMP\commit-msg.txt", $msg, (New-Object Text.UTF8Encoding $false)); git commit -F "$env:TEMP\commit-msg.txt"
  ```
- **代码/注释全中文**，与仓库现有风格一致（简体中文 XML 文档注释、命名用英文）。
- **CPM**：所有 PackageReference 不写 Version（`Directory.Packages.props` 管理）；本计划零新增外部包。
- **继承配置**：`Directory.Build.props` 已全局 net10.0 / Nullable / ImplicitUsings；Setup 工程在自己的 csproj 覆写 `net10.0-windows`。
- **接口契约（跨任务共享，签名以此为准）**：
  ```csharp
  // Company.UpdateChecker（Task 1 产出，Task 5 模板引用其名字）
  public static class AssemblyVersions { public static SemVersion FromAssemblyName(string assemblyName); }

  // Company.Wire.Core（Task 2~6 产出，Task 7 壳消费）
  public enum UiKind { Console, WinForms, Wpf }
  public enum StartupPatchStatus { Patched, AlreadyPatched, Downgraded }
  public sealed record WireOptions(string ProjectPath, UiKind UiKind, string ManifestUrl, bool AutoAttach, string UpdateCheckerVersion = "1.2.0");
  public sealed record StartupPatchResult(StartupPatchStatus Status, string? TargetFile, string? Instructions);
  public sealed record BackupEntry(string Original, string Backup);
  public sealed record WireResult(
      IReadOnlyList<string> ScannedPackages, IReadOnlyList<string> GeneratedFiles,
      bool CsprojModified, StartupPatchResult StartupPatch, IReadOnlyList<BackupEntry> Backups);
  public sealed record ScanResult(IReadOnlyList<string> CompanyPackages, string RootNamespace);
  public static class DependencyScanner { public static ScanResult Scan(string csprojPath); }
  public static class CsprojEditor { public static bool TryAddPackageReference(string csprojPath, string packageId, string version); }
  public static class StartupPatcher
  {
      public static StartupPatchResult Plan(string projectDir, UiKind kind, string rootNamespace); // 纯计算，不写盘
      public static StartupPatchResult Apply(string projectDir, UiKind kind, string rootNamespace); // 写盘 + .bak 备份
  }
  public static class WiringGenerator
  {
      public static IReadOnlyList<string> Generate(string projectDir, string rootNamespace, UiKind uiKind,
          string manifestUrl, IReadOnlyList<string> dependencies); // 返回生成文件绝对路径
  }
  public static class WireRunner
  {
      public static WireResult Run(WireOptions options, Action<string>? log = null);
      public static void Rollback(WireResult result);
  }
  ```
- **挂接注入行**（Task 4/5 共用，`__NS__`=根命名空间）：
  - Console 顶级语句：`await __NS__.UpdateCheck.UpdateCheckRunner.RunAsync();`
  - WinForms / WPF / 传统 Main：`_ = __NS__.UpdateCheck.UpdateCheckRunner.RunAsync();`（fire-and-forget，`RunAsync` 内部全捕获，检测失败绝不影响程序运行）
  - 统一标记注释行：`// ===== Company UpdateCheck (auto-wired) =====`
- **默认清单地址**：`https://raw.githubusercontent.com/1159792767/shared-library/main/tools/manifest/manifest.json`（上线 GitLab 时只改模板默认值一处）。

---

### Task 1: UpdateChecker 1.2.0 —— FromAssemblyName（TDD）

**Files:**
- Modify: `packages/csharp/Company.UpdateChecker/AssemblyVersions.cs`
- Modify: `packages/csharp/Company.UpdateChecker/Company.UpdateChecker.csproj`（版本 1.1.0→1.2.0 + 描述）
- Test: `tests/Company.UpdateChecker.Tests/AssemblyVersionsTests.cs`

**Interfaces:**
- Consumes: 现有 `AssemblyVersions.FromAssembly(Assembly)`、`SemVersion.Parse`。
- Produces: `public static SemVersion FromAssemblyName(string assemblyName)`——按名加载程序集读版本；找不到时抛 `InvalidOperationException`（消息含程序集名）。Task 5 生成的接线代码将按名调用它。

- [ ] **Step 1: 写失败测试**（追加到 `AssemblyVersionsTests.cs` 类内，风格与现有两个测试一致）

```csharp
    [Fact]
    public void FromAssemblyName_按名称加载程序集_解析出SemVer()
    {
        // 测试工程引用了 UpdateChecker 工程，其程序集必然在依赖图中，可按名解析
        var version = AssemblyVersions.FromAssemblyName("Company.UpdateChecker");

        Assert.NotNull(version);
        Assert.False(version.IsPrerelease);
        Assert.True(version.Major >= 1);
    }

    [Fact]
    public void FromAssemblyName_程序集不存在_抛出含名称的明确异常()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => AssemblyVersions.FromAssemblyName("Company.NoSuchPackage"));
        Assert.Contains("Company.NoSuchPackage", ex.Message);
    }
```

- [ ] **Step 2: 跑测试确认 RED**

Run: `dotnet test tests\Company.UpdateChecker.Tests --nologo --filter "FullyQualifiedName~FromAssemblyName"`
Expected: 编译错误 CS0117（`AssemblyVersions` 不含 `FromAssemblyName`）——缺方法的正确 RED。

- [ ] **Step 3: 最小实现**（加到 `AssemblyVersions.cs` 的 `AssemblyVersions` 类内，`FromAssembly` 之后）

```csharp
    /// <summary>
    /// 按程序集名（= NuGet 包名）注册依赖版本：接线向导生成的代码用它，
    /// 向导无需知道各包的入口类型；新增公共包时向导与接线代码零改动。
    /// 程序集不存在 = 引用未还原或包名写错，必须快速失败。
    /// </summary>
    public static SemVersion FromAssemblyName(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        try
        {
            return FromAssembly(Assembly.Load(new AssemblyName(assemblyName)));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            throw new InvalidOperationException(
                $"按名加载程序集 {assemblyName} 失败：请确认已通过 NuGet 引用该包且已还原。", ex);
        }
    }
```

- [ ] **Step 4: 跑测试确认 GREEN（且旧测试不回归）**

Run: `dotnet test tests\Company.UpdateChecker.Tests --nologo`
Expected: 全部 PASS（原有 + 新增 2 个）。

- [ ] **Step 5: 升版本号**（`Company.UpdateChecker.csproj`）

```xml
    <Version>1.2.0</Version>
    <Description>公司程序内更新检查模块：拉取版本清单、SemVer 对比、生成程序内"有新版本"提示；1.1.0 起支持 bearer 鉴权的私有清单地址；1.2.0 新增 FromAssemblyName 按程序集名注册依赖（接线向导配套）。</Description>
```

- [ ] **Step 6: 全仓测试 + 提交**

Run: `dotnet test --nologo` → 全绿（39 个）。
```powershell
$msg = "新增 AssemblyVersions.FromAssemblyName 按程序集名注册依赖（UpdateChecker 1.2.0，接线向导配套）"; [IO.File]::WriteAllText("$env:TEMP\commit-msg.txt", $msg, (New-Object Text.UTF8Encoding $false)); git add -A; git commit -F "$env:TEMP\commit-msg.txt"
```

---

### Task 2: Wire.Core 骨架 + 测试资产助手 + DependencyScanner（TDD）

**Files:**
- Create: `packages/csharp/Company.Wire.Core/Company.Wire.Core.csproj`
- Create: `packages/csharp/Company.Wire.Core/WireOptions.cs`（放 Global Constraints 里的枚举/记录，除 WireResult/BackupEntry 需要 BackupEntry…全部放这一个文件）
- Create: `packages/csharp/Company.Wire.Core/DependencyScanner.cs`
- Create: `tests/Company.Wire.Core.Tests/Company.Wire.Core.Tests.csproj`
- Create: `tests/Company.Wire.Core.Tests/TestAssets.cs`
- Test: `tests/Company.Wire.Core.Tests/DependencyScannerTests.cs`

**Interfaces:**
- Produces: `ScanResult(IReadOnlyList<string> CompanyPackages, string RootNamespace)`、`DependencyScanner.Scan(csprojPath)`；测试助手 `TestAssets.TempProject(string csprojXml, string projectName)`（见 Step 1）——后续所有 Core 测试共用。

- [ ] **Step 1: 建工程 + 模型文件 + 测试助手（先于测试的脚手架，随本任务交付）**

`packages/csharp/Company.Wire.Core/Company.Wire.Core.csproj`：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- 接线核心库：不单独发 NuGet，由 Setup 工程引用同仓构建 -->
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

`packages/csharp/Company.Wire.Core/WireOptions.cs`（Global Constraints 中列出的全部公共类型，逐字落实；文件加一行头注释 `// 接线向导的选项、结果与状态类型（规格见 docs/superpowers/specs/2026-10-12-update-wizard-design.md）`）：
```csharp
namespace Company.Wire.Core;

/// <summary>项目程序的 UI 形态，决定生成的提示样式与启动挂接位置。</summary>
public enum UiKind { Console, WinForms, Wpf }

/// <summary>启动挂接结果。</summary>
public enum StartupPatchStatus
{
    /// <summary>已自动写入启动调用。</summary>
    Patched,
    /// <summary>先前已挂接（幂等跳过）。</summary>
    AlreadyPatched,
    /// <summary>非标准形态，降级为指引文本（绝不盲改用户代码）。</summary>
    Downgraded,
}

/// <summary>一次接线的全部选项。</summary>
public sealed record WireOptions(
    string ProjectPath,          // 目标 .csproj 绝对路径
    UiKind UiKind,               // 生成的提示形态
    string ManifestUrl,          // 版本清单地址
    bool AutoAttach,             // 是否自动挂接启动调用（false = 只给指引）
    string UpdateCheckerVersion = "1.2.0");

/// <summary>启动挂接结果：状态 + 目标文件 + 降级时的指引文本。</summary>
public sealed record StartupPatchResult(StartupPatchStatus Status, string? TargetFile, string? Instructions);

/// <summary>回滚用的备份记录：Original 被改前备份到 Backup（.bak）。</summary>
public sealed record BackupEntry(string Original, string Backup);

/// <summary>一次接线的执行结果（向导完成页与回滚的输入）。</summary>
public sealed record WireResult(
    IReadOnlyList<string> ScannedPackages,
    IReadOnlyList<string> GeneratedFiles,
    bool CsprojModified,
    StartupPatchResult StartupPatch,
    IReadOnlyList<BackupEntry> Backups);

/// <summary>csproj 扫描结果：Company.* 依赖包名 + 根命名空间。</summary>
public sealed record ScanResult(IReadOnlyList<string> CompanyPackages, string RootNamespace);
```

`tests/Company.Wire.Core.Tests/Company.Wire.Core.Tests.csproj`（照 UpdateChecker.Tests 模式，CPM 无需版本号）：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\packages\csharp\Company.Wire.Core\Company.Wire.Core.csproj" />
  </ItemGroup>
</Project>
```

`tests/Company.Wire.Core.Tests/TestAssets.cs`（所有 Core 测试共用的临时工程工厂；每测试独立临时目录，Dispose 自动清理）：
```csharp
using System.Text;

namespace Company.Wire.Core.Tests;

/// <summary>Core 测试资产：在每个测试独立的临时目录里造出可扫描/可改写的 csproj 与源码文件。</summary>
public sealed class TestAssets : IDisposable
{
    public string Root { get; }

    public TestAssets()
    {
        Root = Path.Combine(Path.GetTempPath(), "wire-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>在根目录下建 &lt;name&gt;/&lt;name&gt;.csproj，写入给定 XML 内容；返回 csproj 路径。</summary>
    public string CreateProject(string name, string csprojXml)
    {
        var dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        var csproj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(csproj, csprojXml, new UTF8Encoding(false));
        return csproj;
    }

    /// <summary>便捷工厂：标准 SDK 工程（可选多个 PackageReference）。</summary>
    public string CreateStandardProject(string name, params string[] packageIds)
    {
        var refs = string.Join(Environment.NewLine, packageIds.Select(p =>
            $"      <PackageReference Include=\"{p}\" Version=\"1.0.0\" />"));
        return CreateProject(name, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
            {refs}
              </ItemGroup>
            </Project>
            """);
    }

    /// <summary>在工程目录写源码文件；返回文件绝对路径。</summary>
    public string WriteSource(string csprojPath, string fileName, string content)
    {
        var file = Path.Combine(Path.GetDirectoryName(csprojPath)!, fileName);
        File.WriteAllText(file, content, new UTF8Encoding(false));
        return file;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) { Directory.Delete(Root, true); }
    }
}
```

- [ ] **Step 2: 写失败测试** `tests/Company.Wire.Core.Tests/DependencyScannerTests.cs`

```csharp
using Company.Wire.Core;
using Xunit;

namespace Company.Wire.Core.Tests;

/// <summary>DependencyScanner 行为规格：从 csproj 提取 Company.* 包名与根命名空间。</summary>
public class DependencyScannerTests : IDisposable
{
    private readonly TestAssets _assets = new();

    [Fact]
    public void Scan_混合包引用_只返回Company开头的包且保持文档顺序()
    {
        var csproj = _assets.CreateStandardProject("BClient",
            "Newtonsoft.Json", "Company.Drivers", "xunit", "Company.UpdateChecker");

        var result = DependencyScanner.Scan(csproj);

        Assert.Equal(new[] { "Company.Drivers", "Company.UpdateChecker" }, result.CompanyPackages);
    }

    [Fact]
    public void Scan_无Company包_返回空列表不报错()
    {
        var csproj = _assets.CreateStandardProject("PlainApp", "Newtonsoft.Json");

        var result = DependencyScanner.Scan(csproj);

        Assert.Empty(result.CompanyPackages);
    }

    [Fact]
    public void Scan_未显式配置RootNamespace_回退为工程文件名()
    {
        var csproj = _assets.CreateProject("MyTools",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n  <PropertyGroup>\r\n    <TargetFramework>net10.0</TargetFramework>\r\n  </PropertyGroup>\r\n</Project>");

        var result = DependencyScanner.Scan(csproj);

        Assert.Equal("MyTools", result.RootNamespace);
    }

    [Fact]
    public void Scan_显式配置RootNamespace_优先读取配置值()
    {
        var csproj = _assets.CreateProject("MyTools",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n  <PropertyGroup>\r\n    <RootNamespace>Company.B.Tools</RootNamespace>\r\n  </PropertyGroup>\r\n</Project>");

        var result = DependencyScanner.Scan(csproj);

        Assert.Equal("Company.B.Tools", result.RootNamespace);
    }

    public void Dispose() => _assets.Dispose();
}
```

- [ ] **Step 3: 跑测试确认 RED**

Run: `dotnet test tests\Company.Wire.Core.Tests --nologo`
Expected: 编译错误 CS0103（`DependencyScanner` 不存在）。

- [ ] **Step 4: 最小实现** `packages/csharp/Company.Wire.Core/DependencyScanner.cs`

```csharp
using System.Xml.Linq;

namespace Company.Wire.Core;

/// <summary>csproj 扫描器：提取 Company.* 依赖包名（向导据此自动生成依赖登记）与根命名空间。</summary>
public static class DependencyScanner
{
    public static ScanResult Scan(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);

        var packages = doc.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .Where(id => id.StartsWith("Company.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var rootNamespace = doc.Descendants("RootNamespace").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrEmpty(rootNamespace))
        {
            rootNamespace = Path.GetFileNameWithoutExtension(csprojPath);
        }

        return new ScanResult(packages, rootNamespace);
    }
}
```

- [ ] **Step 5: 跑测试确认 GREEN**

Run: `dotnet test tests\Company.Wire.Core.Tests --nologo`
Expected: 4 个 PASS。

- [ ] **Step 6: 提交**

```powershell
$msg = "接线核心库骨架 + DependencyScanner（Wire.Core，含测试资产助手）"; [IO.File]::WriteAllText("$env:TEMP\commit-msg.txt", $msg, (New-Object Text.UTF8Encoding $false)); git add -A; git commit -F "$env:TEMP\commit-msg.txt"
```

---

### Task 3: CsprojEditor —— 幂等加包引用（TDD）

**Files:**
- Create: `packages/csharp/Company.Wire.Core/CsprojEditor.cs`
- Test: `tests/Company.Wire.Core.Tests/CsprojEditorTests.cs`

**Interfaces:**
- Consumes: 无（纯 XML 操作）。
- Produces: `CsprojEditor.TryAddPackageReference(csprojPath, packageId, version) -> bool`（true=本次新增并写盘；false=已存在，未动文件）。修改文件前自动写 `.bak` 备份（备份路径 = 原文件全名 + `.bak`）。Task 6 的 WireRunner 依赖此行为。

- [ ] **Step 1: 写失败测试** `tests/Company.Wire.Core.Tests/CsprojEditorTests.cs`

```csharp
using Company.Wire.Core;
using Xunit;

namespace Company.Wire.Core.Tests;

/// <summary>CsprojEditor 行为规格：幂等添加 PackageReference，保留既有内容，改前备份。</summary>
public class CsprojEditorTests : IDisposable
{
    private readonly TestAssets _assets = new();

    [Fact]
    public void TryAdd_工程无该引用_添加成功并保留既有内容()
    {
        var csproj = _assets.CreateStandardProject("BClient", "Company.Drivers");

        var added = CsprojEditor.TryAddPackageReference(csproj, "Company.UpdateChecker", "1.2.0");

        Assert.True(added);
        var content = File.ReadAllText(csproj);
        Assert.Contains("Include=\"Company.UpdateChecker\" Version=\"1.2.0\"", content);
        Assert.Contains("Include=\"Company.Drivers\"", content);   // 既有引用不动
        Assert.Contains("net10.0", content);                        // 既有属性不动
    }

    [Fact]
    public void TryAdd_重复执行_返回false且文件字节不变（幂等）
    {
        var csproj = _assets.CreateStandardProject("BClient", "Company.Drivers");

        CsprojEditor.TryAddPackageReference(csproj, "Company.UpdateChecker", "1.2.0");
        var afterFirst = File.ReadAllText(csproj);

        var second = CsprojEditor.TryAddPackageReference(csproj, "Company.UpdateChecker", "9.9.9");

        Assert.False(second);                       // 已存在：不改版本、不重复加
        Assert.Equal(afterFirst, File.ReadAllText(csproj));
    }

    [Fact]
    public void TryAdd_修改前_自动生成bak备份()
    {
        var csproj = _assets.CreateStandardProject("BClient");
        var original = File.ReadAllText(csproj);

        CsprojEditor.TryAddPackageReference(csproj, "Company.UpdateChecker", "1.2.0");

        var bak = csproj + ".bak";
        Assert.True(File.Exists(bak));
        Assert.Equal(original, File.ReadAllText(bak));
    }

    [Fact]
    public void TryAdd_已有引用时不产生备份（未写盘）
    {
        var csproj = _assets.CreateStandardProject("BClient", "Company.UpdateChecker");

        var added = CsprojEditor.TryAddPackageReference(csproj, "Company.UpdateChecker", "1.2.0");

        Assert.False(added);
        Assert.False(File.Exists(csproj + ".bak"));
    }

    public void Dispose() => _assets.Dispose();
}
```

- [ ] **Step 2: 跑测试确认 RED**

Run: `dotnet test tests\Company.Wire.Core.Tests --nologo --filter "FullyQualifiedName~CsprojEditorTests"`
Expected: CS0103（`CsprojEditor` 不存在）。

- [ ] **Step 3: 最小实现** `packages/csharp/Company.Wire.Core/CsprojEditor.cs`

```csharp
using System.Xml.Linq;

namespace Company.Wire.Core;

/// <summary>
/// csproj 编辑器：以 XML API 幂等添加 PackageReference（绝不整文本替换，保留既有内容与格式）。
/// 修改前把原文件备份为 &lt;原文件&gt;.bak（回滚与安全底线，见 WireRunner.Rollback）。
/// </summary>
public static class CsprojEditor
{
    /// <summary>添加包引用。返回 true=本次新增；false=同名引用已存在（文件未动，幂等）。</summary>
    public static bool TryAddPackageReference(string csprojPath, string packageId, string version)
    {
        var doc = XDocument.Load(csprojPath);

        var exists = doc.Descendants("PackageReference")
            .Any(e => string.Equals((string?)e.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            return false;
        }

        // 无条件写备份：只在确有修改时才写盘，所以这里一定是改前状态
        File.Copy(csprojPath, csprojPath + ".bak", overwrite: true);

        var itemGroup = doc.Descendants("ItemGroup")
            .FirstOrDefault(g => g.Elements("PackageReference").Any());
        if (itemGroup is null)
        {
            itemGroup = new XElement("ItemGroup");
            doc.Root!.Add(itemGroup);
        }

        itemGroup.Add(new XElement("PackageReference",
            new XAttribute("Include", packageId),
            new XAttribute("Version", version)));

        doc.Save(csprojPath);
        return true;
    }
}
```

- [ ] **Step 4: 跑测试确认 GREEN**

Run: `dotnet test tests\Company.Wire.Core.Tests --nologo`
Expected: 全部 PASS（Task 2 的 4 个 + 本任务 4 个）。

- [ ] **Step 5: 提交**

```powershell
$msg = "CsprojEditor：XML API 幂等添加 PackageReference（改前 .bak 备份）"; [IO.File]::WriteAllText("$env:TEMP\commit-msg.txt", $msg, (New-Object Text.UTF8Encoding $false)); git add -A; git commit -F "$env:TEMP\commit-msg.txt"
```

---

### Task 4: StartupPatcher —— 三形态启动挂接（TDD）

**Files:**
- Create: `packages/csharp/Company.Wire.Core/StartupPatcher.cs`
- Test: `tests/Company.Wire.Core.Tests/StartupPatcherTests.cs`

**Interfaces:**
- Consumes: `UiKind`、`StartupPatchStatus/Result`（Task 2）。
- Produces: `StartupPatcher.Plan(projectDir, kind, rootNamespace) -> StartupPatchResult`（纯计算不写盘：`Status`+`TargetFile`+`Instructions`——`Downgraded` 时 Instructions 是给用户的精确复制粘贴指引；`Patched` 时 TargetFile 指向将改的文件）；`StartupPatcher.Apply(projectDir, kind, rootNamespace) -> StartupPatchResult`（写盘 + `.bak` 备份）。注入行与标记见 Global Constraints。Task 6/7 依赖二者（向导预览页调 Plan，执行调 Apply）。
- 挂接定位规则（测试即规格）：
  - **Console 顶级语句**：`Program.cs` 不含 `static` 方法签名 → 在首条非 `using`/非 `#`/非空行前插入 `await` 版注入块。
  - **Console 传统 Main**：`Program.cs` 含 `static ... Main` → 方法体第一行后插入 fire-and-forget 版。
  - **WinForms**：`Program.cs` 含 `Application.Run` → 该行前插入 fire-and-forget 版。
  - **WPF**：`App.xaml.cs` 含 `base.OnStartup` → 该行后插入 fire-and-forget 版。
  - 文件已含标记行 → `AlreadyPatched`；目标文件缺失/形态不认识 → `Downgraded`（给指引）。

- [ ] **Step 1: 写失败测试** `tests/Company.Wire.Core.Tests/StartupPatcherTests.cs`

```csharp
using Company.Wire.Core;
using Xunit;

namespace Company.Wire.Core.Tests;

/// <summary>StartupPatcher 行为规格：三种标准形态的启动挂接、幂等、备份与降级。</summary>
public class StartupPatcherTests : IDisposable
{
    private readonly TestAssets _assets = new();

    private string ProjectDir(string csprojPath) => Path.GetDirectoryName(csprojPath)!;

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
        Assert.Contains("// ===== Company UpdateCheck (auto-wired) =====", content);
        Assert.Contains("await BClient.UpdateCheck.UpdateCheckRunner.RunAsync();", content);
        // 注入点必须在首条业务语句之前
        Assert.True(content.IndexOf("RunAsync();") < content.IndexOf("Console.WriteLine"));
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
        Assert.Contains("_ = BClient.UpdateCheck.UpdateCheckRunner.RunAsync();", content);
        Assert.True(content.IndexOf("RunAsync();") < content.IndexOf("Application.Run"));
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
        Assert.Contains("_ = BClient.UpdateCheck.UpdateCheckRunner.RunAsync();", content);
        Assert.True(content.IndexOf("RunAsync();") > content.IndexOf("base.OnStartup"));
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
        Assert.Contains("_ = BClient.UpdateCheck.UpdateCheckRunner.RunAsync();", content);
        Assert.True(content.IndexOf("RunAsync();") < content.IndexOf("WriteLine"));
    }

    [Fact]
    public void Plan_已含标记_返回AlreadyPatched且不再计划改动()
    {
        var csproj = _assets.CreateStandardProject("BClient");
        var file = _assets.WriteSource(csproj, "Program.cs",
            "// ===== Company UpdateCheck (auto-wired) =====\r\n_ = BClient.UpdateCheck.UpdateCheckRunner.RunAsync();\r\nConsole.WriteLine(\"hello\");\r\n");
        var before = File.ReadAllText(file);

        var plan = StartupPatcher.Plan(ProjectDir(csproj), UiKind.Console, "BClient");

        Assert.Equal(StartupPatchStatus.AlreadyPatched, plan.Status);
        Assert.Equal(before, File.ReadAllText(file)); // Plan 本就不写盘
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
    public void Apply_重复执行_幂等不二次备份不重复插入()
    {
        var csproj = _assets.CreateStandardProject("BClient");
        _assets.WriteSource(csproj, "Program.cs", "Console.WriteLine(\"hello\");\r\n");
        StartupPatcher.Apply(ProjectDir(csproj), UiKind.Console, "BClient");
        var afterFirst = File.ReadAllText(Path.Combine(ProjectDir(csproj), "Program.cs"));

        var second = StartupPatcher.Apply(ProjectDir(csproj), UiKind.Console, "BClient");

        Assert.Equal(StartupPatchStatus.AlreadyPatched, second.Status);
        Assert.Equal(afterFirst, File.ReadAllText(Path.Combine(ProjectDir(csproj), "Program.cs")));
    }

    public void Dispose() => _assets.Dispose();
}
```

- [ ] **Step 2: 跑测试确认 RED**

Run: `dotnet test tests\Company.Wire.Core.Tests --nologo --filter "FullyQualifiedName~StartupPatcherTests"`
Expected: CS0103（`StartupPatcher` 不存在）。

- [ ] **Step 3: 最小实现** `packages/csharp/Company.Wire.Core/StartupPatcher.cs`

```csharp
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
        if (lines.Any(l => l.TrimStart().StartsWith(Marker.TrimStart(), StringComparison.Ordinal)))
        {
            return new StartupPatchResult(StartupPatchStatus.AlreadyPatched, file, null);
        }

        var (index, awaitCall) = kind switch
        {
            UiKind.Wpf => (IndexOfAfter(lines, "base.OnStartup"), false),
            UiKind.WinForms => (IndexOfBefore(lines, "Application.Run"), false),
            UiKind.Console => IsTopLevelStatements(lines)
                ? (IndexAfterDirectives(lines), true)
                : (IndexInsideMain(lines), false),
            _ => (-1, false),
        };

        if (index < 0)
        {
            return Downgraded(rootNamespace, $"已找到 {Path.GetFileName(file)} 但未识别出标准插入点：");
        }

        var newLines = BuildPatchedLines(lines, index, awaitCall, rootNamespace);
        if (write)
        {
            File.Copy(file, file + ".bak", overwrite: true);
            File.WriteAllLines(file, newLines, new UTF8Encoding(false));
        }
        else
        {
            // Plan：只产出"将要写入"的内容预览——写临时串而不是文件；此处用内存副本供向导预览
        }

        return new StartupPatchResult(StartupPatchStatus.Patched, file, null);
    }

    /// <summary>Plan 模式需要的预览内容：与 Apply 写盘内容一致的整文件文本（供向导差异预览）。</summary>
    public static string? PreviewContent(string projectDir, UiKind kind, string rootNamespace)
    {
        var result = Plan(projectDir, kind, rootNamespace);
        if (result.Status != StartupPatchStatus.Patched)
        {
            return null;
        }

        var lines = File.ReadAllLines(result.TargetFile!);
        var (index, awaitCall) = kind switch
        {
            UiKind.Wpf => (IndexOfAfter(lines, "base.OnStartup"), false),
            UiKind.WinForms => (IndexOfBefore(lines, "Application.Run"), false),
            _ => IsTopLevelStatements(lines) ? (IndexAfterDirectives(lines), true) : (IndexInsideMain(lines), false),
        };
        return string.Join(Environment.NewLine, BuildPatchedLines(lines, index, awaitCall, rootNamespace));
    }

    private static StartupPatchResult Downgraded(string rootNamespace, string reason)
    {
        var call = $"_ = {rootNamespace}.UpdateCheck.UpdateCheckRunner.RunAsync();";
        return new StartupPatchResult(StartupPatchStatus.Downgraded, null,
            reason + "请在程序启动处（Program.cs 的 Main / App.xaml.cs 的 OnStartup）手工加入这一行：\r\n" +
            $"{call}\r\n（控制台顶级语句程序可用 await 形式：await {rootNamespace}.UpdateCheck.UpdateCheckRunner.RunAsync();）");
    }

    private static List<string> BuildPatchedLines(string[] lines, int index, bool awaitCall, string rootNamespace)
    {
        var call = (awaitCall ? "await " : "_ = ") + rootNamespace + ".UpdateCheck.UpdateCheckRunner.RunAsync();";
        var indent = DetectIndent(lines, index);
        var result = lines.ToList();
        // index 语义：注入块插到该行之前（IndexOfAfter 类定位已把锚点行折算成"插到锚点行后"的等价下标）
        result.InsertRange(index, new[] { Marker, indent + call, "" });
        return result;
    }

    private static string DetectIndent(string[] lines, int index)
    {
        var line = lines[index < lines.Length ? index : lines.Length - 1];
        var indent = line.Length - line.TrimStart().Length;
        return new string(' ', Math.Max(indent, 0));
    }

    // ---- 定位：返回"插入点行下标"（注入块插到该行之前）----

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
```

- [ ] **Step 4: 跑测试确认 GREEN**

Run: `dotnet test tests\Company.Wire.Core.Tests --nologo`
Expected: 全部 PASS（含 Task 2/3）。若某个定位测试失败：修定位算法，不改测试。

- [ ] **Step 5: 提交**

```powershell
$msg = "StartupPatcher：三形态启动挂接（幂等 + .bak + 不认识即降级为指引）"; [IO.File]::WriteAllText("$env:TEMP\commit-msg.txt", $msg, (New-Object Text.UTF8Encoding $false)); git add -A; git commit -F "$env:TEMP\commit-msg.txt"
```

---

### Task 5: Templates + WiringGenerator —— 生成接线文件（TDD）

**Files:**
- Create: `packages/csharp/Company.Wire.Core/Templates.cs`
- Create: `packages/csharp/Company.Wire.Core/WiringGenerator.cs`
- Test: `tests/Company.Wire.Core.Tests/WiringGeneratorTests.cs`

**Interfaces:**
- Consumes: `UiKind`（Task 2）；模板中按名引用 `AssemblyVersions.FromAssemblyName`（Task 1）。
- Produces: `WiringGenerator.Generate(projectDir, rootNamespace, uiKind, manifestUrl, dependencies) -> IReadOnlyList<string>`（生成文件的绝对路径）。固定生成 3 个文件到 `<工程目录>\UpdateCheck\`：`UpdateCheckService.cs`（UI 无关核心）、`UpdateCheckRunner.cs`（按形态：控制台打印 / WinForms+WPF 弹窗，critical 用警示图标）、`接线说明.md`。生成文件属向导所有，重复执行直接覆盖。Task 6/7 依赖此行为。

- [ ] **Step 1: 写失败测试** `tests/Company.Wire.Core.Tests/WiringGeneratorTests.cs`

```csharp
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
        Assert.Contains("MessageBoxIcon.Warning", runner);   // critical 强提示
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
```

- [ ] **Step 2: 跑测试确认 RED**

Run: `dotnet test tests\Company.Wire.Core.Tests --nologo --filter "FullyQualifiedName~WiringGeneratorTests"`
Expected: CS0103（`WiringGenerator` 不存在）。

- [ ] **Step 3: 实现** `packages/csharp/Company.Wire.Core/Templates.cs`（生成源码的模板常量，用 `__NS__`/`__MANIFEST_URL__`/`__DEPS__` 占位符 + Replace，不用插值避免转义地狱）

```csharp
namespace Company.Wire.Core;

/// <summary>接线源码模板（占位符：__NS__ 根命名空间 / __MANIFEST_URL__ 清单地址 / __DEPS__ 依赖登记数组项）。</summary>
internal static class Templates
{
    public const string Service = """
        using Company.UpdateChecker;

        namespace __NS__.UpdateCheck;

        /// <summary>
        /// 公共库更新检查接线（由 Company.UpdateChecker.Setup 向导生成，可直接修改）。
        /// 检测失败自动降级，绝不影响本程序正常运行。
        /// </summary>
        public static class UpdateCheckService
        {
            /// <summary>版本清单地址（内外网可达的只读 HTTP 地址，切换环境时只改这里）。</summary>
            public const string ManifestUrl = "__MANIFEST_URL__";

            /// <summary>本项目使用的公共包登记：版本从程序集自动读取，升级 NuGet 包重新编译后自动感知。</summary>
            public static readonly PackageDependency[] Dependencies =
            {
        __DEPS__
            };

            /// <summary>执行一次更新检查。清单不可达等异常由 Runner 消化（现场设备不允许因检查罢工）。</summary>
            public static async Task<IReadOnlyList<PackageUpdate>> CheckAsync()
            {
                var source = new HttpManifestSource(
                    ManifestUrl,
                    Environment.GetEnvironmentVariable("UPDATE_MANIFEST_TOKEN"));
                return await new UpdateChecker(source).CheckAsync(Dependencies);
            }
        }
        """;

    public const string RunnerConsole = """
        using Company.UpdateChecker;

        namespace __NS__.UpdateCheck;

        /// <summary>控制台形态：检测结果直接打印到控制台（由向导生成）。</summary>
        public static class UpdateCheckRunner
        {
            public static async Task RunAsync()
            {
                try
                {
                    var updates = await UpdateCheckService.CheckAsync();
                    if (updates.Count == 0)
                    {
                        Console.WriteLine("  [检查结果] 所有公共包均为最新版本。");
                        return;
                    }

                    Console.WriteLine("  [检查结果] 检测到公共库有新版本！");
                    foreach (var u in updates)
                    {
                        Console.WriteLine($"  │ {u.PackageId} {u.CurrentVersion} -> {u.LatestVersion}" +
                            $"{(u.IsCritical ? "  !! 重要更新" : "")}");
                        Console.WriteLine($"  │ 变更：{u.ReleaseNotes ?? "（无说明）"}");
                        foreach (var a in u.Artifacts)
                        {
                            Console.WriteLine($"  │ 下载：[{a.Type}] {a.Url}");
                        }
                    }
                }
                catch (UpdateManifestException)
                {
                    // 清单不可达：静默降级，不影响程序运行
                }
            }
        }
        """;

    public const string RunnerWinForms = """
        using Company.UpdateChecker;

        namespace __NS__.UpdateCheck;

        /// <summary>WinForms 形态：有更新时弹窗提示（critical 用黄色警示图标；由向导生成）。</summary>
        public static class UpdateCheckRunner
        {
            public static async Task RunAsync()
            {
                try
                {
                    var updates = await UpdateCheckService.CheckAsync();
                    if (updates.Count == 0)
                    {
                        return;
                    }

                    var critical = updates.Any(u => u.IsCritical);
                    var text = string.Join(Environment.NewLine + Environment.NewLine, updates.Select(u =>
                        $"{u.PackageId} {u.CurrentVersion} -> {u.LatestVersion}" +
                        (u.IsCritical ? "  !! 重要更新" : "") + Environment.NewLine +
                        $"{u.ReleaseNotes ?? ""}"));
                    System.Windows.Forms.MessageBox.Show(
                        "检测到公共库有新版本：" + Environment.NewLine + Environment.NewLine + text,
                        critical ? "公共库重要更新" : "公共库更新",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        critical
                            ? System.Windows.Forms.MessageBoxIcon.Warning
                            : System.Windows.Forms.MessageBoxIcon.Information);
                }
                catch (UpdateManifestException)
                {
                    // 清单不可达：静默降级，不影响程序运行
                }
            }
        }
        """;

    public const string RunnerWpf = """
        using Company.UpdateChecker;

        namespace __NS__.UpdateCheck;

        /// <summary>WPF 形态：有更新时弹窗提示（critical 用警示图标；由向导生成）。</summary>
        public static class UpdateCheckRunner
        {
            public static async Task RunAsync()
            {
                try
                {
                    var updates = await UpdateCheckService.CheckAsync();
                    if (updates.Count == 0)
                    {
                        return;
                    }

                    var critical = updates.Any(u => u.IsCritical);
                    var text = string.Join(Environment.NewLine + Environment.NewLine, updates.Select(u =>
                        $"{u.PackageId} {u.CurrentVersion} -> {u.LatestVersion}" +
                        (u.IsCritical ? "  !! 重要更新" : "") + Environment.NewLine +
                        $"{u.ReleaseNotes ?? ""}"));
                    System.Windows.MessageBox.Show(
                        "检测到公共库有新版本：" + Environment.NewLine + Environment.NewLine + text,
                        critical ? "公共库重要更新" : "公共库更新",
                        System.Windows.MessageBoxButton.OK,
                        critical
                            ? System.Windows.MessageBoxImage.Warning
                            : System.Windows.MessageBoxImage.Information);
                }
                catch (UpdateManifestException)
                {
                    // 清单不可达：静默降级，不影响程序运行
                }
            }
        }
        """;

    public const string Readme = """
        # 更新检查接线说明（由 Company.UpdateChecker.Setup 向导生成）

        本目录由接线向导生成，包含三部分：

        - `UpdateCheckService.cs`：依赖登记 + 清单地址 + 检查逻辑（核心，UI 无关）
        - `UpdateCheckRunner.cs`：提示形态（控制台打印 / 弹窗）
        - 本说明

        ## 启动调用

        向导已在程序入口自动挂接（见 csproj 同级 Program.cs / App.xaml.cs 中带
        `// ===== Company UpdateCheck (auto-wired) =====` 标记的代码块）。
        若向导提示"降级为指引"，请手工把这一行加到程序启动处：

        ```
        _ = __NS__.UpdateCheck.UpdateCheckRunner.RunAsync();
        ```

        （控制台顶级语句程序可用 `await __NS__.UpdateCheck.UpdateCheckRunner.RunAsync();`）

        ## 私有清单令牌

        私有清单地址需要 bearer 令牌时，从**用户环境变量** `UPDATE_MANIFEST_TOKEN` 读取
        （不进代码、不进仓库）。向导可自动配置；手工配置：

        ```powershell
        setx UPDATE_MANIFEST_TOKEN "<你的令牌>"
        ```

        ## 升级纪律（PR 检查单）

        1. 本目录代码可以按项目需要修改，但提交前先确认与公共库模板没有冲突性偏差
        2. 依赖登记自动跟随 NuGet 包引用（升级包重新编译即生效），不要手工写版本号
        3. 改动一律走 PR 审批合并，不直推 main
        """;
}
```

- [ ] **Step 4: 实现** `packages/csharp/Company.Wire.Core/WiringGenerator.cs`

```csharp
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

        var deps = string.Join(",\n", dependencies.Select(d =>
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
```

- [ ] **Step 5: 跑测试确认 GREEN**

Run: `dotnet test tests\Company.Wire.Core.Tests --nologo`
Expected: 全部 PASS（Task 2/3/4/5 合计 21 个）。

- [ ] **Step 6: 提交**

```powershell
$msg = "WiringGenerator + 模板：生成 UpdateCheck 接线目录（服务/Runner/说明，critical 警示图标）"; [IO.File]::WriteAllText("$env:TEMP\commit-msg.txt", $msg, (New-Object Text.UTF8Encoding $false)); git add -A; git commit -F "$env:TEMP\commit-msg.txt"
```

---

### Task 6: WireRunner —— 编排 + 回滚（TDD）

**Files:**
- Create: `packages/csharp/Company.Wire.Core/WireRunner.cs`
- Test: `tests/Company.Wire.Core.Tests/WireRunnerTests.cs`

**Interfaces:**
- Consumes: Task 2~5 全部组件；`WireOptions`/`WireResult`/`BackupEntry`（Task 2）。
- Produces: `WireRunner.Run(options, log) -> WireResult`（编排：扫描→登记依赖（自动补 `Company.UpdateChecker`）→生成→csproj→挂接（AutoAttach=false 时给指引）；所有改动的文件记入 `Backups`）；`WireRunner.Rollback(result)`（删生成文件 + 从 .bak 还原被改文件）。Task 7 向导的执行页调 Run/Rollback。

- [ ] **Step 1: 写失败测试** `tests/Company.Wire.Core.Tests/WireRunnerTests.cs`

```csharp
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
        var originalCsproj = File.ReadAllText(csproj);
        var logs = new List<string>();

        var result = WireRunner.Run(new WireOptions(csproj, UiKind.Console, Manifest, AutoAttach: true),
            line => logs.Add(line));

        Assert.Equal(new[] { "Company.Drivers" }, result.ScannedPackages);
        Assert.Equal(3, result.GeneratedFiles.Count);
        Assert.True(result.CsprojModified);
        Assert.Equal(StartupPatchStatus.Patched, result.StartupPatch.Status);
        Assert.Contains(csproj + ".bak", result.Backups.Select(b => b.Backup));          // csproj 备份
        Assert.Contains(program + ".bak", result.Backups.Select(b => b.Backup));         // 挂接文件备份
        Assert.Contains("RunAsync();", File.ReadAllText(program));                        // 真挂上了
        Assert.Contains("Company.UpdateChecker", File.ReadAllText(csproj));               // 引用加上了
        Assert.Contains("扫描", logs[0]);                                                 // 日志从扫描开始
    }

    [Fact]
    public void Run_依赖未引UpdateChecker_自动补进登记集合()
    {
        var csproj = _assets.CreateStandardProject("BClient", "Company.Drivers");

        var result = WireRunner.Run(new WireOptions(csproj, UiKind.Console, Manifest, AutoAttach: true));

        var service = File.ReadAllText(Path.Combine(ProjectDir(csproj), "UpdateCheck", "UpdateCheckService.cs"));
        Assert.Contains("\"Company.UpdateChecker\", AssemblyVersions.FromAssemblyName(\"Company.UpdateChecker\")", service);
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
        Assert.False(File.Exists(program + ".bak"));                                     // 备份清理
        Assert.False(File.Exists(csproj + ".bak"));
    }

    public void Dispose() => _assets.Dispose();
}
```

- [ ] **Step 2: 跑测试确认 RED**

Run: `dotnet test tests\Company.Wire.Core.Tests --nologo --filter "FullyQualifiedName~WireRunnerTests"`
Expected: CS0103（`WireRunner` 不存在）。

- [ ] **Step 3: 实现** `packages/csharp/Company.Wire.Core/WireRunner.cs`

```csharp
namespace Company.Wire.Core;

/// <summary>
/// 接线编排：扫描 → 生成接线文件 → csproj 幂等加引用 → 启动挂接（可关）。
/// 所有被修改的文件先备份（Backups 供向导展示与 Rollback）。
/// </summary>
public static class WireRunner
{
    public static WireResult Run(WireOptions options, Action<string>? log = null)
    {
        log?.Invoke($"扫描 {Path.GetFileName(options.ProjectPath)} ...");
        var scan = DependencyScanner.Scan(options.ProjectPath);
        log?.Invoke(scan.CompanyPackages.Count == 0
            ? "未发现 Company.* 依赖，将只登记 UpdateChecker 自身"
            : $"发现公共包依赖：{string.Join(", ", scan.CompanyPackages)}");

        // 登记集合 = 扫描结果 + UpdateChecker 自身（去重）
        var deps = new List<string>(scan.CompanyPackages);
        if (!deps.Contains("Company.UpdateChecker", StringComparer.OrdinalIgnoreCase))
        {
            deps.Add("Company.UpdateChecker");
        }

        var projectDir = Path.GetDirectoryName(options.ProjectPath)!;
        log?.Invoke("生成接线文件（UpdateCheck/）...");
        var generated = WiringGenerator.Generate(
            projectDir, scan.RootNamespace, options.UiKind, options.ManifestUrl, deps);

        log?.Invoke("为 csproj 添加 Company.UpdateChecker 引用...");
        var csprojModified = CsprojEditor.TryAddPackageReference(
            options.ProjectPath, "Company.UpdateChecker", options.UpdateCheckerVersion);

        var backups = new List<BackupEntry>();
        if (csprojModified)
        {
            backups.Add(new BackupEntry(options.ProjectPath, options.ProjectPath + ".bak"));
            log?.Invoke("csproj 已更新（原文件备份为 .bak）");
        }
        else
        {
            log?.Invoke("csproj 已含该引用，跳过");
        }

        StartupPatchResult patch;
        if (options.AutoAttach)
        {
            log?.Invoke("挂接启动调用...");
            patch = StartupPatcher.Apply(projectDir, options.UiKind, scan.RootNamespace);
            if (patch.Status == StartupPatchStatus.Patched && patch.TargetFile is not null)
            {
                backups.Add(new BackupEntry(patch.TargetFile, patch.TargetFile + ".bak"));
                log?.Invoke($"已挂接：{Path.GetFileName(patch.TargetFile)}（原文件备份为 .bak）");
            }
            else if (patch.Status == StartupPatchStatus.AlreadyPatched)
            {
                log?.Invoke("入口已含挂接标记，跳过");
            }
            else
            {
                log?.Invoke("未识别出标准插入点，已降级为手工指引（绝不盲改代码）");
            }
        }
        else
        {
            var plan = StartupPatcher.Plan(projectDir, options.UiKind, scan.RootNamespace);
            patch = plan.Status == StartupPatchStatus.AlreadyPatched
                ? plan
                : new StartupPatchResult(StartupPatchStatus.Downgraded, null,
                    "未开启自动挂接。请在程序启动处手工加入：\r\n" +
                    $"_ = {scan.RootNamespace}.UpdateCheck.UpdateCheckRunner.RunAsync();");
            log?.Invoke("自动挂接已关闭，生成手工指引");
        }

        log?.Invoke("接线完成。");
        return new WireResult(scan.CompanyPackages, generated, csprojModified, patch, backups);
    }

    /// <summary>回滚一次接线：删除生成文件，从 .bak 还原被修改文件（向导冒烟失败时调用）。</summary>
    public static void Rollback(WireResult result)
    {
        foreach (var file in result.GeneratedFiles)
        {
            if (File.Exists(file)) { File.Delete(file); }
        }

        foreach (var backup in result.Backups)
        {
            if (File.Exists(backup.Backup))
            {
                File.Copy(backup.Backup, backup.Original, overwrite: true);
                File.Delete(backup.Backup);
            }
        }

        // 生成目录若已空则一并清掉
        foreach (var file in result.GeneratedFiles)
        {
            var dir = Path.GetDirectoryName(file);
            if (dir is not null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
    }
}
```

- [ ] **Step 4: 跑全量测试确认 GREEN**

Run: `dotnet test --nologo`
Expected: 全仓全绿（含 UpdateChecker 39 + Wire.Core 25 + Drivers）。

- [ ] **Step 5: 提交**

```powershell
$msg = "WireRunner：接线编排（扫描→生成→csproj→挂接）+ 失败回滚"; [IO.File]::WriteAllText("$env:TEMP\commit-msg.txt", $msg, (New-Object Text.UTF8Encoding $false)); git add -A; git commit -F "$env:TEMP\commit-msg.txt"
```

---

### Task 7: WinForms 向导壳（构建冒烟验证，规格 §5 豁免 TDD）

**Files:**
- Create: `packages/csharp/Company.UpdateChecker.Setup/Company.UpdateChecker.Setup.csproj`
- Create: `packages/csharp/Company.UpdateChecker.Setup/Program.cs`
- Create: `packages/csharp/Company.UpdateChecker.Setup/WizardForm.cs`

**Interfaces:**
- Consumes: `WireRunner.Run/Rollback`、`StartupPatcher.PreviewContent/Plan`、`DependencyScanner.Scan`、`UiKind`（Task 2~6）。
- Produces: 可发布 WinForms exe（Task 9 的 CI 发布对象）。

- [ ] **Step 1: csproj + Program.cs**

`Company.UpdateChecker.Setup.csproj`（覆写根 Directory.Build.props 的 TFM；不 pack 只 publish）：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <IsPackable>false</IsPackable>
    <Version>1.0.0</Version>
    <Description>公共库接线安装向导：双击为既有 C# 项目接入程序内更新检查（扫描依赖、生成接线文件、自动挂接、冒烟验证、失败回滚）。</Description>
    <PackageTags>setup;wizard;update-checker</PackageTags>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Company.Wire.Core\Company.Wire.Core.csproj" />
  </ItemGroup>
</Project>
```

`Program.cs`：
```csharp
namespace Company.UpdateChecker.Setup;

internal static class Program
{
    /// <summary>WinForms 向导入口。net10 SDK 自动生成 ApplicationConfiguration.Initialize()。</summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new WizardForm());
    }
}
```

- [ ] **Step 2: WizardForm.cs**（5 页：欢迎/选项目/选项/预览与执行/完成；布局对齐规格参考样式：左侧品牌栏+右侧内容+底部按钮）

```csharp
using System.Diagnostics;
using Company.Wire.Core;

namespace Company.UpdateChecker.Setup;

/// <summary>5 页安装向导：欢迎 → 选择项目 → 选项 → 预览与执行 → 完成。壳只做 UI，逻辑全在 Wire.Core。</summary>
public sealed class WizardForm : Form
{
    private const string DefaultManifest =
        "https://raw.githubusercontent.com/1159792767/shared-library/main/tools/manifest/manifest.json";
    private const string UpdateCheckerVersion = "1.2.0";

    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = new Padding(24) };
    private readonly Button _back = new() { Text = "< 上一步", Width = 100 };
    private readonly Button _next = new() { Text = "下一步 >", Width = 100 };
    private readonly Panel[] _pages = new Panel[5];
    private int _pageIndex;

    private readonly TextBox _projectPath = new() { Width = 420, ReadOnly = true };
    private readonly RadioButton _uiConsole = new() { Text = "控制台程序", Checked = true, AutoSize = true };
    private readonly RadioButton _uiWinForms = new() { Text = "WinForms 程序", AutoSize = true };
    private readonly RadioButton _uiWpf = new() { Text = "WPF 程序", AutoSize = true };
    private readonly TextBox _manifestUrl = new() { Width = 420, Text = DefaultManifest };
    private readonly CheckBox _autoAttach = new() { Text = "自动挂接启动调用（预览差异 + .bak 备份；不认识的标准形态以外的代码绝不盲改）", Checked = true, AutoSize = true };
    private readonly CheckBox _configToken = new() { Text = "为本机配置清单访问令牌（从 Git 凭据自动获取，写入用户环境变量 UPDATE_MANIFEST_TOKEN）", AutoSize = true };

    private readonly TextBox _preview = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F), WordWrap = false };
    private readonly TextBox _runLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F), Visible = false };
    private readonly Button _execute = new() { Text = "确认执行", Width = 110 };
    private readonly Label _doneSummary = new() { Dock = DockStyle.Fill, Text = "" };

    private WireResult? _lastResult;

    public WizardForm()
    {
        Text = "公共库更新检查 · 接线向导";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(780, 540);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        var banner = new Panel { Dock = DockStyle.Left, Width = 180, BackColor = Color.FromArgb(0, 99, 177) };
        banner.Controls.Add(new Label
        {
            Text = "公共库\r\n\r\n接线向导",
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 13F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 140,
            TextAlign = ContentAlignment.MiddleCenter,
        });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        buttons.Controls.Add(new Button { Text = "取消", Width = 100, DialogResult = DialogResult.Cancel });
        buttons.Controls.Add(_next);
        buttons.Controls.Add(_back);

        _pages[0] = BuildWelcomePage();
        _pages[1] = BuildSelectPage();
        _pages[2] = BuildOptionsPage();
        _pages[3] = BuildExecutePage();
        _pages[4] = BuildDonePage();
        foreach (var page in _pages) { page.Dock = DockStyle.Fill; page.Visible = false; _content.Controls.Add(page); }

        Controls.Add(_content);
        Controls.Add(buttons);
        Controls.Add(banner);

        _back.Click += (s, e) => ShowPage(_pageIndex - 1);
        _next.Click += (s, e) => OnNext();
        _execute.Click += async (s, e) => await RunWiringAsync();

        ShowPage(0);
    }

    private static Panel Page(params Control[] controls)
    {
        var page = new Panel();
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        foreach (var c in controls) { stack.Controls.Add(c); }
        page.Controls.Add(stack);
        return page;
    }

    private Panel BuildWelcomePage() => Page(
        new Label { Text = "欢迎使用公共库接线向导", Font = new Font("微软雅黑", 14F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 8, 0, 16) },
        new Label
        {
            Text = "向导将为你选择的项目接入\"公共库更新检查\"：\r\n\r\n" +
                "  1. 自动扫描项目已引用的 Company.* 公共包并生成依赖登记\r\n" +
                "  2. 生成接线代码（检测失败不影响程序运行）\r\n" +
                "  3. 为 csproj 幂等添加 Company.UpdateChecker 引用\r\n" +
                "  4. 自动挂接启动调用（可预览差异；改前自动备份 .bak）\r\n" +
                "  5. 编译冒烟验证，失败自动回滚\r\n\r\n" +
                "建议先提交当前代码，再开始接线。点击\"下一步\"继续。",
            AutoSize = true,
        });

    private Panel BuildSelectPage()
    {
        var browse = new Button { Text = "浏览...", Width = 90 };
        browse.Click += (s, e) =>
        {
            using var dialog = new OpenFileDialog { Filter = "C# 项目文件 (*.csproj)|*.csproj" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _projectPath.Text = dialog.FileName;
            }
        };
        return Page(
            new Label { Text = "选择要接线的项目", Font = new Font("微软雅黑", 12F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 8, 0, 16) },
            new Label { Text = "项目文件（.csproj）：", AutoSize = true },
            Row(_projectPath, browse),
            new Label { Text = "向导只修改该项目目录内的文件。", AutoSize = true, ForeColor = Color.DimGray });
    }

    private Panel BuildOptionsPage() => Page(
        new Label { Text = "接线选项", Font = new Font("微软雅黑", 12F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 8, 0, 16) },
        new Label { Text = "程序形态（决定提示样式与挂接位置）：", AutoSize = true },
        Row(_uiConsole, _uiWinForms, _uiWpf),
        new Label { Text = "版本清单地址：", AutoSize = true, Margin = new Padding(0, 12, 0, 0) },
        _manifestUrl,
        new Label { Text = "", AutoSize = true, Height = 8 },
        _autoAttach,
        _configToken);

    private Panel BuildExecutePage() => Page(
        new Label { Text = "预览将要执行的修改", Font = new Font("微软雅黑", 12F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 8, 0, 8) },
        _preview,
        _runLog,
        _execute);

    private Panel BuildDonePage()
    {
        var again = new Button { Text = "再接下一个项目", Width = 130 };
        again.Click += (s, e) => { Reset(); ShowPage(1); };
        return Page(_doneSummary, new Label { Text = "", AutoSize = true, Height = 12 }, again);
    }

    private static Panel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0) };
        foreach (var c in controls) { row.Controls.Add(c); }
        return row;
    }

    private UiKind SelectedUiKind => _uiWinForms.Checked ? UiKind.WinForms
        : _uiWpf.Checked ? UiKind.Wpf
        : UiKind.Console;

    private void ShowPage(int index)
    {
        _pageIndex = index;
        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
        }

        _back.Visible = index is > 0 and < 3;
        _next.Visible = index is < 3 and not 3;
        _next.Enabled = index switch
        {
            1 => File.Exists(_projectPath.Text) && _projectPath.Text.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
        _next.Text = index == 2 ? "预览 >" : "下一步 >";
        _execute.Visible = index == 3;
        if (index == 3)
        {
            _preview.Visible = true;
            _runLog.Visible = false;
            _preview.Text = BuildPreviewText();
        }
    }

    private void OnNext()
    {
        if (_pageIndex == 1 && !File.Exists(_projectPath.Text))
        {
            MessageBox.Show("请先选择有效的 .csproj 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ShowPage(_pageIndex + 1);
    }

    private string BuildPreviewText()
    {
        var csproj = _projectPath.Text;
        var dir = Path.GetDirectoryName(csproj)!;
        var scan = DependencyScanner.Scan(csproj);
        var lines = new List<string>
        {
            "将执行以下修改：",
            "",
            "1) 生成文件（UpdateCheck/ 目录）",
            "   UpdateCheck/UpdateCheckService.cs   依赖登记 + 清单地址 + 检查逻辑",
            "   UpdateCheck/UpdateCheckRunner.cs    " + SelectedUiKind switch { UiKind.WinForms => "WinForms 弹窗提示", UiKind.Wpf => "WPF 弹窗提示", _ => "控制台打印提示" },
            "   UpdateCheck/接线说明.md",
            "",
            "2) csproj（原文件备份 .bak）",
            "   " + (File.ReadAllText(csproj).Contains("Company.UpdateChecker") ? "已含 Company.UpdateChecker 引用，跳过" : $"添加 <PackageReference Include=\"Company.UpdateChecker\" Version=\"{UpdateCheckerVersion}\" />"),
            "",
        };

        if (_autoAttach.Checked)
        {
            var preview = StartupPatcher.PreviewContent(dir, SelectedUiKind, scan.RootNamespace);
            if (preview is null)
            {
                var plan = StartupPatcher.Plan(dir, SelectedUiKind, scan.RootNamespace);
                lines.Add("3) 启动挂接：" + (plan.Status == StartupPatchStatus.AlreadyPatched ? "已挂接过，跳过" : "未识别出标准插入点，将降级为手工指引"));
                if (plan.Instructions is not null) { lines.AddRange(plan.Instructions.Split("\r\n").Select(l => "   " + l)); }
            }
            else
            {
                lines.Add("3) 启动挂接（改前备份 .bak），挂接后的文件全文预览：");
                lines.Add(preview);
            }
        }
        else
        {
            lines.Add("3) 启动挂接：未开启，完成后提供手工指引");
        }

        lines.Add("");
        lines.Add("确认无误后点击\"确认执行\"。");
        return string.Join(Environment.NewLine, lines);
    }

    private async Task RunWiringAsync()
    {
        var csproj = _projectPath.Text;
        _preview.Visible = false;
        _runLog.Visible = true;
        _runLog.Text = "";
        _execute.Enabled = false;
        _back.Visible = false;

        void Log(string line) => BeginInvoke(() => { _runLog.AppendText(line + Environment.NewLine); });
        Log("> 开始接线");
        if (_configToken.Checked)
        {
            Log("> 配置清单令牌...");
            ConfigureToken(Log);
        }

        var options = new WireOptions(csproj, SelectedUiKind, _manifestUrl.Text.Trim(), _autoAttach.Checked, UpdateCheckerVersion);
        WireResult result = await Task.Run(() => WireRunner.Run(options, Log));
        _lastResult = result;

        Log("> 编译冒烟验证...");
        var (ok, buildOutput) = await Task.Run(() => RunDotnetBuild(csproj));
        Log(buildOutput);
        if (!ok)
        {
            Log("> 冒烟失败，自动回滚本次修改...");
            WireRunner.Rollback(result);
            Log("> 已回滚：生成文件已删除，被修改文件已从 .bak 还原。请把上方日志发给公共库维护者。");
        }
        else
        {
            Log("> 冒烟通过。");
        }

        BeginInvoke(() =>
        {
            _doneSummary.Text = BuildDoneText(ok, result);
            ShowPage(4);
            _next.Text = "完成";
            _next.Visible = true;
        });
    }

    private string BuildDoneText(bool buildOk, WireResult result) =>
        (buildOk ? "接线完成！" : "接线已回滚（冒烟未通过）。") + "\r\n\r\n" +
        $"  扫描到的公共包：{(result.ScannedPackages.Count == 0 ? "（无）" : string.Join(", ", result.ScannedPackages))}\r\n" +
        $"  csproj：{(result.CsprojModified ? "已添加 Company.UpdateChecker 引用" : "已含引用，未改动")}\r\n" +
        $"  启动挂接：{result.StartupPatch.Status switch { StartupPatchStatus.Patched => "已自动挂接", StartupPatchStatus.AlreadyPatched => "此前已挂接", _ => "未自动挂接——请按下方指引手工加入一行" }}\r\n\r\n" +
        (result.StartupPatch.Instructions is null ? "" : "手工指引：\r\n" + result.StartupPatch.Instructions + "\r\n\r\n") +
        "后续：把改动（csproj / UpdateCheck/ / 入口文件）提交并走 PR 审批合并。";

    private static (bool Ok, string Output) RunDotnetBuild(string csproj)
    {
        var info = new ProcessStartInfo("dotnet", $"build \"{csproj}\" --nologo -v quiet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode == 0, output.Trim());
    }

    /// <summary>从 Git 凭据取 GitHub 令牌写入用户环境变量（私有清单 bearer 鉴权用；失败仅提示不中断）。</summary>
    private void ConfigureToken(Action<string> log)
    {
        try
        {
            var info = new ProcessStartInfo("git", "credential fill")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(info)!;
            process.StandardInput.Write("protocol=https\nhost=github.com\n\n");
            process.StandardInput.Close();
            var cred = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var token = cred.Split('\n').FirstOrDefault(l => l.StartsWith("password="))?["password=".Length..];
            if (string.IsNullOrEmpty(token))
            {
                log("    未取到 Git 凭据（可能未在本机登录 GitHub）：跳过，可稍后手工 setx UPDATE_MANIFEST_TOKEN");
                return;
            }

            Environment.SetEnvironmentVariable("UPDATE_MANIFEST_TOKEN", token, EnvironmentVariableTarget.User);
            log("    已写入用户环境变量 UPDATE_MANIFEST_TOKEN（重开 VS 后生效）");
        }
        catch (Exception ex)
        {
            log("    配置令牌失败（不影响接线）：" + ex.Message);
        }
    }

    private void Reset()
    {
        _projectPath.Text = "";
        _manifestUrl.Text = DefaultManifest;
        _autoAttach.Checked = true;
        _configToken.Checked = false;
        _uiConsole.Checked = true;
        _execute.Enabled = true;
        _lastResult = null;
    }
}
```

- [ ] **Step 3: 构建冒烟验证**

Run: `dotnet build packages\csharp\Company.UpdateChecker.Setup --nologo` → 期望 0 错误 0 警告。
Run: `dotnet test --nologo`（仓库根）→ 全绿（确认测试发现不受 WinForms 工程影响）。

- [ ] **Step 4: 提交**

```powershell
$msg = "接线向导 WinForms 壳：5 页向导（预览差异/冒烟验证/失败回滚/令牌配置）"; [IO.File]::WriteAllText("$env:TEMP\commit-msg.txt", $msg, (New-Object Text.UTF8Encoding $false)); git add -A; git commit -F "$env:TEMP\commit-msg.txt"
```

---

### Task 8: 本机端到端验证（真实接线 → 编译 → 收到 2.5.0 提示）

**Files:** 无仓库改动（全部在 %TEMP% 验证场，属验证步骤）。

**Interfaces:**
- Consumes: Task 1~7 全部产物；真实本地 feed `tools\nuget-feed`；真实远端清单（2.5.0）。
- Produces: 端到端证据（向导代码路径 = WireRunner 同一条；GUI 手工冒烟留给用户双击 exe）。

- [ ] **Step 1: 本地 feed 备好 UpdateChecker 1.2.0**

Run: `dotnet pack packages\csharp\Company.UpdateChecker\Company.UpdateChecker.csproj -o tools\nuget-feed -p:Version=1.2.0 --nologo`
Expected: `tools\nuget-feed` 出现 `Company.UpdateChecker.1.2.0.nupkg`。

- [ ] **Step 2: 搭端到端验证场（project-demo 副本 + 驱动器）**

```powershell
$e2e = Join-Path $env:TEMP ("wire-e2e-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $e2e | Out-Null
Copy-Item -Recurse "src\..\..\project-demo\src\DemoApp" "$e2e\DemoApp"
Remove-Item "$e2e\DemoApp\packages.lock.json" -ErrorAction SilentlyContinue   # 副本不带锁文件，等同未接线项目
# 副本依赖压回 2.4.0：制造"落后于清单 2.5.0"的真实场景
(Get-Content "$e2e\DemoApp\DemoApp.csproj" -Raw -Encoding UTF8) -replace 'Company.Drivers" Version="[\d.]+"', 'Company.Drivers" Version="2.4.0"' | Set-Content "$e2e\DemoApp\DemoApp.csproj" -Encoding UTF8
# 副本 nuget.config：指向真实本地 feed（绝对路径，绕开相对路径）
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="company-local-feed" value="REPO_FEED" />
  </packageSources>
</configuration>
'@ -replace 'REPO_FEED', ("c:\Users\帅b\Desktop\公共库项目库\shared-library\tools\nuget-feed") | Set-Content "$e2e\nuget.config"
New-Item -ItemType Directory -Path "$e2e\Driver" | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="REPO_WIRE" />
  </ItemGroup>
</Project>
'@ -replace 'REPO_WIRE', ("c:\Users\帅b\Desktop\公共库项目库\shared-library\packages\csharp\Company.Wire.Core\Company.Wire.Core.csproj") | Set-Content "$e2e\Driver\Driver.csproj"
@'
using Company.Wire.Core;

var options = new WireOptions(
    args[0], UiKind.Console,
    "https://raw.githubusercontent.com/1159792767/shared-library/main/tools/manifest/manifest.json",
    AutoAttach: true);
var result = WireRunner.Run(options, line => Console.WriteLine("[wire] " + line));
Console.WriteLine($"[wire] 生成 {result.GeneratedFiles.Count} 文件 | csproj改={result.CsprojModified} | 挂接={result.StartupPatch.Status}");
'@ | Set-Content "$e2e\Driver\Program.cs"
"e2e-dir=$e2e"
```

- [ ] **Step 3: 跑驱动器接线副本，然后编译运行副本**

```powershell
$e2e = "<上一步输出的 e2e-dir>"
dotnet run --project "$e2e\Driver\Driver.csproj" -- "$e2e\DemoApp\DemoApp.csproj"
# 断言：[wire] 日志出现 扫描/生成/csproj/挂接；末行 生成 3 文件 | csproj改=True | 挂接=Patched
dotnet build "$e2e\DemoApp\DemoApp.csproj" --nologo
# 断言：0 错误（生成的接线代码 + 挂接行可编译——这是对 Task 5 模板的最终编译验证）
$cred = "protocol=https`nhost=github.com`n" | git credential fill
$env:UPDATE_MANIFEST_TOKEN = ($cred | Where-Object { $_ -like "password=*" }) -replace "^password=", ""
dotnet run --project "$e2e\DemoApp\DemoApp.csproj" --no-restore
# 断言：输出含 "2.4.0" 当前版、"2.5.0" 最新版、notes "add test release for verification"
Remove-Item -Recurse $e2e   # 清理验证场
```

Expected: 三条断言全过——即"向导代码路径对一个真实项目完成接线，程序内收到 2.4.0→2.5.0 更新提示"。
（若第三步清单读旧：raw CDN 缓存约 5 分钟，等 2~3 分钟重跑。）

---

### Task 9: 发布链路扩展 —— Setup 产物挂 Release 附件 + 清单登记 setup 类型

**Files:**
- Modify: `tools/update-manifest.ps1`（新增 `-ArtifactType`/`-ArtifactUrl` 参数）
- Modify: `.github/workflows/release.yml`（publish 门控 + 新增 publish-setup job）

**Interfaces:**
- Consumes: 现有流水线结构（meta 解析/冒烟/清单竞态安全块）。
- Produces: `Company.UpdateChecker.Setup-v1.0.0` tag 触发：publish win-x64 → zip → GitHub Release 附件 → 清单登记 `type:"setup"` + 附件 URL。（CI 配置按规格 §5 豁免 TDD，Task 10 真实发布验证。）

- [ ] **Step 1: update-manifest.ps1 加参数**（param 块加两行；产物构造改用参数）

param 块（第 25 行 `[switch]$ManifestOnly` 之前插入）：
```powershell
    [string]$ArtifactType = "nuget",  # 产物类型：nuget / setup / ppl ...
    [string]$ArtifactUrl = "",        # 显式产物地址；留空 = 按 nuget 约定生成默认地址
```
第 68~73 行的 artifacts 块替换为：
```powershell
    artifacts  = @(
        (New-Object PSObject -Property ([ordered]@{
            type = $ArtifactType
            url  = if ($ArtifactUrl) { $ArtifactUrl } else { "https://updates.company.com/feed/$($PackageId.ToLower()).$($Version).nupkg" }
        }))
    )
```

- [ ] **Step 2: 用临时 tools 副本验证脚本行为**

```powershell
$sandbox = Join-Path $env:TEMP ("manifest-sb-" + [guid]::NewGuid().ToString("N"))
Copy-Item -Recurse tools $sandbox
powershell -NoProfile -ExecutionPolicy Bypass -File "$sandbox\update-manifest.ps1" -PackageId Company.UpdateChecker.Setup -Version 1.0.0 -Notes "test" -ManifestOnly -ArtifactType setup -ArtifactUrl "https://example.com/x.zip"
$m = Get-Content "$sandbox\manifest\manifest.json" -Raw | ConvertFrom-Json
"type=" + $m.packages.'Company.UpdateChecker.Setup'.artifacts[0].type
"url=" + $m.packages.'Company.UpdateChecker.Setup'.artifacts[0].url
Remove-Item -Recurse $sandbox
```
Expected: `type=setup`、`url=https://example.com/x.zip`。（仓库真实清单不被改动。）

- [ ] **Step 3: release.yml 门控 + 新 job**

publish job 的 `if:`（第 42 行）改为：
```yaml
    if: startsWith(github.ref, 'refs/tags/') && !endsWith(github.ref_name, '.Setup')
```
文件末尾追加新 job（meta 块与现有 publish job 刻意重复，注释说明同源）：
```yaml

  # ---------- 发布：Setup 向导（tag 后缀 .Setup 时替代 publish） ----------
  publish-setup:
    if: startsWith(github.ref, 'refs/tags/') && endsWith(github.ref_name, '.Setup')
    needs: test
    runs-on: ubuntu-latest
    permissions:
      contents: write      # 创建 Release + 提交 manifest.json 回 main
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
          fetch-tags: true
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      # meta 解析与 publish job 同源（保持两 job 各自独立可读）
      - name: 从 tag 解析包名/版本/发布说明
        id: meta
        shell: pwsh
        run: |
          $tag = "${{ github.ref_name }}"
          if ($tag -notmatch '^(?<pkg>[A-Za-z.]+)-v(?<ver>\d+\.\d+\.\d+(?:-[0-9A-Za-z.\-]+)?)$') {
            throw "tag '$tag' 不符合约定 <PackageId>v<版本>"
          }
          $pkg = $Matches['pkg']; $ver = $Matches['ver']
          git fetch --no-tags origin "+refs/tags/$($tag):refs/tags/$($tag)"
          $subject = "$(git tag -l --format='%(contents:subject)' $tag)"
          if ([string]::IsNullOrWhiteSpace($subject)) { $subject = "发布 $pkg $ver" }
          $critical = [string]$subject.Contains('[critical]')
          $notes = $subject -replace '\s*\[critical\]\s*$', ''
          "package-id=$pkg" >> $env:GITHUB_OUTPUT
          "version=$ver" >> $env:GITHUB_OUTPUT
          "critical=$critical" >> $env:GITHUB_OUTPUT
          "notes<<NOTES_EOF" >> $env:GITHUB_OUTPUT
          "$notes" >> $env:GITHUB_OUTPUT
          "NOTES_EOF" >> $env:GITHUB_OUTPUT
          "asset-name=$pkg.$ver.win-x64.zip" >> $env:GITHUB_OUTPUT
          "setup-url=https://github.com/${{ github.repository }}/releases/download/$tag/$pkg.$ver.win-x64.zip" >> $env:GITHUB_OUTPUT

      - name: 发布向导（publish win-x64 框架依赖 + Release 附件）
        shell: pwsh
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          $pkg = "${{ steps.meta.outputs.package-id }}"
          $ver = "${{ steps.meta.outputs.version }}"
          dotnet publish "packages/csharp/$pkg" -c Release -r win-x64 --self-contained false `
            -o dist/app -p:Version=$ver --nologo
          if ($LASTEXITCODE -ne 0) { throw "publish 失败" }
          Compress-Archive -Path dist/app/* -DestinationPath "dist/${{ steps.meta.outputs.asset-name }}" -Force
          # ubuntu runner 自带 gh CLI；附件即消费者的下载产物
          gh release create "${{ github.ref_name }}" "dist/${{ steps.meta.outputs.asset-name }}" `
            --title "$pkg $ver" --notes "${{ steps.meta.outputs.notes }}" --repo "$env:GITHUB_REPOSITORY"
          if ($LASTEXITCODE -ne 0) { throw "创建 Release 失败" }
          Write-Host "向导已发布：${{ steps.meta.outputs.setup-url }}"

      - name: 冒烟验证（Release 附件可下载且含 exe）
        shell: pwsh
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          $zip = Join-Path $env:RUNNER_TEMP "asset.zip"
          gh release download "${{ github.ref_name }}" --repo "$env:GITHUB_REPOSITORY" --pattern "*.zip" --output $zip
          if ($LASTEXITCODE -ne 0) { throw "附件下载失败" }
          $list = & unzip -l $zip
          if ($LASTEXITCODE -ne 0) { throw "解包列表失败" }
          if (-not ($list | Where-Object { $_ -match "\.exe$" })) { throw "附件中未找到 exe" }
          Write-Host "冒烟通过：附件可下载且含向导 exe"

      - name: 更新版本清单并提交回 main（含群通知）
        shell: pwsh
        run: |
          git config user.name "release-bot"
          git config user.email "release-bot@users.noreply.github.com"
          git fetch origin main
          git checkout -B manifest-update origin/main
          ./tools/update-manifest.ps1 `
            -PackageId "${{ steps.meta.outputs.package-id }}" `
            -Version "${{ steps.meta.outputs.version }}" `
            -Notes "${{ steps.meta.outputs.notes }}" `
            -Critical:$${{ steps.meta.outputs.critical }} `
            -ManifestOnly `
            -ArtifactType setup `
            -ArtifactUrl "${{ steps.meta.outputs.setup-url }}" `
            -WebhookUrl "${{ secrets.IM_WEBHOOK }}"
          git add tools/manifest/manifest.json
          git diff --staged --quiet
          if ($LASTEXITCODE -eq 0) {
            Write-Host "清单无变化（该版本已登记），跳过提交"
          } else {
            git commit -m "chore(manifest): ${{ steps.meta.outputs.package-id }} ${{ steps.meta.outputs.version }} [skip ci]"
            git push origin HEAD:main
            if ($LASTEXITCODE -ne 0) {
              git fetch origin main
              git rebase origin/main
              git push origin HEAD:main
            }
          }
```

- [ ] **Step 4: 提交**

```powershell
$msg = "发布链路支持 Setup 向导：publish win-x64 + Release 附件 + 清单登记 setup 类型（publish job 按 tag 后缀分流）"; [IO.File]::WriteAllText("$env:TEMP\commit-msg.txt", $msg, (New-Object Text.UTF8Encoding $false)); git add -A; git commit -F "$env:TEMP\commit-msg.txt"
```

---

### Task 10: 真实发布验证（两个 tag 走通两条发布路径）

**Files:** 无新改动；产出远端验证证据。

- [ ] **Step 1: 推 main 验证门禁**

Run: `git -c http.proxy=http://127.0.0.1:65532 push origin main` → GitHub Actions main 门禁 run 绿（API 轮询 `actions/runs?per_page=5`，看 head_branch=main 的最新 run `status=completed conclusion=success`；注意输出中文可能乱码，断言用 ASCII 字段）。

- [ ] **Step 2: 发布 UpdateChecker 1.2.0（走既有 nuget 路径）**

```powershell
$msg = "新增 AssemblyVersions.FromAssemblyName 按程序集名注册依赖（接线向导配套）"; [IO.File]::WriteAllText("$env:TEMP\tag-msg.txt", $msg, (New-Object Text.UTF8Encoding $false))
git tag -a Company.UpdateChecker-v1.2.0 -F "$env:TEMP\tag-msg.txt"
git -c http.proxy=http://127.0.0.1:65532 push origin Company.UpdateChecker-v1.2.0
```
验证：release run 全绿（publish job 因不带 .Setup 后缀正常走 nuget 路径）；Contents API 读清单：`Company.UpdateChecker latest=1.1.0 → 1.2.0`、notes 与 tag 注释一致、type=nuget。

- [ ] **Step 3: 发布 Setup 1.0.0（走新 setup 路径）**

```powershell
$msg = "首个接线安装向导：双击为项目接入程序内更新检查（扫描/生成/自动挂接/冒烟/回滚）"; [IO.File]::WriteAllText("$env:TEMP\tag-msg.txt", $msg, (New-Object Text.UTF8Encoding $false))
git tag -a Company.UpdateChecker.Setup-v1.0.0 -F "$env:TEMP\tag-msg.txt"
git -c http.proxy=http://127.0.0.1:65532 push origin Company.UpdateChecker.Setup-v1.0.0
```
验证（全部 Contents/REST API，ASCII 断言）：
- `publish-setup` run `status=completed conclusion=success`（publish job 被门控跳过）
- `releases/tags/Company.UpdateChecker.Setup-v1.0.0` 存在，`assets[0].name=Company.UpdateChecker.Setup.1.0.0.win-x64.zip`
- 清单 `Company.UpdateChecker.Setup`：`latest=1.0.0`、`artifacts[0].type=setup`、`artifacts[0].url` = Release 下载地址
- 本地同步：`git pull --rebase origin main`（拿回两个 bot 清单提交）

- [ ] **Step 4: 收尾**

- 仓库树干净：`git status --porcelain` 无输出
- 向用户报告：本地端到端证据（Task 8 三断言）+ 两条发布路径证据 + 请用户双击 `Company.UpdateChecker.Setup.exe` 做一次 GUI 手工冒烟（向导本尊的最终体验确认）

---

## Self-Review 记录（计划完成后已核）

- **规格覆盖**：§1 形态→T7/T9/T10；§2 五页→T7；§3 结构→T2~T7；§4 决策1→T1、决策2→T3、决策3→T4、决策4→T5（critical 警示图标/静默降级）、决策5→T6+T7+T8、决策6→各任务边界；§5→T1~T6 TDD + T7 构建 + T8 端到端；§6→T9+T10。无缺口。
- **类型一致性**：跨任务签名以 Global Constraints 契约块为准，各任务 Interfaces 与之一字不差。
- **已知取舍**（执行者注意）：① release.yml 两个 publish job 的 meta/清单块刻意重复（两 job 独立可读，注释已说明）；② 生成文件重复执行=覆盖（向导所有物），csproj/挂接才是幂等跳过；③ UpdateChecker 版本号在向导壳里是常量 `1.2.0`（升级时改壳一处）。
