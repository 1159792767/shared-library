namespace Company.UpdateChecker;

/// <summary>
/// 程序内更新检查器：各项目程序启动时/定时调用，
/// 对比"本项目当前依赖版本"与"版本清单最新版本"，产出程序内更新提示。
/// </summary>
public sealed class UpdateChecker
{
    private readonly IManifestSource _source;
    private readonly UpdateCheckerOptions _options;

    public UpdateChecker(IManifestSource source, UpdateCheckerOptions? options = null)
    {
        _source = source;
        _options = options ?? new UpdateCheckerOptions();
    }

    /// <summary>
    /// 检查依赖列表的更新。只提示"清单版本高于当前"的正式版；
    /// 清单滞后、包未登记、预发布（未开启选项）均不提示。
    /// </summary>
    public async Task<IReadOnlyList<PackageUpdate>> CheckAsync(
        IEnumerable<PackageDependency> dependencies,
        CancellationToken cancellationToken = default)
    {
        var json = await _source.GetManifestJsonAsync(cancellationToken).ConfigureAwait(false);
        var manifest = UpdateManifest.Parse(json);

        var updates = new List<PackageUpdate>();
        foreach (var dependency in dependencies)
        {
            if (!manifest.Packages.TryGetValue(dependency.PackageId, out var entry))
            {
                continue; // 清单未登记：项目引用的包不归这个清单管，不提示
            }

            var latest = ParseLatestVersion(dependency.PackageId, entry.Latest);

            if (latest.IsPrerelease && !_options.IncludePrerelease)
            {
                continue; // 默认不把预发布版推给项目程序
            }

            if (latest <= dependency.CurrentVersion)
            {
                continue; // 已是最新，或本地领先清单（清单滞后）
            }

            updates.Add(new PackageUpdate(
                dependency.PackageId,
                dependency.CurrentVersion,
                latest,
                entry.Critical,
                entry.Notes,
                MapArtifacts(entry.Artifacts)));
        }

        return updates;
    }

    private static SemVersion ParseLatestVersion(string packageId, string latest)
    {
        if (!SemVersion.TryParse(latest, out var version))
        {
            throw new UpdateManifestException(
                $"版本清单中的包 {packageId} 版本号非法：\"{latest}\"，请联系公共库维护者。");
        }

        return version!;
    }

    private static IReadOnlyList<PackageArtifact> MapArtifacts(List<ManifestArtifact>? artifacts) =>
        artifacts?.Select(a => new PackageArtifact(a.Type, a.Url)).ToList() ?? [];
}
