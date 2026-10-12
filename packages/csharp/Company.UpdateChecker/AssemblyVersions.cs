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

    /// <summary>
    /// 按程序集名（= NuGet 包名）注册依赖版本：接线向导生成的代码用它，
    /// 向导无需知道各包的入口类型；新增公共包时向导与接线代码零改动。
    /// 程序集不存在 = 引用未还原或包名写错，必须快速失败。
    /// </summary>
    public static SemVersion FromAssemblyName(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        try
        {
            return FromAssembly(Assembly.Load(new AssemblyName(assemblyName)));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            throw new InvalidOperationException(
                $"按名加载程序集 {assemblyName} 失败：请确认已通过 NuGet 引用该包且已还原。", ex);
        }
    }
}
