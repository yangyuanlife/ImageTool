$ErrorActionPreference = 'Continue'
$owner = 'yangyuanlife'
$repo  = 'ImageTool'
$tag   = "v$env:VERSION"
$staleName = "ImageTool-$env:VERSION-win-x64.zip"

Write-Output "检查 GitHub $tag 是否有旧附件 $staleName ..."
try {
    $json = gh api "repos/$owner/$repo/releases/tags/$tag/assets" 2>$null
    if (-not $json) { Write-Output "  无法读取附件列表（gh 未登录或网络问题），跳过清理"; exit 0 }
    $assets = $json | ConvertFrom-Json
} catch {
    Write-Output "  读取附件列表异常，跳过清理: $_"; exit 0
}

$stale = $assets | Where-Object { $_.name -eq $staleName }
if ($stale) {
    try {
        gh api -X DELETE "repos/$owner/$repo/releases/assets/$($stale.id)" 2>$null
        Write-Output "  已删除 GitHub 旧附件: $staleName"
    } catch {
        Write-Output "  删除旧附件失败: $_"
    }
} else {
    Write-Output "  无旧附件 $staleName，无需清理"
}
