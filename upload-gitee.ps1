# upload-gitee.ps1
# 把 publish/ 下所有 ImageTool-<version>-*.zip 上传到 Gitee Release。
# 前置: 设置环境变量 GITEE_TOKEN（Gitee 私人令牌，勾 projects 权限）和 VERSION（版本号）。
# 用法: $env:GITEE_TOKEN="xxx"; $env:VERSION="1.0.1"; powershell -NoProfile -File upload-gitee.ps1
# 编码: 本文件为 UTF-8 带 BOM，中文可正常显示。

$ErrorActionPreference = 'Stop'

$token   = $env:GITEE_TOKEN
$version = $env:VERSION
if (-not $token)   { Write-Error "GITEE_TOKEN 未设置"; exit 1 }
if (-not $version) { Write-Error "VERSION 未设置"; exit 1 }

$owner = 'yangyuanlife'
$repo  = 'ImageTool'
$tag   = "v$version"
$apiBase = "https://gitee.com/api/v5/repos/$owner/$repo"

# ---- 1. 查是否已有同 tag 的 Release ----
$getUrl = "$apiBase/releases/tags/$tag`?access_token=$token"
try {
    $rel = Invoke-RestMethod -Uri $getUrl -Method Get -TimeoutSec 30
    Write-Output "Gitee: Release $tag 已存在 (id=$($rel.id))，复用之"
} catch [System.Net.WebException] {
    $resp = $_.Exception.Response
    if ($resp -and $resp.StatusCode -eq 404) {
        # ---- 2. 不存在则创建 ----
        $createBody = @{
            access_token     = $token
            tag_name         = $tag
            name             = $tag
            body             = "ImageTool $tag`n`nWindows 截图 / 图片处理工具。`n- win-x64 自包含单文件（推荐，双击即跑）`n- win-arm64 自包含单文件（ARM Windows）`n- win-x64 框架依赖（需装 .NET 10 运行时，体积小）"
            target_commitish = 'master'
        }
        $rel = Invoke-RestMethod -Uri "$apiBase/releases" -Method Post -Body $createBody -TimeoutSec 30
        Write-Output "Gitee: 创建 Release $tag (id=$($rel.id))"
    } else {
        throw
    }
}

$releaseId = $rel.id

# ---- 3. 逐个上传 zip（Gitee 附件为 multipart/form-data）----
$zips = Get-ChildItem "publish\ImageTool-$version-*.zip"
if ($zips.Count -eq 0) { Write-Error "publish/ 下未找到任何 ImageTool-$version-*.zip"; exit 1 }

# 关键: access_token 必须放在 URL query 参数里（Gitee 强制要求），否则返回 405
$uploadBase = "$apiBase/releases/$releaseId/attach_files`?access_token=$token"

foreach ($zip in $zips) {
    Write-Output ("Gitee: 上传 {0} ({1:N1} MB) ..." -f $zip.Name, ($zip.Length / 1MB))
    try {
        $result = Invoke-RestMethod -Uri "$uploadBase&name=$($zip.Name)" -Method Post `
            -Form @{ file = Get-Item -Path $zip.FullName } -TimeoutSec 600
        Write-Output ("  -> 完成: {0}" -f $result.url)
    } catch {
        $msg = $_.Exception.Message
        # Gitee 对同名附件重复上传返回 400 且含"重复/已存在"字样，视为已存在跳过
        if ($msg -match '重复|已存在|exist|already') {
            Write-Output ("  -> 已存在，跳过: {0}" -f $zip.Name)
        } else {
            Write-Error ("  -> 上传失败: {0}" -f $msg)
            exit 1
        }
    }
}

Write-Output "Gitee: 全部上传完成 -> https://gitee.com/$owner/$repo/releases/$tag"
