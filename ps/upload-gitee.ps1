$ErrorActionPreference = 'Stop'

$token   = $env:GITEE_TOKEN
$version = $env:VERSION
if (-not $token)   { Write-Error "GITEE_TOKEN not set"; exit 1 }
if (-not $version) { Write-Error "VERSION not set"; exit 1 }

$owner = 'yangyuanlife'
$repo  = 'ImageTool'
$tag   = "v$version"
$apiBase = "https://gitee.com/api/v5/repos/$owner/$repo"

$enc8 = [System.Text.Encoding]::UTF8
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$notesPath = $env:NOTES_PATH
if ($notesPath -and (Test-Path $notesPath)) {
    $notes = [System.IO.File]::ReadAllText($notesPath, $enc8)
} else {
    $notes = "ImageTool $tag`n`nWindows screenshot / image tool."
}

$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $curl) {
    Write-Error "curl.exe not found；Windows 10/11 自带 curl，请确认 PATH 含 System32"
    exit 1
}

# 统一用 curl 收发 JSON，并打印真实 HTTP 状态码，避免静默失败
function Invoke-GiteeJson {
    param($Method, $Url, $BodyFile)
    $args = @('-sS', '-w', "`n%{http_code}", '-X', $Method, $Url)
    if ($BodyFile) {
        $args += '-H', 'Content-Type: application/json'
        $args += '--data-binary', "@$BodyFile"
    }
    $out = & curl.exe @args 2>&1
    $exit = $LASTEXITCODE
    $lines = @($out -split "`n")
    $code = $lines[-1].Trim()
    $bodyText = ($lines[0..($lines.Length - 2)] -join "`n").Trim()
    [PSCustomObject]@{ Exit = $exit; Code = $code; Body = $bodyText }
}

# 1) 按 tag 查询 Release
Write-Output "Gitee: 查询 Release $tag ..."
$getUrl = "$apiBase/releases/tags/$tag`?access_token=$token"
$r = Invoke-GiteeJson -Method GET -Url $getUrl
$releaseId = $null
if ($r.Code -eq '200' -and $r.Body) {
    try {
        $rel = $r.Body | ConvertFrom-Json
        $releaseId = $rel.id
    } catch {
        Write-Warning ("Gitee: GET 返回非 JSON（code={0}）：{1}" -f $r.Code, ($r.Body.Substring(0, [Math]::Min(200, $r.Body.Length))))
    }
}

# 2) 查不到就创建
if (-not $releaseId) {
    Write-Output ("Gitee: Release $tag 不存在或查询失败（GET code={0}），创建中 ..." -f $r.Code)
    $createBody = @{
        tag_name         = $tag
        name             = $tag
        body             = $notes
        target_commitish = 'master'
    } | ConvertTo-Json -Compress
    $tmpBody = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmpBody, $createBody, $utf8NoBom)
    $c = Invoke-GiteeJson -Method POST -Url "$apiBase/releases`?access_token=$token" -BodyFile $tmpBody
    Remove-Item $tmpBody -Force -ErrorAction SilentlyContinue
    Write-Output ("Gitee: create 响应（code={0}）：{1}" -f $c.Code, ($c.Body.Substring(0, [Math]::Min(300, $c.Body.Length))))
    if ($c.Code -eq '201' -or $c.Code -eq '200') {
        try { $releaseId = ($c.Body | ConvertFrom-Json).id } catch {}
    }
}

# 3) 仍拿不到 id 就明确报错退出（不再带空 URL 硬传）
if (-not $releaseId) {
    Write-Error ("Gitee: 无法获取 release id（GET code={0}，创建 code={1}）。响应: {2}" -f $r.Code, $c.Code, $c.Body)
    exit 1
}
Write-Output ("Gitee: target release id = {0}" -f $releaseId)

# 4) 用系统 curl 上传附件（标准 multipart，规避手写字节流的 405）
$releaseDir = $env:RELEASE_DIR
if (-not $releaseDir) { $releaseDir = "Release\v$version" }
$zips = Get-ChildItem "$releaseDir\ImageTool-$version-*.zip"
if ($zips.Count -eq 0) { Write-Error "no ImageTool-$version-*.zip under $releaseDir"; exit 1 }

$uploadUrl = "$apiBase/releases/$releaseId/attach_files"
Write-Output ("Gitee: upload url = {0}" -f ($uploadUrl -replace [regex]::Escape($token), '***'))

foreach ($zip in $zips) {
    Write-Output ("Gitee: uploading {0} ({1:N1} MB) ..." -f $zip.Name, ($zip.Length / 1MB))
    $raw = & curl.exe -sS -# --max-time 600 -X POST $uploadUrl -F "access_token=$token" -F "file=@$($zip.FullName)" 2>&1
    $curlExit = $LASTEXITCODE
    $result = $raw | Out-String
    $preview = $result.Trim()
    if ($preview.Length -gt 400) { $preview = $preview.Substring(0, 400) }
    Write-Output ("  -> curl exit={0}，HTTP 响应: {1}" -f $curlExit, $preview)
    if ($result -match '"id"\s*:') {
        Write-Output "  -> done"
    } elseif ($result -match '重复|已存在|exist|already') {
        Write-Output ("  -> already exists, skip: {0}" -f $zip.Name)
    } else {
        Write-Error ("  -> upload failed: {0}" -f $preview)
        exit 1
    }
}

Write-Output "Gitee: all uploaded -> https://gitee.com/$owner/$repo/releases/$tag"
