using System.Reflection;

namespace Company.UpdateChecker;

/// <summary>
/// 从编译产物程序集读取包版本（InformationalVersion，形如 "2.3.0+abc123"）。
/// 项目程序用它注册"当前依赖版本"：升级 NuGet 包重新编译后自动感知，无需手维护版本号。
/// </summary>
public static class AssemblyVersions
{
    public static SemVersion FromAssembly(Assembly assembly)
    {
        var raw = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                $"程序集 {assembly.GetName().Name} 缺少 AssemblyInformationalVersionAttribute，" +
                "打包配置有误：请通过 dotnet pack 生成包，不要手工编译部署 DLL。");
        }

        return SemVersion.Parse(raw);
    }
}
