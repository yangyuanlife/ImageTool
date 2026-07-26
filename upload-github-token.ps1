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

try {
    $rel = Invoke-RestMethod -Headers $h -Uri "$base/releases/tags/$tag"
    Write-Output "Release $tag already exists, updating notes and uploading assets ..."
    $patch = @{ body = $notes } | ConvertTo-Json -Compress
    Invoke-RestMethod -Headers $h -Method Patch -Body $patch -ContentType 'application/json' -Uri "$base/releases/$($rel.id)"
} catch {
    $body = @{
        tag_name   = $tag
        name       = $tag
        body       = $notes
        draft      = $false
        prerelease = $false
    } | ConvertTo-Json -Compress
    $rel = Invoke-RestMethod -Headers $h -Method Post -Body $body -ContentType 'application/json' -Uri "$base/releases"
    Write-Output "Created Release $tag ..."
}

$h2 = $h.Clone()
$h2['Content-Type'] = 'application/zip'
$dir = $env:RELEASE_DIR
if (-not $dir) { $dir = "Release\v$env:VERSION" }
$files = Get-ChildItem "$dir\ImageTool-$($env:VERSION)-*.zip"
if ($files.Count -eq 0) { Write-Output '[Warn] no zip found, skip upload.'; exit 0 }
foreach ($f in $files) {
    $url = $rel.upload_url.Replace('{?name,label}', "?name=$($f.Name)")
    Write-Output "Uploading $($f.Name) ..."
    Invoke-RestMethod -Headers $h2 -Method Post -InFile $f.FullName -Uri $url
}
Write-Output "OK: all assets uploaded to $tag"
