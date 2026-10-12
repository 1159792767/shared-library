# LabVIEW 与 git 协作配置（LVMerge + 二进制纪律）

> 适用：公共库 `labview/` 目录下的所有 LabVIEW 源码（.lvlib/.vi/.ctl）。
> 核心矛盾：VI 是二进制文件，git 的文本级三方合并会直接损坏 VI。
> 必须做到两件事：**禁止 git 自动合并 VI + 冲突时走 LVMerge 工具**。

## 1. 仓库配置（每个 LabVIEW 开发者克隆后执行一次）

### 1.1 `.gitattributes`（提交进仓库，对所有人生效）

放公共库仓库根目录：

```gitattributes
# ---- LabVIEW 二进制文件：禁止文本级自动合并（否则 git 会生成损坏的 VI）----
*.vi     -merge binary
*.ctl    -merge binary
*.lvclass -merge binary
*.vit    -merge binary
*.lvlibp -merge binary
*.lvprog -merge binary

# ---- 文本类项目文件：正常合并 ----
*.lvlib  text
*.lvproj text
*.ini    text

# ---- 本地缓存/别名文件：不进库 ----
*.aliases export-ignore
*.lvlps  export-ignore
```

### 1.2 LVMerge 合并工具（配置在本地 git）

`git config --global`（一次配置，所有仓库生效；路径按 LabVIEW 版本调整）：

```bash
git config --global mergetool.lvmerge.cmd "\"C:/Program Files/National Instruments/LabVIEW 2024/resource/framework/merge/LVMerge.exe\" \"\$BASE\" \"\$REMOTE\" \"\$LOCAL\" -o \"\$MERGED\""
git config --global mergetool.lvmerge.trustExitCode false
git config --global mergetool.prompt false
```

> 注意：不同 LabVIEW 版本的 LVMerge 参数顺序可能有差异，接入时以本机
> `<LabVIEW>\resource\framework\merge\` 目录下 NI 文档为准核对一次。

冲突处理流程（合并 MR/PR 时遇到 VI 冲突）：

```bash
git merge feature/xxx     # VI 标记为冲突（不会自动合并，文件保持完好）
git mergetool -t lvmerge  # 弹出 LabVIEW 三方合并界面，人工确认后保存
git commit
```

## 2. 协作纪律（防"多人改包错乱"的 LabVIEW 专属闸门）

| 纪律 | 原因 |
|---|---|
| **改公共库 VI 前先声明锁定**：在群里/issue 登记要改的库文件名和预计完成时间 | VI 二进制冲突即使能 LVMerge，成本也高；事前错开比事后解决便宜得多 |
| **改动走分支 + MR/PR**，main 受保护，至少 1 人审批 + CI 绿灯 | 与 C# 包同一套闸门（见仓库 README） |
| **先 `git pull --rebase` 再合并**，后到者负责同步 | 让 git 把"先后到达"串行化，杜绝覆盖 |
| **合并前在本地打开 LabVIEW 验证**冲突文件可正常加载 | LVMerge 输出仍需人工确认语义正确 |
| **公共库改动尽量"加新 VI"而非"改旧 VI"** | 新文件天然零冲突；旧 VI 语义变更走主版本号（SemVer MAJOR） |

## 3. LabVIEW 公共库的打包分发（PPL）

- 公共库以 Packed Project Library（`.lvlibp`）形式对外分发：版本化、单文件、加载快、防误改源码。
- CI 发布时（Windows 自托管 runner）：
  1. 打开公共库项目，右键 lvlib → Build → Packed Library，输出命名含版本号（如 `Company.CommonLibs_2.3.1.lvlibp`）；
  2. 产物挂 GitLab Release/GitHub Release 附件；
  3. 版本清单 `manifest.json` 中登记 `type: "ppl"` 的下载地址（与 C# 包同一条链路）。
- 项目库（LabVIEW 项目）通过"安装到指定目录 + 项目引用 PPL"消费；升级 = 替换 PPL 文件 + 版本号变更走 MR。

## 4. CI 中的 LabVIEW 冒烟测试（占位说明）

CI 配置（`.github/workflows/release.yml` / `.gitlab-ci.yml`）已预留注释位置：

- 前提：自托管 Windows runner 安装 LabVIEW 开发环境 + G CLI（JKI 出品的命令行工具，可无界面运行 VI）。
- 写一个 `labview/tests/Smoke Test.vi`：批量调用公共库各 VI，断言预期输出（等价于 C# 单元测试），错误时返回非零。
- CI 命令：`g-cli --kill-if-needed run "labview/tests/Smoke Test.vi"`——失败即中止发布。
