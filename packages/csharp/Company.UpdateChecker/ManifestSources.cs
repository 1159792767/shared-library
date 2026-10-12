namespace Company.UpdateChecker;

/// <summary>本地文件清单源：演示验证与内网共享盘场景。</summary>
public sealed class FileManifestSource(string manifestPath) : IManifestSource
{
    public async Task<string> GetManifestJsonAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(manifestPath))
        {
            throw new UpdateManifestException($"版本清单文件不存在：{manifestPath}");
        }

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new UpdateManifestException($"版本清单文件为空：{manifestPath}");
            }

            return json;
        }
        catch (IOException ex)
        {
            throw new UpdateManifestException($"读取版本清单文件失败：{manifestPath}", ex);
        }
    }
}

/// <summary>
/// HTTP 清单源：生产场景——程序定时拉取部署在内外网可达地址上的只读版本清单。
/// 可选 bearerToken 用于需要鉴权的清单地址（如私有仓库 raw 地址、带鉴权的清单服务）；
/// 无鉴权地址（生产 DMZ 静态服务）不传即可。
/// </summary>
public sealed class HttpManifestSource : IManifestSource, IDisposable
{
    private readonly HttpClient _client;
    private readonly string _manifestUrl;

    public HttpManifestSource(string manifestUrl, string? bearerToken = null, HttpMessageHandler? handler = null)
    {
        _manifestUrl = manifestUrl;
        _client = handler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(10) }
            : new HttpClient(handler, disposeHandler: false);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        }
    }

    public async Task<string> GetManifestJsonAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _client.GetStringAsync(_manifestUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new UpdateManifestException($"版本清单内容为空：{_manifestUrl}");
            }

            return json;
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateManifestException($"获取版本清单失败（网络/服务异常）：{_manifestUrl}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // 调用方主动取消不算失败；这里只包装“超时”
            throw new UpdateManifestException($"获取版本清单超时：{_manifestUrl}", ex);
        }
    }

    public void Dispose() => _client.Dispose();
}
