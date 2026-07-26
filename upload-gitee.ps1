# upload-gitee.ps1
# Upload all ImageTool-<version>-*.zip from publish/ to the Gitee Release.
# Requires env: GITEE_TOKEN (Gitee private token, projects scope), VERSION.
# Usage: $env:GITEE_TOKEN="xxx"; $env:VERSION="1.0.1"; powershell -NoProfile -File upload-gitee.ps1

$ErrorActionPreference = 'Stop'

$token   = $env:GITEE_TOKEN
$version = $env:VERSION
if (-not $token)   { Write-Error "GITEE_TOKEN not set"; exit 1 }
if (-not $version) { Write-Error "VERSION not set"; exit 1 }

$owner = 'yangyuanlife'
$repo  = 'ImageTool'
$tag   = "v$version"
$apiBase = "https://gitee.com/api/v5/repos/$owner/$repo"

# 1. Check if a Release with the same tag already exists
$getUrl = "$apiBase/releases/tags/$tag"
try {
    $rel = Invoke-RestMethod -Uri "$getUrl`?access_token=$token" -Method Get -TimeoutSec 30
    Write-Output "Gitee: Release $tag already exists (id=$($rel.id)), reusing it"
} catch [System.Net.WebException] {
    $resp = $_.Exception.Response
    if ($resp -and $resp.StatusCode -eq 404) {
        # 2. Create it if missing
        $createBody = @{
            access_token      = $token
            tag_name          = $tag
            name              = $tag
            body              = "ImageTool $tag`n`nWindows screenshot / image tool.`n- win-x64 self-contained single file (recommended)`n- win-arm64 self-contained single file (ARM Windows)`n- win-x64 framework-dependent (needs .NET 10 runtime, smaller)"
            target_commitish  = 'master'
        } | ConvertTo-Json -Compress
        $rel = Invoke-RestMethod -Uri "$apiBase/releases" -Method Post `
            -Body $createBody -ContentType 'application/json' -TimeoutSec 30
        Write-Output "Gitee: Created Release $tag (id=$($rel.id))"
    } else {
        throw
    }
}

$releaseId = $rel.id

# 3. Upload each zip (Gitee attachments use multipart/form-data)
$zips = Get-ChildItem "publish\ImageTool-$version-*.zip"
if ($zips.Count -eq 0) { Write-Error "No ImageTool-$version-*.zip found under publish/"; exit 1 }

$boundary  = [System.Guid]::NewGuid().ToString()
$uploadUrl = "$apiBase/releases/$releaseId/attach_files"

foreach ($zip in $zips) {
    Write-Output ("Gitee: Uploading {0} ({1:N1} MB) ..." -f $zip.Name, ($zip.Length / 1MB))

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
    Write-Output ("  -> Done: {0}" -f $result.url)
}

Write-Output "Gitee: All uploads done -> https://gitee.com/$owner/$repo/releases/$tag"
