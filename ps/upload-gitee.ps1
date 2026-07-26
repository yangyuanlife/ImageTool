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
$notesPath = $env:NOTES_PATH
if ($notesPath -and (Test-Path $notesPath)) {
    $notes = [System.IO.File]::ReadAllText($notesPath, $enc8)
} else {
    $notes = "ImageTool $tag`n`nWindows screenshot / image tool."
}

# 1) 获取或创建 Release（GET/JSON 用 Invoke-RestMethod 没问题）
$getUrl = "$apiBase/releases/tags/$tag`?access_token=$token"
try {
    $rel = Invoke-RestMethod -Uri $getUrl -Method Get -TimeoutSec 30
    Write-Output "Gitee: Release $tag exists (id=$($rel.id))"
} catch [System.Net.WebException] {
    $resp = $_.Exception.Response
    if ($resp -and $resp.StatusCode -eq 404) {
        $createBody = @{
            tag_name         = $tag
            name             = $tag
            body             = $notes
            target_commitish = 'master'
        } | ConvertTo-Json -Compress
        $rel = Invoke-RestMethod -Uri "$apiBase/releases`?access_token=$token" -Method Post -Body $createBody -ContentType 'application/json' -TimeoutSec 30
        Write-Output "Gitee: created Release $tag (id=$($rel.id))"
    } else {
        throw
    }
}
$releaseId = $rel.id
Write-Output "Gitee: target release id = $releaseId"

# 2) 用系统自带 curl.exe 上传附件（生成标准 multipart，规避手写字节流的 405 坑）
$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $curl) {
    Write-Error "curl.exe not found；Windows 10/11 自带 curl，请确认 PATH 含 System32"
    exit 1
}

$releaseDir = $env:RELEASE_DIR
if (-not $releaseDir) { $releaseDir = "Release\v$version" }
$zips = Get-ChildItem "$releaseDir\ImageTool-$version-*.zip"
if ($zips.Count -eq 0) { Write-Error "no ImageTool-$version-*.zip under $releaseDir"; exit 1 }

$uploadUrl = "$apiBase/releases/$releaseId/attach_files"
Write-Output ("Gitee: upload url = {0}" -f ($uploadUrl -replace [regex]::Escape($token), '***'))

foreach ($zip in $zips) {
    Write-Output ("Gitee: uploading {0} ({1:N1} MB) ..." -f $zip.Name, ($zip.Length / 1MB))
    $raw = & curl.exe -sS -X POST $uploadUrl -F "access_token=$token" -F "file=@$($zip.FullName)" 2>&1
    $curlExit = $LASTEXITCODE
    $result = $raw | Out-String
    if ($result -match '"id"\s*:') {
        Write-Output "  -> done"
    } elseif ($result -match '重复|已存在|exist|already') {
        Write-Output ("  -> already exists, skip: {0}" -f $zip.Name)
    } else {
        Write-Error ("  -> upload failed (curl exit {0}): {1}" -f $curlExit, $result.Trim())
        exit 1
    }
}

Write-Output "Gitee: all uploaded -> https://gitee.com/$owner/$repo/releases/$tag"
