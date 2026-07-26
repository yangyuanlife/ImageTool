$ErrorActionPreference = 'Stop'

$repo = $env:GH_REPO
if (-not $repo) { $repo = 'yangyuanlife/ImageTool' }
$tag  = $env:GH_TAG
if (-not $tag)  { $tag = "v$($env:VERSION)" }
$tok  = $env:GITHUB_TOKEN
if (-not $tok)  { Write-Output '[Skip] GITHUB_TOKEN not set, skip GitHub upload.'; exit 0 }

$e = [System.Text.Encoding]
$notesPath = $env:NOTES_PATH
if ($notesPath -and (Test-Path $notesPath)) {
    $notes = [System.IO.File]::ReadAllText($notesPath, $e::UTF8)
} else {
    $notes = "ImageTool $tag"
}

$base = "https://api.github.com/repos/$repo"
$h = @{
    Authorization = "Bearer $tok"
    'User-Agent'  = 'ImageTool-Release'
}

# 1) 取/建 Release（JSON 小请求，无需进度条，但打印状态）
try {
    Write-Output "GitHub: 查询 Release $tag ..."
    $rel = Invoke-RestMethod -Headers $h -Uri "$base/releases/tags/$tag"
    Write-Output "GitHub: Release $tag 已存在 (id=$($rel.id))，更新说明 ..."
    $patch = @{ body = $notes } | ConvertTo-Json -Compress
    Invoke-RestMethod -Headers $h -Method Patch -Body $patch -ContentType 'application/json' -Uri "$base/releases/$($rel.id)" | Out-Null
} catch {
    Write-Output "GitHub: Release $tag 不存在，创建中 ..."
    $body = @{
        tag_name   = $tag
        name       = $tag
        body       = $notes
        draft      = $false
        prerelease = $false
    } | ConvertTo-Json -Compress
    $rel = Invoke-RestMethod -Headers $h -Method Post -Body $body -ContentType 'application/json' -Uri "$base/releases"
    Write-Output "GitHub: 已创建 Release $tag (id=$($rel.id))"
}

# 2) 上传附件（大文件，用 curl 显示进度条，与 Gitee 一致）
$dir = $env:RELEASE_DIR
if (-not $dir) { $dir = "Release\v$env:VERSION" }
$files = Get-ChildItem "$dir\ImageTool-$($env:VERSION)-*.zip"
if ($files.Count -eq 0) { Write-Output '[Warn] no zip found, skip upload.'; exit 0 }

$curlCmd = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $curlCmd) {
    Write-Error '未找到 curl.exe（Windows 10/11 自带），无法上传 GitHub 附件。'
    exit 1
}

$uploadBase = $rel.upload_url -replace '\{\?name,label\}', ''
foreach ($f in $files) {
    $sizeMB = [math]::Round($f.Length / 1MB, 1)
    $name   = [System.Uri]::EscapeDataString($f.Name)
    $url    = "$uploadBase`?name=$name"
    Write-Output "GitHub: uploading $($f.Name) ($sizeMB MB) ..."
    $resp = & curl.exe -# -S --max-time 600 -X POST `
        -H "Authorization: Bearer $tok" `
        -H "Content-Type: application/zip" `
        -H "User-Agent: ImageTool-Release" `
        -w "`nHTTP_CODE:%{http_code}" `
        --data-binary "@$($f.FullName)" `
        $url
    $code = ''
    if ($resp -match 'HTTP_CODE:(\d+)') { $code = $Matches[1] }
    Write-Output "  -> curl exit=$LASTEXITCODE, HTTP=$code"
    if ($LASTEXITCODE -ne 0 -or ($code -and $code -notmatch '^2')) {
        Write-Output "  -> 响应: $resp"
        Write-Error "GitHub: 上传 $($f.Name) 失败"
        exit 1
    }
    Write-Output "  -> done"
}
Write-Output "OK: all assets uploaded to $tag"
