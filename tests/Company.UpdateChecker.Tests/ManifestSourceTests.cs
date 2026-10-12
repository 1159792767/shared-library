using Company.UpdateChecker;
using Xunit;

namespace Company.UpdateChecker.Tests;

/// <summary>
/// 清单源行为规格：
/// - FileManifestSource：本地文件源（演示/内网共享盘场景）
/// - HttpManifestSource：HTTP 源（内外网可达的只读清单地址）
/// 两者失败时统一抛 UpdateManifestException，调用方一套降级逻辑处理。
/// </summary>
public class FileManifestSourceTests : IDisposable
{
    private readonly string _tempFile =
        Path.Combine(Path.GetTempPath(), $"manifest-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Get_文件存在_返回文件全部内容()
    {
        var json = """{ "packages": { "Company.Drivers": { "latest": "2.3.0" } } }""";
        await File.WriteAllTextAsync(_tempFile, json);
        var source = new FileManifestSource(_tempFile);

        Assert.Equal(json, await source.GetManifestJsonAsync());
    }

    [Fact]
    public async Task Get_文件不存在_抛清单异常并指明路径()
    {
        var source = new FileManifestSource(_tempFile); // 尚未创建

        var ex = await Assert.ThrowsAsync<UpdateManifestException>(
            () => source.GetManifestJsonAsync());
        Assert.Contains(_tempFile, ex.Message);
    }

    [Fact]
    public async Task Get_文件为空_抛清单异常()
    {
        await File.WriteAllTextAsync(_tempFile, "");
        var source = new FileManifestSource(_tempFile);

        await Assert.ThrowsAsync<UpdateManifestException>(() => source.GetManifestJsonAsync());
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }
}

public class HttpManifestSourceTests
{
    [Fact]
    public async Task Get_服务器返回错误状态_抛清单异常并指明地址()
    {
        var handler = new StubHttpMessageHandler(
            _ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)));
        var source = new HttpManifestSource("https://updates.company.com/manifest.json", handler: handler);

        var ex = await Assert.ThrowsAsync<UpdateManifestException>(
            () => source.GetManifestJsonAsync());
        Assert.Contains("https://updates.company.com/manifest.json", ex.Message);
    }

    [Fact]
    public async Task Get_网络请求异常_抛清单异常()
    {
        var handler = new StubHttpMessageHandler(
            _ => throw new HttpRequestException("模拟网络中断"));
        var source = new HttpManifestSource("https://updates.company.com/manifest.json", handler: handler);

        await Assert.ThrowsAsync<UpdateManifestException>(() => source.GetManifestJsonAsync());
    }

    [Fact]
    public async Task Get_配置令牌_请求携带Bearer授权头()
    {
        // 私有仓库/鉴权清单服务场景：令牌以 Authorization: Bearer 头发送，不拼 URL（避免泄漏进日志）
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return Task.FromResult(OkResponse());
        });
        var source = new HttpManifestSource(
            "https://updates.company.com/manifest.json", bearerToken: "t0k3n", handler: handler);

        await source.GetManifestJsonAsync();

        Assert.NotNull(captured);
        Assert.Equal("Bearer", captured!.Headers.Authorization?.Scheme);
        Assert.Equal("t0k3n", captured.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Get_未配置令牌_请求无授权头()
    {
        // 无鉴权清单地址（生产 DMZ 静态服务）：请求不应携带任何凭据
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return Task.FromResult(OkResponse());
        });
        var source = new HttpManifestSource("https://updates.company.com/manifest.json", handler: handler);

        await source.GetManifestJsonAsync();

        Assert.Null(captured!.Headers.Authorization);
    }

    private static HttpResponseMessage OkResponse() =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "packages": {} }""")
        };

    /// <summary>注入测试用的 HTTP 行为替身：真实 HttpClient 管道 + 可控响应，验证异常包装逻辑。</summary>
    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }
}
