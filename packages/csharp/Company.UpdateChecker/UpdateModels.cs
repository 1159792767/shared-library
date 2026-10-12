namespace Company.UpdateChecker;

/// <summary>项目当前依赖的公共包（包名 + 正在使用的版本）。</summary>
public sealed record PackageDependency(string PackageId, SemVersion CurrentVersion);

/// <summary>一次检查中发现的单个可更新包提示。</summary>
/// <param name="IsCritical">发布方标记的重要更新（缺陷修复/安全修复），程序可据此强提示。</param>
public sealed record PackageUpdate(
    string PackageId,
    SemVersion CurrentVersion,
    SemVersion LatestVersion,
    bool IsCritical,
    string? ReleaseNotes,
    IReadOnlyList<PackageArtifact> Artifacts);

/// <summary>包的下载产物（NuGet 包地址 / PPL 文件地址等）。</summary>
public sealed record PackageArtifact(string Type, string Url);

/// <summary>更新检查的可调策略。</summary>
public sealed record UpdateCheckerOptions
{
    /// <summary>
    /// 是否把预发布版（如 2.4.0-beta.1）作为更新提示。默认 false：
    /// 预发布版本只对显式开启的项目可见，防止未验证版本扩散到所有项目。
    /// </summary>
    public bool IncludePrerelease { get; init; }
}

/// <summary>版本清单的获取来源：本地文件、HTTP 地址等。</summary>
public interface IManifestSource
{
    Task<string> GetManifestJsonAsync(CancellationToken cancellationToken = default);
}

/// <summary>版本清单不可用或内容非法时抛出，由调用方决定降级策略（提示网络异常/跳过检查）。</summary>
public sealed class UpdateManifestException : Exception
{
    public UpdateManifestException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
