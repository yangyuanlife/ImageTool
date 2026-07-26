$version = $env:VERSION
$out = $env:NOTES_DIR
if (-not $out) { $out = "Release\v$version" }
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out | Out-Null }
$notesPath = Join-Path $out "release notes.txt"
$content = @"
ImageTool v$version

Windows 截图 / 图片处理工具。

下载说明：
- ImageTool-$version-win-x64-self-contained.zip - 推荐，单文件双击即跑，无需安装 .NET
- ImageTool-$version-win-arm64-self-contained.zip - ARM Windows（如 Surface Pro X）
- ImageTool-$version-win-x64-framework-dependent.zip - 需安装 .NET 10 运行时，体积小

更新内容：
- （请在此填写本次更新内容）
"@
[System.IO.File]::WriteAllText($notesPath, $content, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "Generated release notes: $notesPath"
