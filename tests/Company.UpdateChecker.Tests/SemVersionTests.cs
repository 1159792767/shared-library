using Company.UpdateChecker;
using Xunit;

namespace Company.UpdateChecker.Tests;

/// <summary>
/// SemVersion 行为规格：遵循 SemVer 2.0.0 核心规则（主.次.修订[-预发布][+构建元数据]）。
/// 构建元数据（+后缀）不参与比较——用于剥离 AssemblyInformationalVersion 里的 commit 哈希。
/// </summary>
public class SemVersionTests
{
    // ---------- 解析 ----------

    [Fact]
    public void Parse_标准三段版本_解析出主次修订号()
    {
        var v = SemVersion.Parse("2.3.1");

        Assert.Equal(2, v.Major);
        Assert.Equal(3, v.Minor);
        Assert.Equal(1, v.Patch);
        Assert.False(v.IsPrerelease);
    }

    [Fact]
    public void Parse_带预发布标识_识别为预发布版本()
    {
        var v = SemVersion.Parse("2.4.0-beta.1");

        Assert.True(v.IsPrerelease);
        Assert.Equal("beta.1", v.Prerelease);
    }

    [Fact]
    public void Parse_带构建元数据后缀_元数据被剥离不影响等值()
    {
        // dotnet 编译产物 InformationalVersion 形如 "2.3.0+abc123"，必须能读
        var withMeta = SemVersion.Parse("2.3.0+abc123");
        var plain = SemVersion.Parse("2.3.0");

        Assert.Equal(plain, withMeta);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.2")]
    [InlineData("1.2.x")]
    [InlineData("")]
    public void Parse_非法版本字符串_抛出格式异常(string input)
    {
        Assert.Throws<FormatException>(() => SemVersion.Parse(input));
    }

    // ---------- 比较 ----------

    [Fact]
    public void 比较_修订号更高_则更大()
    {
        Assert.True(SemVersion.Parse("2.3.1") > SemVersion.Parse("2.3.0"));
    }

    [Fact]
    public void 比较_次版本更高_则更大()
    {
        Assert.True(SemVersion.Parse("2.4.0") > SemVersion.Parse("2.3.9"));
    }

    [Fact]
    public void 比较_主版本更高_则更大()
    {
        Assert.True(SemVersion.Parse("3.0.0") > SemVersion.Parse("2.9.9"));
    }

    [Fact]
    public void 比较_正式版大于同号预发布版()
    {
        // SemVer 规则：2.4.0-beta.1 < 2.4.0（预发布排在正式发布之前）
        Assert.True(SemVersion.Parse("2.4.0") > SemVersion.Parse("2.4.0-beta.1"));
    }

    [Fact]
    public void 比较_预发布标识按段比较_数字段用数值序()
    {
        Assert.True(SemVersion.Parse("2.4.0-beta.2") > SemVersion.Parse("2.4.0-beta.1"));
        // beta 在字母序上大于 alpha
        Assert.True(SemVersion.Parse("2.4.0-beta.1") > SemVersion.Parse("2.4.0-alpha.9"));
    }

    [Fact]
    public void 比较_段数更多的预发布版更大()
    {
        // SemVer 规则：alpha.1 < alpha.1.1（预发布段逐个比较，先短的后长的更大）
        Assert.True(SemVersion.Parse("2.4.0-alpha.1.1") > SemVersion.Parse("2.4.0-alpha.1"));
    }

    [Fact]
    public void ToString_还原规范版本字符串()
    {
        Assert.Equal("2.4.0-beta.1", SemVersion.Parse("2.4.0-beta.1").ToString());
        Assert.Equal("2.3.0", SemVersion.Parse("2.3.0+abc").ToString());
    }
}
