# 公共库接线安装向导（Company.UpdateChecker.Setup）设计规格

> 日期：2026-10-12 ｜ 状态：已与需求方确认（方案=WinForms 向导；挂接=自动改码+预览+备份+降级）
>
> 背景：B/C 同事的项目要接入"程序内更新检查"，目前需手工加包引用、拷代码、贴启动调用。
> 目标：**双击式图形安装向导**，全程点鼠标完成接线，零命令行。

## 1. 产品形态

- **WinForms 向导 exe**：经典安装向导布局（左侧品牌栏 + 右侧内容区 + Back/Next/Cancel），对齐需求方提供的参考截图风格。
- 发布：framework-dependent、win-x64（约 1~2MB；团队成员装有 VS，.NET 桌面运行时必备）。
- 分发：CI publish → 挂 GitHub Release 附件 + 版本清单登记 `type:"setup"`；上线 GitLab 后放内网 HTTP/共享盘。
- 向导自身的升级走同一条 tag→CI 推送链路（旧向导打开时同样能收到新版提示）。

## 2. 向导流程（5 页）

| 页 | 内容 |
|---|---|
| 欢迎 | 说明"将为本项目接入公共库更新检查"，建议先提交当前代码 |
| 选择项目 | 浏览选 .csproj（选 .sln 时列出项目由用户选一个，批量接线见决策 6） |
| 选项 | ① UI 形态单选：控制台/WinForms/WPF ② 清单地址（默认当前 GitHub raw 地址，上线改一次默认值）③ "为本机配置清单令牌"复选（从 Git 凭据自动获取并写用户环境变量 UPDATE_MANIFEST_TOKEN，取不到时改为手动粘贴）④ 自动挂接复选（默认开，执行前展示差异预览） |
| 执行 | 进度+日志：扫描依赖 → 生成接线文件 → csproj 幂等加 PackageReference → 挂接启动调用（.bak 备份）→ `dotnet build` 冒烟 |
| 完成 | 绿勾结果 + 未自动完成事项（如降级为指引的挂接）+ "再接下一个项目" |

## 3. 代码结构（公共库 monorepo 内）

```
packages/csharp/
  Company.Wire.Core/               纯库（TDD 核心，net10.0，无 UI 依赖）
    DependencyScanner.cs           csproj → Company.* 依赖登记（XML 读取）
    CsprojEditor.cs                XML API 幂等添加 PackageReference(UpdateChecker)
    StartupPatcher.cs              三形态启动挂接（见决策 3）
    WiringGenerator.cs             生成 UpdateCheck/ 目录：UpdateCheckService.cs + UI 文件 + 接线说明.md
    WireOptions.cs / WireResult.cs 选项与结果（含每步执行日志、降级提示）
  Company.UpdateChecker.Setup/     WinForms 薄壳（net10.0-windows）
    Program.cs / WizardForm.cs     向导 UI + 调 Core + dotnet build 冒烟（Process 调 dotnet）
tests/
  Company.Wire.Core.Tests/         xUnit，Core 全量 TDD
```

`Company.Wire.Core` 不单独发 NuGet，作为 Setup 工程依赖同仓构建。

## 4. 关键设计决策

1. **依赖注册用程序集名而非类型名**：UpdateChecker 包新增 `AssemblyVersions.FromAssemblyName(string)`（1.2.0，TDD 先行）。生成的接线代码写 `new PackageDependency("Company.Drivers", AssemblyVersions.FromAssemblyName("Company.Drivers"))`——向导无需知道各包入口类型，新增包零向导改动。
2. **csproj 修改**：XDocument XML API，保留既有内容与格式；幂等（已有同名引用则不动，不重复不加版本）。
3. **启动挂接**（仅三种标准形态，其余一律降级为指引文本）：
   - 控制台顶级语句：在首个顶级语句前插入标记代码块；传统 Main：方法体开头。
   - WinForms：Program.cs 的 `Main` 内 `Application.Run` 前。
   - WPF：App.xaml.cs 的 `OnStartup` 内 `base.OnStartup(e)` 后；无显式文件则降级。
   - 统一标记 `// ===== Company UpdateCheck (auto-wired) =====`；已含标记跳过（幂等）；改动前写 `.bak` 备份并向导内预览差异。
4. **生成的接线代码行为**：检测失败静默降级（不弹错、不中断程序）；常规更新信息图标弹窗；critical 黄色警示图标 + "!!" 前缀；依赖列表由向导按扫描结果注入生成。
5. **冒烟验证**：执行页自动 `dotnet build` 接线后的项目，失败则展示错误日志并回滚本次生成/挂接文件（.bak 恢复）。
6. **本期不做**：LabVIEW 接线（已有接口规格文档，后续做 VIPM 包）；CLI/模板包形态；除三形态插入点外的任何用户代码改写；多项目批量接线（二期）。

## 5. 测试策略

- Core 全量 TDD：Scanner / Editor / Patcher / Generator 各自先写失败测试，覆盖幂等、备份、降级三分支、XML 保真。
- Setup 壳：CI build（Linux 上可编译 net10.0-windows 工程）+ 本机手动冒烟。
- 端到端验证：对 project-demo 的副本真实接线 → build → 运行收到 2.5.0 更新提示（复用现有已验证清单）。

## 6. 发布与清单

- `Company.UpdateChecker` 1.2.0：常规 tag 发布（新增 FromAssemblyName）。
- `Company.UpdateChecker.Setup`：release.yml 扩展——包名匹配 Setup 时改走 `dotnet publish -r win-x64`（framework-dependent），产物上传 Release 附件；`update-manifest.ps1` 增加产物类型/地址覆盖参数，登记 `type:"setup"` + 附件 URL。
- tag 约定不变：`Company.UpdateChecker.Setup-v1.0.0`。
