using System.Reflection;
using System.Reflection.Emit;
using Company.UpdateChecker;
using Xunit;

namespace Company.UpdateChecker.Tests;

/// <summary>
/// AssemblyVersions 行为规格：从编译产物的 InformationalVersion（形如 "2.3.0+abc123"）
/// 读出包版本。项目程序用它注册"当前依赖版本"，升级 NuGet 包后自动感知。
/// </summary>
public class AssemblyVersionsTests
{
    [Fact]
    public void FromAssembly_读取程序集信息版本_解析出SemVer()
    {
        // 用测试程序集自身：dotnet 编译产物必然带 InformationalVersion
        var version = AssemblyVersions.FromAssembly(typeof(AssemblyVersionsTests).Assembly);

        Assert.NotNull(version);
        Assert.False(version.IsPrerelease);
        Assert.True(version.Major >= 1);
    }

    [Fact]
    public void FromAssembly_程序集无信息版本_抛出打包配置异常()
    {
        // 动态程序集不携带 InformationalVersion：说明打包配置有问题，必须快速失败
        var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("NoVersionAssembly"), AssemblyBuilderAccess.Run);

        var ex = Assert.Throws<InvalidOperationException>(
            () => AssemblyVersions.FromAssembly(dynamicAssembly));
        Assert.Contains("NoVersionAssembly", ex.Message);
    }
}
