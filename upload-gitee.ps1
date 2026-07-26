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
$getUrl = "$apiBase/releases/tags/$tag"
try {
    $rel = Invoke-RestMethod -Uri "$getUrl`?access_token=$token" -Method Get -TimeoutSec 30
    Write-Output "Gitee: Release $tag 已存在 (id=$($rel.id))，复用之"
} catch [System.Net.WebException] {
    $resp = $_.Exception.Response
    if ($resp -and $resp.StatusCode -eq 404) {
        # ---- 2. 不存在则创建 ----
        $createBody = @{
            access_token      = $token
            tag_name          = $tag
            name              = $tag
            body              = "ImageTool $tag`n`nWindows 截图 / 图片处理工具。`n- win-x64 自包含单文件（推荐，双击即跑）`n- win-arm64 自包含单文件（ARM Windows）`n- win-x64 框架依赖（需装 .NET 10 运行时，体积小）"
            target_commitish  = 'master'
        } | ConvertTo-Json -Compress
        $rel = Invoke-RestMethod -Uri "$apiBase/releases" -Method Post `
            -Body $createBody -ContentType 'application/json' -TimeoutSec 30
        Write-Output "Gitee: 创建 Release $tag (id=$($rel.id))"
    } else {
        throw
    }
}

$releaseId = $rel.id

# ---- 3. 逐个上传 zip（Gitee 附件为 multipart/form-data）----
$zips = Get-ChildItem "publish\ImageTool-$version-*.zip"
if ($zips.Count -eq 0) { Write-Error "publish/ 下未找到任何 ImageTool-$version-*.zip"; exit 1 }

$boundary  = [System.Guid]::NewGuid().ToString()
$uploadUrl = "$apiBase/releases/$releaseId/attach_files"

foreach ($zip in $zips) {
    Write-Output ("Gitee: 上传 {0} ({1:N1} MB) ..." -f $zip.Name, ($zip.Length / 1MB))

    $fileBin  = [System.IO.File]::ReadAllBytes($zip.FullName)
    $fileName = $zip.Name

    $headerLines = @(
        "--$boundary",
        'Content-Disposition: form-data; name="access_token"',
        "",
        $token,
        "--$boundary",
        "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`"",
        "Content-Type: application/zip",
        ""
    )
    $bodyStart = [System.Text.Encoding]::UTF8.GetBytes(($headerLines -join "`r`n") + "`r`n")
    $bodyEnd   = [System.Text.Encoding]::UTF8.GetBytes("`r`n--$boundary--`r`n")

    $body = New-Object byte[] ($bodyStart.Length + $fileBin.Length + $bodyEnd.Length)
    [System.Buffer]::BlockCopy($bodyStart, 0, $body, 0, $bodyStart.Length)
    [System.Buffer]::BlockCopy($fileBin,   0, $body, $bodyStart.Length, $fileBin.Length)
    [System.Buffer]::BlockCopy($bodyEnd,   0, $body, $bodyStart.Length + $fileBin.Length, $bodyEnd.Length)

    $result = Invoke-RestMethod -Uri $uploadUrl -Method Post `
        -Body $body -ContentType "multipart/form-data; boundary=$boundary" -TimeoutSec 600
    Write-Output ("  -> 完成: {0}" -f $result.url)
}

Write-Output "Gitee: 全部上传完成 -> https://gitee.com/$owner/$repo/releases/$tag"
