# 共享库（公共库 monorepo）

公司标准化公共包的唯一发布源。项目库（A/B/C 各项目）**只通过"带版本号的包产物"引用本仓库**，不直接引用源码。

## 目录结构

```
shared-library/
├─ packages/
│  ├─ csharp/                    # C# 公共包（NuGet），每包一个目录 + 必配测试
│  │  ├─ Company.UpdateChecker/  #   程序内更新检查模块（公共库的第一个包）
│  │  └─ Company.Drivers/        #   示例业务包
├─ tests/                        # 单元测试（每包必有测试，CI 门禁）
├─ labview/                      # LabVIEW 公共库（源码 + LVMerge 协作规范 + VI 接口规格）
├─ tools/
│  ├─ update-manifest.ps1        # 本地模拟 CI 发布（打包→推源→更新清单→通知）
│  ├─ notify-im.ps1              # 群机器人通知（企微/钉钉，内外网同事都收到）
│  ├─ manifest/manifest.json     # 版本清单（生产部署在内外网可达的只读地址）
│  └─ nuget-feed/                # 本地文件夹 NuGet 源（模拟私有 Registry）
├─ .github/workflows/release.yml # 发布流水线（GitHub 阶段）
├─ .gitlab-ci.yml                # 发布流水线（GitLab 阶段，逻辑一一对应）
├─ Directory.Build.props         # 全局构建属性
└─ Directory.Packages.props      # 中央包管理：仓库内部依赖版本统一在这改
```

## 协作纪律（多人改包不错乱的四道闸门）

| 闸门 | 机制 | 防什么 |
|---|---|---|
| **1. 分支保护** | main 禁止直推；改动必须走 MR/PR，**至少 1 人审批 + CI 全绿**才可合并；后合并者必须先同步最新 main | 互相覆盖、未审查的改动 |
| **2. CI 独占发布** | 正式版本只从 git tag 发布（`<包名>v<版本>`，如 `Company.Drivers-v2.3.1`），发布凭据（CI token）不下发开发机 | 幽灵版本、手滑发版 |
| **3. 版本号单源** | 版本号来自 tag，人不手填；语义遵循 SemVer（主.次.修订[-预发布]） | 版本号冲突、语义不清 |
| **4. 消费端锁文件** | 项目库用 `packages.lock.json` 锁定精确版本，升级必须显式改 `PackageReference` 并走 PR | 坏版本瞬间击穿所有项目 |

**LabVIEW 追加闸门**：VI 是二进制——改公共库 VI 前先在群里声明锁定；仓库已配 `.gitattributes` 禁止文本级自动合并，冲突走 LVMerge（见 `labview/git-LVMerge-配置.md`）。

## 修改并发布一个包（标准流程）

```bash
git checkout -b fix/drivers-retry     # 1. 分支
# 2. 先写失败测试（TDD），再改实现，dotnet test 全绿
git push && # 3. 提 MR，等审批（闸门 1）
git checkout main && git merge ...    # 4. 合并
git tag Company.Drivers-v2.3.1        # 5. 打 tag
git push --tags                       # 6. CI 自动：测试→打包→推私有源→更新清单→群通知
```

本地无 git 环境验证链路时用模拟脚本（不影响真实源）：

```powershell
cd tools
.\update-manifest.ps1 -PackageId Company.Drivers -Version 2.3.1 -Notes "修复断连重试缺陷" -Critical
```

## 推送通知的三个通道

1. **群机器人**（开发者即时收到）：CI 发布后 POST 企微/钉钉 webhook，消息含包名/版本/变更说明/升级指引；
2. **程序内 UpdateChecker**（项目程序运行时收到）：程序启动拉版本清单对比，程序内弹出"有新版本"（见 `project-demo` 演示）；
3. **Renovate 依赖看板**（项目库管理用）：接入 Renovate 后自动跟踪各项目库可升级依赖（生产接入见迁移清单）。

## 版本清单（manifest.json）

发布产物的"账本"，部署在**内外网都可达的只读地址**（前期 GitHub raw；后期公司 DMZ 静态文件服务）——这是外网同事也能收到推送的关键：通知与程序内提示都从这个只读 JSON 感知新版本，不需要暴露内网 GitLab。

## GitHub → GitLab 迁移清单

| 组件 | 前期 GitHub | 后期 GitLab | 迁移动作 |
|---|---|---|---|
| C# 私有源 | GitHub Packages | GitLab NuGet Registry | 项目库 nuget.config 改源地址 |
| CI | release.yml | .gitlab-ci.yml（已备好） | 启用对应文件 |
| 版本清单 | GitHub raw/Pages | DMZ 静态服务 | UpdateChecker 的清单地址改配置 |
| 群通知 | 相同 | 相同（网络放行出站 HTTPS） | 无 |
| 分支保护/MR | 仓库设置 | 仓库设置 | 按清单重配一遍 |
