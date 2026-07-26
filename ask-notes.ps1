$ErrorActionPreference = 'Stop'

$timeout = 10
Write-Host ""
Write-Host "release notes 如上，是否需要修改？"
Write-Host "  【Y】修改    【N】不需要修改（默认）"
$end = [DateTime]::Now.AddSeconds($timeout)
$choice = $null
while ([DateTime]::Now -lt $end) {
    $remain = [math]::Max(0, [int]($end - [DateTime]::Now).TotalSeconds)
    Write-Host ("`r  剩余 {0,2} 秒，请按 [Y] 或 [N] ... " -f $remain) -NoNewline
    if ([Console]::KeyAvailable) {
        $k = [Console]::ReadKey($true)
        $c = $k.KeyChar
        if ($c -eq 'y' -or $c -eq 'Y') { $choice = 'Y'; break }
        if ($c -eq 'n' -or $c -eq 'N') { $choice = 'N'; break }
    }
    Start-Sleep -Milliseconds 150
}
Write-Host ""
if ($choice -eq 'Y') {
    Write-Host "  -> 已选择：修改"
    exit 0
} else {
    Write-Host "  -> 已选择（或超时未按键）：不需要修改"
    exit 1
}
