<#
.SYNOPSIS
    发版通知：推送到企微/钉钉群机器人（内外网同事都能收到），本地演示则打印消息。

.DESCRIPTION
    企业微信与钉钉群机器人的 text 消息格式相同：
      { "msgtype": "text", "text": { "content": "<消息>" } }
    webhook 地址（含密钥）由 CI 的 SECRET 注入，脚本不落盘。
    内网 GitLab 场景：网络策略需放行内网 CI 到 IM 服务的出站 HTTPS。
#>
param(
    [string]$WebhookUrl = "",      # 留空 = 本地演示模式，只打印
    [string]$PackageId,
    [string]$Version,
    [string]$Notes = "",
    [switch]$Critical
)

$severity = if ($Critical) { "!! 重要更新（建议 3 个工作日内升级）" } else { "常规更新" }

$message = @"
[公共库发版] $PackageId $Version
变更说明: $Notes
重要程度: $severity
升级指引: 项目库修改 PackageReference 版本号 -> 本地验证 -> 提 PR 审批合并
版本清单: https://updates.company.com/manifest.json
"@

if ($WebhookUrl) {
    $payload = @{
        msgtype = 'text'
        text    = @{ content = $message }
    } | ConvertTo-Json
    Invoke-RestMethod -Uri $WebhookUrl -Method Post -Body $payload -ContentType 'application/json' | Out-Null
    Write-Host "    已推送群通知。"
} else {
    Write-Host $message -ForegroundColor Yellow
}
