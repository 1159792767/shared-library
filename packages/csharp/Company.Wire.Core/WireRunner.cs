namespace Company.Wire.Core;

/// <summary>
/// 接线编排：扫描 → 生成接线文件 → csproj 幂等加引用 → 启动挂接（可关）。
/// 所有被修改的文件先备份（Backups 供向导展示与 Rollback）。
/// </summary>
public static class WireRunner
{
    public static WireResult Run(WireOptions options, Action<string>? log = null)
    {
        log?.Invoke($"扫描 {Path.GetFileName(options.ProjectPath)} ...");
        var scan = DependencyScanner.Scan(options.ProjectPath);
        log?.Invoke(scan.CompanyPackages.Count == 0
            ? "未发现 Company.* 依赖，将只登记 UpdateChecker 自身"
            : $"发现公共包依赖：{string.Join(", ", scan.CompanyPackages)}");

        // 登记集合 = 扫描结果 + UpdateChecker 自身（去重）
        var deps = new List<string>(scan.CompanyPackages);
        if (!deps.Contains("Company.UpdateChecker", StringComparer.OrdinalIgnoreCase))
        {
            deps.Add("Company.UpdateChecker");
        }

        var projectDir = Path.GetDirectoryName(options.ProjectPath)!;
        log?.Invoke("生成接线文件（UpdateCheck/）...");
        var generated = WiringGenerator.Generate(
            projectDir, scan.RootNamespace, options.UiKind, options.ManifestUrl, deps);

        log?.Invoke("为 csproj 添加 Company.UpdateChecker 引用...");
        var csprojModified = CsprojEditor.TryAddPackageReference(
            options.ProjectPath, "Company.UpdateChecker", options.UpdateCheckerVersion);

        var backups = new List<BackupEntry>();
        if (csprojModified)
        {
            backups.Add(new BackupEntry(options.ProjectPath, options.ProjectPath + ".bak"));
            log?.Invoke("csproj 已更新（原文件备份为 .bak）");
        }
        else
        {
            log?.Invoke("csproj 已含该引用，跳过");
        }

        StartupPatchResult patch;
        if (options.AutoAttach)
        {
            log?.Invoke("挂接启动调用...");
            patch = StartupPatcher.Apply(projectDir, options.UiKind, scan.RootNamespace);
            if (patch.Status == StartupPatchStatus.Patched && patch.TargetFile is not null)
            {
                backups.Add(new BackupEntry(patch.TargetFile, patch.TargetFile + ".bak"));
                log?.Invoke($"已挂接：{Path.GetFileName(patch.TargetFile)}（原文件备份为 .bak）");
            }
            else if (patch.Status == StartupPatchStatus.AlreadyPatched)
            {
                log?.Invoke("入口已含挂接标记，跳过");
            }
            else
            {
                log?.Invoke("未识别出标准插入点，已降级为手工指引（绝不盲改代码）");
            }
        }
        else
        {
            var plan = StartupPatcher.Plan(projectDir, options.UiKind, scan.RootNamespace);
            patch = plan.Status == StartupPatchStatus.AlreadyPatched
                ? plan
                : new StartupPatchResult(StartupPatchStatus.Downgraded, null,
                    "未开启自动挂接。请在程序启动处手工加入：\r\n" +
                    $"_ = {scan.RootNamespace}.UpdateCheck.UpdateCheckRunner.RunAsync();");
            log?.Invoke("自动挂接已关闭，生成手工指引");
        }

        log?.Invoke("接线完成。");
        return new WireResult(scan.CompanyPackages, generated, csprojModified, patch, backups);
    }

    /// <summary>回滚一次接线：删除生成文件，从 .bak 还原被修改文件（向导冒烟失败时调用）。</summary>
    public static void Rollback(WireResult result)
    {
        foreach (var file in result.GeneratedFiles)
        {
            if (File.Exists(file)) { File.Delete(file); }
        }

        foreach (var backup in result.Backups)
        {
            if (File.Exists(backup.Backup))
            {
                File.Copy(backup.Backup, backup.Original, overwrite: true);
                File.Delete(backup.Backup);
            }
        }

        // 生成目录若已空则一并清掉
        foreach (var file in result.GeneratedFiles)
        {
            var dir = Path.GetDirectoryName(file);
            if (dir is not null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
    }
}
