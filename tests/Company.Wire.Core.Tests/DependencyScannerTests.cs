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
