$ErrorActionPreference = 'Stop'

function Send-GiteeFile {
    param(
        [string]$Url,
        [string]$FilePath,
        [string]$FileName
    )
    $boundary = [System.Guid]::NewGuid().ToString('N')
    $enc = [System.Text.Encoding]::UTF8
    $CRLF = $enc.GetBytes("`r`n")
    $fileBytes = [System.IO.File]::ReadAllBytes($FilePath)

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("--").Append($boundary).Append("`r`n")
    [void]$sb.Append("Content-Disposition: form-data; name=`"file`"; filename=`"$FileName`"`r`n")
    [void]$sb.Append("Content-Type: application/octet-stream`r`n`r`n")
    $headerBytes = $enc.GetBytes($sb.ToString())

    $ms = New-Object System.IO.MemoryStream
    $ms.Write($headerBytes, 0, $headerBytes.Length)
    $ms.Write($fileBytes, 0, $fileBytes.Length)
    $ms.Write($CRLF, 0, $CRLF.Length)
    $tail = $enc.GetBytes("--$boundary--`r`n")
    $ms.Write($tail, 0, $tail.Length)

    $req = [System.Net.HttpWebRequest]::Create($Url)
    $req.Method = 'POST'
    $req.ContentType = "multipart/form-data; boundary=$boundary"
    $req.UserAgent = 'ImageTool-Release'
    $req.Timeout = 600000
    $req.ContentLength = $ms.Length
    $stream = $req.GetRequestStream()
    $ms.Position = 0
    $ms.CopyTo($stream)
    $stream.Close()

    $resp = $req.GetResponse()
    $reader = New-Object System.IO.StreamReader($resp.GetResponseStream(), $enc)
    $out = $reader.ReadToEnd()
    $reader.Close()
    $resp.Close()
    return $out
}

$token   = $env:GITEE_TOKEN
$version = $env:VERSION
if (-not $token)   { Write-Error "GITEE_TOKEN not set"; exit 1 }
if (-not $version) { Write-Error "VERSION not set"; exit 1 }

$owner = 'yangyuanlife'
$repo  = 'ImageTool'
$tag   = "v$version"
$apiBase = "https://gitee.com/api/v5/repos/$owner/$repo"

$e = [System.Text.Encoding]

$notesPath = $env:NOTES_PATH
if ($notesPath -and (Test-Path $notesPath)) {
    $notes = [System.IO.File]::ReadAllText($notesPath, $e::UTF8)
} else {
    $notes = "ImageTool $tag`n`nWindows screenshot / image tool."
}

$getUrl = "$apiBase/releases/tags/$tag`?access_token=$token"
try {
    $rel = Invoke-RestMethod -Uri $getUrl -Method Get -TimeoutSec 30
    Write-Output "Gitee: Release $tag exists (id=$($rel.id)), reuse"
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

$releaseDir = $env:RELEASE_DIR
if (-not $releaseDir) { $releaseDir = "Release\v$version" }
$zips = Get-ChildItem "$releaseDir\ImageTool-$version-*.zip"
if ($zips.Count -eq 0) { Write-Error "no ImageTool-$version-*.zip under $releaseDir"; exit 1 }

$uploadBase = "$apiBase/releases/$releaseId/attach_files`?access_token=$token"

foreach ($zip in $zips) {
    Write-Output ("Gitee: uploading {0} ({1:N1} MB) ..." -f $zip.Name, ($zip.Length / 1MB))
    try {
        $result = Send-GiteeFile -Url "$uploadBase&name=$($zip.Name)" -FilePath $zip.FullName -FileName $zip.Name
        Write-Output ("  -> done: {0}" -f $result)
    } catch {
        $msg = $_.Exception.Message
        if ($msg -match '重复|已存在|exist|already') {
            Write-Output ("  -> already exists, skip: {0}" -f $zip.Name)
        } else {
            Write-Error ("  -> upload failed: {0}" -f $msg)
            exit 1
        }
    }
}

Write-Output "Gitee: all uploaded -> https://gitee.com/$owner/$repo/releases/$tag"
