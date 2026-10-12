using Company.UpdateChecker;
using Xunit;

namespace Company.UpdateChecker.Tests;

/// <summary>
/// UpdateChecker 行为规格：拉取版本清单，对比项目当前依赖，产出"有新版本"提示。
/// 推送语义遵循设计：默认只提示正式版（预发布需显式开启）；本地领先/未登记不提示。
/// </summary>
public class UpdateCheckerTests
{
    private const string DriversManifest = """
        {
          "generatedAt": "2026-10-12T10:00:00Z",
          "packages": {
            "Company.Drivers": {
              "latest": "2.3.1",
              "channel": "stable",
              "critical": true,
              "releasedAt": "2026-10-11T09:00:00Z",
              "notes": "修复 Modbus 断连重试缺陷",
              "artifacts": [
                { "type": "nuget", "url": "https://updates.company.com/feed/company.drivers.2.3.1.nupkg" }
              ]
            }
          }
        }
        """;

    private static UpdateChecker CheckerFor(string json, UpdateCheckerOptions? options = null) =>
        new(new StubManifestSource(json), options);

    private static readonly PackageDependency Drivers230 =
        new("Company.Drivers", SemVersion.Parse("2.3.0"));

    [Fact]
    public async Task Check_清单版本高于当前_返回一条更新提示()
    {
        var updates = await CheckerFor(DriversManifest).CheckAsync(new[] { Drivers230 });

        var update = Assert.Single(updates);
        Assert.Equal("Company.Drivers", update.PackageId);
        Assert.Equal("2.3.0", update.CurrentVersion.ToString());
        Assert.Equal("2.3.1", update.LatestVersion.ToString());
        Assert.Equal("修复 Modbus 断连重试缺陷", update.ReleaseNotes);
        Assert.True(update.IsCritical);
    }

    [Fact]
    public async Task Check_更新提示携带下载产物地址()
    {
        var updates = await CheckerFor(DriversManifest).CheckAsync(new[] { Drivers230 });

        var artifact = Assert.Single(Assert.Single(updates).Artifacts);
        Assert.Equal("nuget", artifact.Type);
        Assert.Equal("https://updates.company.com/feed/company.drivers.2.3.1.nupkg", artifact.Url);
    }

    [Fact]
    public async Task Check_清单版本等于当前_无更新提示()
    {
        var deps = new[] { new PackageDependency("Company.Drivers", SemVersion.Parse("2.3.1")) };

        Assert.Empty(await CheckerFor(DriversManifest).CheckAsync(deps));
    }

    [Fact]
    public async Task Check_本地版本领先清单_无更新提示()
    {
        // 本地 2.9.0 领先清单 2.3.1：清单滞后时不能反向"提示降级"
        var deps = new[] { new PackageDependency("Company.Drivers", SemVersion.Parse("2.9.0")) };

        Assert.Empty(await CheckerFor(DriversManifest).CheckAsync(deps));
    }

    [Fact]
    public async Task Check_清单未登记该包_无更新提示()
    {
        var deps = new[] { new PackageDependency("Company.Unregistered", SemVersion.Parse("1.0.0")) };

        Assert.Empty(await CheckerFor(DriversManifest).CheckAsync(deps));
    }

    [Fact]
    public async Task Check_默认忽略预发布新版()
    {
        // 2.4.0-beta.1 是预发布：默认策略不向项目推送预发布版
        var json = """{ "packages": { "Company.Drivers": { "latest": "2.4.0-beta.1" } } }""";

        Assert.Empty(await CheckerFor(json).CheckAsync(new[] { Drivers230 }));
    }

    [Fact]
    public async Task Check_开启预发布选项_提示预发布新版()
    {
        var json = """{ "packages": { "Company.Drivers": { "latest": "2.4.0-beta.1" } } }""";
        var checker = CheckerFor(json, new UpdateCheckerOptions { IncludePrerelease = true });

        var update = Assert.Single(await checker.CheckAsync(new[] { Drivers230 }));
        Assert.Equal("2.4.0-beta.1", update.LatestVersion.ToString());
        Assert.True(update.LatestVersion.IsPrerelease);
    }

    [Fact]
    public async Task Check_清单Json非法_抛出带说明的清单异常()
    {
        // 更新检查失败不应静默吞掉：调用方（项目程序）捕获后决定降级策略
        var checker = CheckerFor("{ 这不是合法 JSON");

        var ex = await Assert.ThrowsAsync<UpdateManifestException>(
            () => checker.CheckAsync(new[] { Drivers230 }));
        Assert.Contains("版本清单", ex.Message);
    }

    [Fact]
    public async Task Check_多包依赖_只提示有新版的包()
    {
        var json = """
        {
          "packages": {
            "Company.Drivers": { "latest": "2.3.1" },
            "Company.Utils":  { "latest": "1.0.0" }
          }
        }
        """;
        var deps = new[]
        {
            new PackageDependency("Company.Drivers", SemVersion.Parse("2.3.0")),
            new PackageDependency("Company.Utils", SemVersion.Parse("1.0.0")),
            new PackageDependency("Company.Unregistered", SemVersion.Parse("0.1.0")),
        };

        var updates = await CheckerFor(json).CheckAsync(deps);

        var update = Assert.Single(updates);
        Assert.Equal("Company.Drivers", update.PackageId);
    }

    private sealed class StubManifestSource : IManifestSource
    {
        private readonly string _json;
        public StubManifestSource(string json) => _json = json;

        public Task<string> GetManifestJsonAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_json);
    }
}
