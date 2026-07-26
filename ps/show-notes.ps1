$path = $env:NOTES_PATH
if (-not $path) { Write-Error "NOTES_PATH not set"; exit 1 }
$e = [System.Text.Encoding]
$t = [System.IO.File]::ReadAllText($path, $e::UTF8)
[Console]::OutputEncoding = $e::GetEncoding('gbk')
Write-Host $t
