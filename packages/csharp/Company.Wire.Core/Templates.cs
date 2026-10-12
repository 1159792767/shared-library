namespace Company.Wire.Core;

/// <summary>接线源码模板（占位符：__NS__ 根命名空间 / __MANIFEST_URL__ 清单地址 / __DEPS__ 依赖登记数组项）。</summary>
internal static class Templates
{
    public const string Service = """
        using Company.UpdateChecker;

        namespace __NS__.UpdateCheck;

        /// <summary>
        /// 公共库更新检查接线（由 Company.UpdateChecker.Setup 向导生成，可直接修改）。
        /// 检测失败自动降级，绝不影响本程序正常运行。
        /// </summary>
        public static class UpdateCheckService
        {
            /// <summary>版本清单地址（内外网可达的只读 HTTP 地址，切换环境时只改这里）。</summary>
            public const string ManifestUrl = "__MANIFEST_URL__";

            /// <summary>本项目使用的公共包登记：版本从程序集自动读取，升级 NuGet 包重新编译后自动感知。</summary>
            public static readonly PackageDependency[] Dependencies =
            {
        __DEPS__
            };

            /// <summary>执行一次更新检查。清单不可达等异常由 Runner 消化（现场设备不允许因检查罢工）。</summary>
            public static async Task<IReadOnlyList<PackageUpdate>> CheckAsync()
            {
                var source = new HttpManifestSource(
                    ManifestUrl,
                    Environment.GetEnvironmentVariable("UPDATE_MANIFEST_TOKEN"));
                return await new UpdateChecker(source).CheckAsync(Dependencies);
            }
        }
        """;

    public const string RunnerConsole = """
        using Company.UpdateChecker;

        namespace __NS__.UpdateCheck;

        /// <summary>控制台形态：检测结果直接打印到控制台（由向导生成）。</summary>
        public static class UpdateCheckRunner
        {
            public static async Task RunAsync()
            {
                try
                {
                    var updates = await UpdateCheckService.CheckAsync();
                    if (updates.Count == 0)
                    {
                        Console.WriteLine("  [检查结果] 所有公共包均为最新版本。");
                        return;
                    }

                    Console.WriteLine("  [检查结果] 检测到公共库有新版本！");
                    foreach (var u in updates)
                    {
                        Console.WriteLine($"  │ {u.PackageId} {u.CurrentVersion} -> {u.LatestVersion}" +
                            $"{(u.IsCritical ? "  !! 重要更新" : "")}");
                        Console.WriteLine($"  │ 变更：{u.ReleaseNotes ?? "（无说明）"}");
                        foreach (var a in u.Artifacts)
                        {
                            Console.WriteLine($"  │ 下载：[{a.Type}] {a.Url}");
                        }
                    }
                }
                catch (UpdateManifestException)
                {
                    // 清单不可达：静默降级，不影响程序运行
                }
            }
        }
        """;

    public const string RunnerWinForms = """
        using Company.UpdateChecker;

        namespace __NS__.UpdateCheck;

        /// <summary>WinForms 形态：有更新时弹窗提示（critical 用黄色警示图标；由向导生成）。</summary>
        public static class UpdateCheckRunner
        {
            public static async Task RunAsync()
            {
                try
                {
                    var updates = await UpdateCheckService.CheckAsync();
                    if (updates.Count == 0)
                    {
                        return;
                    }

                    var critical = updates.Any(u => u.IsCritical);
                    var text = string.Join(Environment.NewLine + Environment.NewLine, updates.Select(u =>
                        $"{u.PackageId} {u.CurrentVersion} -> {u.LatestVersion}" +
                        (u.IsCritical ? "  !! 重要更新" : "") + Environment.NewLine +
                        $"{u.ReleaseNotes ?? ""}"));
                    System.Windows.Forms.MessageBox.Show(
                        "检测到公共库有新版本：" + Environment.NewLine + Environment.NewLine + text,
                        critical ? "公共库重要更新" : "公共库更新",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        critical
                            ? System.Windows.Forms.MessageBoxIcon.Warning
                            : System.Windows.Forms.MessageBoxIcon.Information);
                }
                catch (UpdateManifestException)
                {
                    // 清单不可达：静默降级，不影响程序运行
                }
            }
        }
        """;

    public const string RunnerWpf = """
        using Company.UpdateChecker;

        namespace __NS__.UpdateCheck;

        /// <summary>WPF 形态：有更新时弹窗提示（critical 用警示图标；由向导生成）。</summary>
        public static class UpdateCheckRunner
        {
            public static async Task RunAsync()
            {
                try
                {
                    var updates = await UpdateCheckService.CheckAsync();
                    if (updates.Count == 0)
                    {
                        return;
                    }

                    var critical = updates.Any(u => u.IsCritical);
                    var text = string.Join(Environment.NewLine + Environment.NewLine, updates.Select(u =>
                        $"{u.PackageId} {u.CurrentVersion} -> {u.LatestVersion}" +
                        (u.IsCritical ? "  !! 重要更新" : "") + Environment.NewLine +
                        $"{u.ReleaseNotes ?? ""}"));
                    System.Windows.MessageBox.Show(
                        "检测到公共库有新版本：" + Environment.NewLine + Environment.NewLine + text,
                        critical ? "公共库重要更新" : "公共库更新",
                        System.Windows.MessageBoxButton.OK,
                        critical
                            ? System.Windows.MessageBoxImage.Warning
                            : System.Windows.MessageBoxImage.Information);
                }
                catch (UpdateManifestException)
                {
                    // 清单不可达：静默降级，不影响程序运行
                }
            }
        }
        """;

    public const string Readme = """
        # 更新检查接线说明（由 Company.UpdateChecker.Setup 向导生成）

        本目录由接线向导生成，包含三部分：

        - `UpdateCheckService.cs`：依赖登记 + 清单地址 + 检查逻辑（核心，UI 无关）
        - `UpdateCheckRunner.cs`：提示形态（控制台打印 / 弹窗）
        - 本说明

        ## 启动调用

        向导已在程序入口自动挂接（见 csproj 同级 Program.cs / App.xaml.cs 中带
        `// ===== Company UpdateCheck (auto-wired) =====` 标记的代码块）。
        若向导提示"降级为指引"，请手工把这一行加到程序启动处：

        ```
        _ = __NS__.UpdateCheck.UpdateCheckRunner.RunAsync();
        ```

        （控制台顶级语句程序可用 `await __NS__.UpdateCheck.UpdateCheckRunner.RunAsync();`）

        ## 私有清单令牌

        私有清单地址需要 bearer 令牌时，从**用户环境变量** `UPDATE_MANIFEST_TOKEN` 读取
        （不进代码、不进仓库）。向导可自动配置；手工配置：

        ```powershell
        setx UPDATE_MANIFEST_TOKEN "<你的令牌>"
        ```

        ## 升级纪律（PR 检查单）

        1. 本目录代码可以按项目需要修改，但提交前先确认与公共库模板没有冲突性偏差
        2. 依赖登记自动跟随 NuGet 包引用（升级包重新编译即生效），不要手工写版本号
        3. 改动一律走 PR 审批合并，不直推 main
        """;
}
