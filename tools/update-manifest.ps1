<#
.SYNOPSIS
    模拟 CI 发布 job：打包公共包 -> 推送私有源 -> 更新版本清单 -> 群通知各项目组。

.DESCRIPTION
    本地验证用脚本，与 .github/workflows/release.yml / .gitlab-ci.yml 中的发布逻辑一一对应：
      1. 校验 SemVer 版本号
      2. dotnet pack（版本号由参数注入 = 生产中由 CI 按 git tag 决定）
      3. nupkg 推入本地文件夹源（= 生产中的 GitHub Packages / GitLab NuGet Registry）
      4. 更新 tools/manifest/manifest.json（= 生产中提交到 DMZ 静态清单服务）
      5. 调 notify-im.ps1 通知（= 生产中 POST 到企微/钉钉群机器人）

    CI 用法（发布 job 中）：-ManifestOnly —— 包已由 CI 单独发布，本脚本只执行
    第 4/5 步（清单更新 + 通知），与本地共用同一份清单写入逻辑，行为完全一致。

.EXAMPLE
    .\update-manifest.ps1 -PackageId Company.Drivers -Version 2.3.1 -Notes "修复 Modbus 断连重试缺陷" -Critical
#>
param(
    [Parameter(Mandatory = $true)] [string]$PackageId,
    [Parameter(Mandatory = $true)] [string]$Version,
    [string]$Notes = "",
    [switch]$Critical,
    [string]$WebhookUrl = "",   # 企微/钉钉群机器人 webhook；留空 = 本地演示只打印消息
    [switch]$ManifestOnly       # CI 复用：跳过打包/推源，只更新清单 + 通知
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot   # tools 的上级 = 公共库仓库根

# ---------- 1. 校验 SemVer ----------
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$') {
    throw "版本号 '$Version' 不符合 SemVer（期望：主.次.修订[-预发布]），发布中止。"
}

# ---------- 2. 打包 ----------
$csproj = Join-Path $RepoRoot "packages/csharp/$PackageId/$PackageId.csproj"
if (-not $ManifestOnly -and -not (Test-Path $csproj)) {
    throw "找不到公共包项目：$csproj"
}

if (-not $ManifestOnly) {
    $feedDir = Join-Path $PSScriptRoot "nuget-feed"
    Write-Host ">> [1/4] dotnet pack $PackageId $Version (Release)"
    dotnet pack $csproj -c Release -o $feedDir -p:Version=$Version --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "打包失败：$PackageId $Version（测试未通过或配置错误），发布中止。" }
} else {
    Write-Host ">> [1/4] -ManifestOnly：跳过打包（CI 已发布，仅更新清单 + 通知）"
}

# ---------- 3. 更新版本清单 ----------
$manifestPath = Join-Path $PSScriptRoot "manifest/manifest.json"
Write-Host ">> [2/4] 更新版本清单：$manifestPath"

if (Test-Path $manifestPath) {
    $manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    $manifest = New-Object PSObject -Property @{ generatedAt = ''; packages = New-Object PSObject }
}

$now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$entry = New-Object PSObject -Property ([ordered]@{
    latest     = $Version
    channel    = 'stable'
    critical   = [bool]$Critical
    releasedAt = $now
    notes      = $Notes
    artifacts  = @(
        (New-Object PSObject -Property ([ordered]@{
            type = 'nuget'
            url  = "https://updates.company.com/feed/$($PackageId.ToLower()).$($Version).nupkg"
        }))
    )
})

if ($manifest.packages.PSObject.Properties.Name -contains $PackageId) {
    $manifest.packages.$PackageId = $entry
} else {
    $manifest.packages | Add-Member -MemberType NoteProperty -Name $PackageId -Value $entry
}
$manifest.generatedAt = $now

$manifestDir = Split-Path -Parent $manifestPath
if (-not (Test-Path $manifestDir)) { New-Item -ItemType Directory -Path $manifestDir | Out-Null }
$manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding UTF8

# ---------- 4. 通知 ----------
Write-Host ">> [3/4] 发版通知"
& (Join-Path $PSScriptRoot "notify-im.ps1") -WebhookUrl $WebhookUrl -PackageId $PackageId -Version $Version -Notes $Notes -Critical:$Critical

Write-Host ">> [4/4] 发布完成：$PackageId $Version"
