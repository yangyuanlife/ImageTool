$ErrorActionPreference = 'Continue'
$owner = 'yangyuanlife'
$repo  = 'ImageTool'
$tag   = "v$env:VERSION"
$staleName = "ImageTool-$env:VERSION-win-x64.zip"

Write-Output "Checking GitHub $tag for stale asset $staleName ..."
try {
    $json = gh api "repos/$owner/$repo/releases/tags/$tag/assets" 2>$null
    if (-not $json) { Write-Output "  Cannot read asset list (gh not logged in or network issue), skip cleanup"; exit 0 }
    $assets = $json | ConvertFrom-Json
} catch {
    Write-Output "  Error reading asset list, skip cleanup: $_"; exit 0
}

$stale = $assets | Where-Object { $_.name -eq $staleName }
if ($stale) {
    try {
        gh api -X DELETE "repos/$owner/$repo/releases/assets/$($stale.id)" 2>$null
        Write-Output "  Deleted stale GitHub asset: $staleName"
    } catch {
        Write-Output "  Failed to delete stale asset: $_"
    }
} else {
    Write-Output "  No stale asset $staleName, nothing to clean"
}
