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
    public void TryAdd_重复执行_返回false且文件内容不变_幂等()
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
    public void TryAdd_已有引用时不产生备份_未写盘()
    {
        var csproj = _assets.CreateStandardProject("BClient", "Company.UpdateChecker");

        var added = CsprojEditor.TryAddPackageReference(csproj, "Company.UpdateChecker", "1.2.0");

        Assert.False(added);
        Assert.False(File.Exists(csproj + ".bak"));
    }

    public void Dispose() => _assets.Dispose();
}
