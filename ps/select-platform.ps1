$ErrorActionPreference = 'Stop'

$timeout = 10
$default = 'ALL'
$tmp = Join-Path $env:TEMP 'imagetool_platform.txt'

Write-Host ""
Write-Host "请确认要发布的平台"
Write-Host "  1: GitHub"
Write-Host "  2: Gitee"
Write-Host "  3: 全部（默认）"
Write-Host "  请输入对应的序号："

$end = [DateTime]::Now.AddSeconds($timeout)
$choice = $null
while ([DateTime]::Now -lt $end) {
    $remain = [math]::Max(0, [int]($end - [DateTime]::Now).TotalSeconds)
    Write-Host ("`r  剩余 {0,2} 秒，按 1 / 2 / 3 ... " -f $remain) -NoNewline
    if ([Console]::KeyAvailable) {
        $k = [Console]::ReadKey($true)
        $c = $k.KeyChar
        if ($c -eq '1') { $choice = 'GITHUB'; break }
        if ($c -eq '2') { $choice = 'GITEE';  break }
        if ($c -eq '3') { $choice = 'ALL';    break }
    }
    Start-Sleep -Milliseconds 150
}
Write-Host ""

if (-not $choice) { $choice = $default }

[System.IO.File]::WriteAllText($tmp, $choice, [System.Text.Encoding]::ASCII)

switch ($choice) {
    'GITHUB' { Write-Host "  -> 已选择：仅发布 GitHub" }
    'GITEE'  { Write-Host "  -> 已选择：仅发布 Gitee" }
    'ALL'    { Write-Host "  -> 已选择（或超时默认）：发布全部（GitHub + Gitee）" }
}
