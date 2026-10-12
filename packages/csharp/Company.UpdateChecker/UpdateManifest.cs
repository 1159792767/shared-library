using System.Text.Json;

namespace Company.UpdateChecker;

/// <summary>
/// 版本清单（manifest）：CI 发布包时自动生成/更新的 JSON，
/// 部署在内外网可达的只读地址上——所有项目程序从这个清单感知新版本。
/// </summary>
public sealed class UpdateManifest
{
    /// <summary>清单生成时间（CI 发布时间）。</summary>
    public DateTimeOffset? GeneratedAt { get; set; }

    /// <summary>包名 → 最新发布信息。键为 NuGet 包名（如 Company.Drivers）。</summary>
    public Dictionary<string, ManifestPackage> Packages { get; set; } = new();

    public static UpdateManifest Parse(string json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
            return manifest ?? throw new UpdateManifestException("版本清单内容为空。");
        }
        catch (JsonException ex)
        {
            throw new UpdateManifestException($"版本清单 JSON 解析失败：{ex.Message}", ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>清单中单个包的最新发布信息。</summary>
public sealed class ManifestPackage
{
    /// <summary>最新正式版本号（SemVer 字符串，如 "2.3.1"）。</summary>
    public string Latest { get; set; } = "";

    /// <summary>发布通道：stable / beta 等（预留，程序可按通道过滤）。</summary>
    public string? Channel { get; set; }

    /// <summary>是否重要更新（缺陷/安全修复），提示时强标注。</summary>
    public bool Critical { get; set; }

    /// <summary>该版本的发布时间。</summary>
    public DateTimeOffset? ReleasedAt { get; set; }

    /// <summary>变更说明（CHANGELOG 摘要）。</summary>
    public string? Notes { get; set; }

    /// <summary>下载产物列表。</summary>
    public List<ManifestArtifact>? Artifacts { get; set; }
}

/// <summary>清单中声明的下载产物。</summary>
public sealed class ManifestArtifact
{
    /// <summary>产物类型：nuget / ppl / vip / zip 等。</summary>
    public string Type { get; set; } = "";

    /// <summary>下载地址（内外网可达的只读地址）。</summary>
    public string Url { get; set; } = "";
}
