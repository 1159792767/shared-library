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
