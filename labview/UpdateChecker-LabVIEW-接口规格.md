# UpdateChecker 的 LabVIEW 实现接口规格

> 目标：让 LabVIEW 项目程序和 C# 程序从**同一个版本清单**感知公共库新版本
> （`tools/manifest/manifest.json`，生产部署在内外网可达的只读 HTTP 地址）。
> 本文是给 LabVIEW 开发的实现规格：按此做一个库（建议 `Company.UpdateChecker.lvlib`），
> 各项目程序引用同一套 VI。

## 1. 主入口 VI：`Check Updates.vi`

| 连接器端子 | 方向 | 类型 | 说明 |
|---|---|---|---|
| manifest url | 输入 | string | 版本清单地址（演示：本地路径；生产：`https://updates.company.com/manifest.json`） |
| dependencies | 输入 | 簇数组 | 本项目当前依赖（簇定义见 2.1） |
| include prerelease | 输入 | boolean | 默认 FALSE：预发布版不提示（与 C# 包策略一致） |
| updates available | 输出 | 簇数组 | 检测到的更新（簇定义见 2.2） |
| error in / out | 常规 | error cluster | 清单不可达时输出错误但**不中断调用方**（现场设备降级策略） |

## 2. 数据结构（LabVIEW 簇定义）

### 2.1 输入簇：Package Dependency

| 字段 | 类型 | 对应 C# 包 |
|---|---|---|
| package id | string | `PackageDependency.PackageId` |
| current version | string | `PackageDependency.CurrentVersion`（如 "2.3.0"） |

### 2.2 输出簇：Package Update

| 字段 | 类型 | 对应 C# 包 |
|---|---|---|
| package id | string | `PackageUpdate.PackageId` |
| current version | string | `PackageUpdate.CurrentVersion` |
| latest version | string | `PackageUpdate.LatestVersion` |
| critical | boolean | `PackageUpdate.IsCritical`（重要更新强提示） |
| notes | string | `PackageUpdate.ReleaseNotes` |
| artifact url | string | `Artifacts[n].Url`（取第一个产物的下载地址） |

## 3. 实现要点

### 3.1 拉取清单
- LabVIEW 2023+：用自带 HTTP Client VIs（`GET` 返回字符串体）。
- 老版本：用 TCP IP + 手写 HTTP GET，或安装 REST/HTTP 工具包。
- 演示阶段：允许 manifest url 为本地路径，直接 `Read from Text File`。

### 3.2 解析 JSON
- `Unflatten From JSON`（LabVIEW 2014+ 内置）。
- 簇字段名必须与清单 JSON 键一致（大小写不敏感）：`generatedAt`、`packages`。
- `packages` 是"包名 → 条目"的映射：LabVIEW 解析动态键建议先用
  `Flatten To JSON`/字符串处理或让 CI 额外生成一份**数组形式**的清单
  （`packages_list: [{ id, latest, critical, notes, ... }]`，对 LabVIEW 更友好，
  由 `tools/update-manifest.ps1` 一并输出——接入时在脚本里加 10 行即可）。

### 3.3 版本比较（一期简化：三段数字）
```
比较 VI：Version Less Than.vi
输入：version A (string), version B (string)
逻辑：
  1. 用 "." 拆分为数字数组（Spreadsheet String To Array + Decimal String To Number）
  2. 逐段比较：主版本高者为大；主版本相同比次版本；再比修订号
  3. 预发布后缀（"-" 后的部分）：本地实现可先不比较——清单默认只登记正式版
  4. 解析失败（非数字段）→ 输出 error
输出：A < B (boolean)
```
提示规则（与 C# 包一致）：仅当 `清单版本 > 当前版本` 时输出更新条目；
清单滞后、包未登记、预发布（未开启开关）一律不提示。

### 3.4 项目程序集成方式
各项目程序启动 VI（或状态机空闲态定时）调用：

```
依赖数组（写死本项目用的包名+版本，或从项目常量 VI 读）
    ↓
Check Updates.vi
    ↓
updates available 数组非空？
    ├─ 是 → One Button Dialog："检测到公共库有新版本：<notes>，请联系开发人员升级"（critical 时用醒目提示/日志+弹窗）
    └─ 否 → 无操作
```

## 4. 与发布链路的衔接

公共库 CI 发布 LabVIEW 包（PPL）时：
1. PPL 产物挂 Release 附件（内外网可达的下载地址）；
2. `manifest.json` 登记：`{ "type": "ppl", "url": "https://updates.company.com/downloads/Company.CommonLibs_2.3.1.lvlibp" }`；
3. 群通知照常发出——C# 项目、LabVIEW 项目、程序内提示（C# 与 LabVIEW 两版 UpdateChecker）三条通道同步感知。
