using System.Diagnostics;
using Company.Wire.Core;

namespace Company.UpdateChecker.Setup;

/// <summary>5 页安装向导：欢迎 → 选择项目 → 选项 → 预览与执行 → 完成。壳只做 UI，逻辑全在 Wire.Core。</summary>
public sealed class WizardForm : Form
{
    private const string DefaultManifest =
        "https://raw.githubusercontent.com/1159792767/shared-library/main/tools/manifest/manifest.json";
    private const string UpdateCheckerVersion = "1.2.0";

    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = new Padding(24) };
    private readonly Button _back = new() { Text = "< 上一步", Width = 100 };
    private readonly Button _next = new() { Text = "下一步 >", Width = 100 };
    private readonly Panel[] _pages = new Panel[5];
    private int _pageIndex;

    private readonly TextBox _projectPath = new() { Width = 420, ReadOnly = true };
    private readonly RadioButton _uiConsole = new() { Text = "控制台程序", Checked = true, AutoSize = true };
    private readonly RadioButton _uiWinForms = new() { Text = "WinForms 程序", AutoSize = true };
    private readonly RadioButton _uiWpf = new() { Text = "WPF 程序", AutoSize = true };
    private readonly TextBox _manifestUrl = new() { Width = 420, Text = DefaultManifest };
    private readonly CheckBox _autoAttach = new() { Text = "自动挂接启动调用（预览差异 + .bak 备份；非标准形态绝不盲改）", Checked = true, AutoSize = true };
    private readonly CheckBox _configToken = new() { Text = "为本机配置清单访问令牌（从 Git 凭据自动获取，写入用户环境变量 UPDATE_MANIFEST_TOKEN）", AutoSize = true };

    private readonly TextBox _preview = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F), WordWrap = false };
    private readonly TextBox _runLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F), Visible = false };
    private readonly Button _execute = new() { Text = "确认执行", Width = 110 };
    private readonly Label _doneSummary = new() { Dock = DockStyle.Fill, Text = "" };

    private WireResult? _lastResult;

    public WizardForm()
    {
        Text = "公共库更新检查 · 接线向导";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(780, 540);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        var banner = new Panel { Dock = DockStyle.Left, Width = 180, BackColor = Color.FromArgb(0, 99, 177) };
        banner.Controls.Add(new Label
        {
            Text = "公共库\r\n\r\n接线向导",
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 13F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 140,
            TextAlign = ContentAlignment.MiddleCenter,
        });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        buttons.Controls.Add(new Button { Text = "取消", Width = 100, DialogResult = DialogResult.Cancel });
        buttons.Controls.Add(_next);
        buttons.Controls.Add(_back);

        _pages[0] = BuildWelcomePage();
        _pages[1] = BuildSelectPage();
        _pages[2] = BuildOptionsPage();
        _pages[3] = BuildExecutePage();
        _pages[4] = BuildDonePage();
        foreach (var page in _pages) { page.Dock = DockStyle.Fill; page.Visible = false; _content.Controls.Add(page); }

        Controls.Add(_content);
        Controls.Add(buttons);
        Controls.Add(banner);

        _back.Click += (s, e) => ShowPage(_pageIndex - 1);
        _next.Click += (s, e) => OnNext();
        _execute.Click += async (s, e) => await RunWiringAsync();

        ShowPage(0);
    }

    private static Panel Page(params Control[] controls)
    {
        var page = new Panel();
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        foreach (var c in controls) { stack.Controls.Add(c); }
        page.Controls.Add(stack);
        return page;
    }

    private Panel BuildWelcomePage() => Page(
        new Label { Text = "欢迎使用公共库接线向导", Font = new Font("微软雅黑", 14F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 8, 0, 16) },
        new Label
        {
            Text = "向导将为你选择的项目接入\"公共库更新检查\"：\r\n\r\n" +
                "  1. 自动扫描项目已引用的 Company.* 公共包并生成依赖登记\r\n" +
                "  2. 生成接线代码（检测失败不影响程序运行）\r\n" +
                "  3. 为 csproj 幂等添加 Company.UpdateChecker 引用\r\n" +
                "  4. 自动挂接启动调用（可预览差异；改前自动备份 .bak）\r\n" +
                "  5. 编译冒烟验证，失败自动回滚\r\n\r\n" +
                "建议先提交当前代码，再开始接线。点击\"下一步\"继续。",
            AutoSize = true,
        });

    private Panel BuildSelectPage()
    {
        var browse = new Button { Text = "浏览...", Width = 90 };
        browse.Click += (s, e) =>
        {
            using var dialog = new OpenFileDialog { Filter = "C# 项目文件 (*.csproj)|*.csproj" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _projectPath.Text = dialog.FileName;
            }
        };
        return Page(
            new Label { Text = "选择要接线的项目", Font = new Font("微软雅黑", 12F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 8, 0, 16) },
            new Label { Text = "项目文件（.csproj）：", AutoSize = true },
            Row(_projectPath, browse),
            new Label { Text = "向导只修改该项目目录内的文件。", AutoSize = true, ForeColor = Color.DimGray });
    }

    private Panel BuildOptionsPage() => Page(
        new Label { Text = "接线选项", Font = new Font("微软雅黑", 12F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 8, 0, 16) },
        new Label { Text = "程序形态（决定提示样式与挂接位置）：", AutoSize = true },
        Row(_uiConsole, _uiWinForms, _uiWpf),
        new Label { Text = "版本清单地址：", AutoSize = true, Margin = new Padding(0, 12, 0, 0) },
        _manifestUrl,
        new Label { Text = "", AutoSize = true, Height = 8 },
        _autoAttach,
        _configToken);

    private Panel BuildExecutePage() => Page(
        new Label { Text = "预览将要执行的修改", Font = new Font("微软雅黑", 12F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 8, 0, 8) },
        _preview,
        _runLog,
        _execute);

    private Panel BuildDonePage()
    {
        var again = new Button { Text = "再接下一个项目", Width = 130 };
        again.Click += (s, e) => { Reset(); ShowPage(1); };
        return Page(_doneSummary, new Label { Text = "", AutoSize = true, Height = 12 }, again);
    }

    private static Panel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0) };
        foreach (var c in controls) { row.Controls.Add(c); }
        return row;
    }

    private UiKind SelectedUiKind => _uiWinForms.Checked ? UiKind.WinForms
        : _uiWpf.Checked ? UiKind.Wpf
        : UiKind.Console;

    private void ShowPage(int index)
    {
        _pageIndex = index;
        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
        }

        _back.Visible = index is > 0 and < 3;
        _next.Visible = index is < 3 and not 3;
        _next.Enabled = index switch
        {
            1 => File.Exists(_projectPath.Text) && _projectPath.Text.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
        _next.Text = index == 2 ? "预览 >" : "下一步 >";
        _execute.Visible = index == 3;
        if (index == 3)
        {
            _preview.Visible = true;
            _runLog.Visible = false;
            _preview.Text = BuildPreviewText();
        }
    }

    private void OnNext()
    {
        if (_pageIndex == 1 && !File.Exists(_projectPath.Text))
        {
            MessageBox.Show("请先选择有效的 .csproj 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ShowPage(_pageIndex + 1);
    }

    private string BuildPreviewText()
    {
        var csproj = _projectPath.Text;
        var dir = Path.GetDirectoryName(csproj)!;
        var scan = DependencyScanner.Scan(csproj);
        var lines = new List<string>
        {
            "将执行以下修改：",
            "",
            "1) 生成文件（UpdateCheck/ 目录）",
            "   UpdateCheck/UpdateCheckService.cs   依赖登记 + 清单地址 + 检查逻辑",
            "   UpdateCheck/UpdateCheckRunner.cs    " + SelectedUiKind switch { UiKind.WinForms => "WinForms 弹窗提示", UiKind.Wpf => "WPF 弹窗提示", _ => "控制台打印提示" },
            "   UpdateCheck/接线说明.md",
            "",
            "2) csproj（原文件备份 .bak）",
            "   " + (File.ReadAllText(csproj).Contains("Company.UpdateChecker") ? "已含 Company.UpdateChecker 引用，跳过" : $"添加 <PackageReference Include=\"Company.UpdateChecker\" Version=\"{UpdateCheckerVersion}\" />"),
            "",
        };

        if (_autoAttach.Checked)
        {
            var preview = StartupPatcher.PreviewContent(dir, SelectedUiKind, scan.RootNamespace);
            if (preview is null)
            {
                var plan = StartupPatcher.Plan(dir, SelectedUiKind, scan.RootNamespace);
                lines.Add("3) 启动挂接：" + (plan.Status == StartupPatchStatus.AlreadyPatched ? "已挂接过，跳过" : "未识别出标准插入点，将降级为手工指引"));
                if (plan.Instructions is not null) { lines.AddRange(plan.Instructions.Split("\r\n").Select(l => "   " + l)); }
            }
            else
            {
                lines.Add("3) 启动挂接（改前备份 .bak），挂接后的文件全文预览：");
                lines.Add(preview);
            }
        }
        else
        {
            lines.Add("3) 启动挂接：未开启，完成后提供手工指引");
        }

        lines.Add("");
        lines.Add("确认无误后点击\"确认执行\"。");
        return string.Join(Environment.NewLine, lines);
    }

    private async Task RunWiringAsync()
    {
        var csproj = _projectPath.Text;
        _preview.Visible = false;
        _runLog.Visible = true;
        _runLog.Text = "";
        _execute.Enabled = false;
        _back.Visible = false;

        void Log(string line) => BeginInvoke(() => { _runLog.AppendText(line + Environment.NewLine); });
        Log("> 开始接线");
        if (_configToken.Checked)
        {
            Log("> 配置清单令牌...");
            ConfigureToken(Log);
        }

        var options = new WireOptions(csproj, SelectedUiKind, _manifestUrl.Text.Trim(), _autoAttach.Checked, UpdateCheckerVersion);
        WireResult result = await Task.Run(() => WireRunner.Run(options, Log));
        _lastResult = result;

        Log("> 编译冒烟验证...");
        var (ok, buildOutput) = await Task.Run(() => RunDotnetBuild(csproj));
        Log(buildOutput);
        if (!ok)
        {
            Log("> 冒烟失败，自动回滚本次修改...");
            WireRunner.Rollback(result);
            Log("> 已回滚：生成文件已删除，被修改文件已从 .bak 还原。请把上方日志发给公共库维护者。");
        }
        else
        {
            Log("> 冒烟通过。");
        }

        BeginInvoke(() =>
        {
            _doneSummary.Text = BuildDoneText(ok, result);
            ShowPage(4);
            _next.Text = "完成";
            _next.Visible = true;
        });
    }

    private string BuildDoneText(bool buildOk, WireResult result) =>
        (buildOk ? "接线完成！" : "接线已回滚（冒烟未通过）。") + "\r\n\r\n" +
        $"  扫描到的公共包：{(result.ScannedPackages.Count == 0 ? "（无）" : string.Join(", ", result.ScannedPackages))}\r\n" +
        $"  csproj：{(result.CsprojModified ? "已添加 Company.UpdateChecker 引用" : "已含引用，未改动")}\r\n" +
        $"  启动挂接：{result.StartupPatch.Status switch { StartupPatchStatus.Patched => "已自动挂接", StartupPatchStatus.AlreadyPatched => "此前已挂接", _ => "未自动挂接——请按下方指引手工加入一行" }}\r\n\r\n" +
        (result.StartupPatch.Instructions is null ? "" : "手工指引：\r\n" + result.StartupPatch.Instructions + "\r\n\r\n") +
        "后续：把改动（csproj / UpdateCheck/ / 入口文件）提交并走 PR 审批合并。";

    private static (bool Ok, string Output) RunDotnetBuild(string csproj)
    {
        var info = new ProcessStartInfo("dotnet", $"build \"{csproj}\" --nologo -v quiet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode == 0, output.Trim());
    }

    /// <summary>从 Git 凭据取 GitHub 令牌写入用户环境变量（私有清单 bearer 鉴权用；失败仅提示不中断）。</summary>
    private void ConfigureToken(Action<string> log)
    {
        try
        {
            var info = new ProcessStartInfo("git", "credential fill")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(info)!;
            process.StandardInput.Write("protocol=https\nhost=github.com\n\n");
            process.StandardInput.Close();
            var cred = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var token = cred.Split('\n').FirstOrDefault(l => l.StartsWith("password="))?["password=".Length..];
            if (string.IsNullOrEmpty(token))
            {
                log("    未取到 Git 凭据（可能未在本机登录 GitHub）：跳过，可稍后手工 setx UPDATE_MANIFEST_TOKEN");
                return;
            }

            Environment.SetEnvironmentVariable("UPDATE_MANIFEST_TOKEN", token, EnvironmentVariableTarget.User);
            log("    已写入用户环境变量 UPDATE_MANIFEST_TOKEN（重开 VS 后生效）");
        }
        catch (Exception ex)
        {
            log("    配置令牌失败（不影响接线）：" + ex.Message);
        }
    }

    private void Reset()
    {
        _projectPath.Text = "";
        _manifestUrl.Text = DefaultManifest;
        _autoAttach.Checked = true;
        _configToken.Checked = false;
        _uiConsole.Checked = true;
        _execute.Enabled = true;
        _lastResult = null;
    }
}
