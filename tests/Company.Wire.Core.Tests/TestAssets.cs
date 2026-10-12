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
