@echo off
:: ============================================================================
::  ImageTool 一键发布脚本
::  功能: 1) 自包含单文件发布(win-x64)  2) 打包成 ImageTool-<版本>-win-x64.zip
::        3) 优先用 gh CLI 发布；未安装 gh 时若设了 GITHUB_TOKEN 则回退 PowerShell API
::  用法: 双击运行（需先 `gh auth login` 一次），或  set GITHUB_TOKEN=xxx && publish-release.bat
::  说明: 推荐装 gh CLI（winget install --id GitHub.cli），`gh auth login` 浏览器授权后
::        发布全自动、无需手填 token，且 --clobber 可自动覆盖同名附件。无 gh 且无 token 时跳过第3步，
::        但仍会生成 zip，可手动到 GitHub Releases 拖入。
:: ============================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"

:: ---- 读取版本号 (从 ImageTool.csproj 的 <Version>) ----
for /f %%v in ('powershell -NoProfile -Command "(Select-Xml -Path ImageTool.csproj -XPath //Version).Node.InnerText"') do set VERSION=%%v
if "%VERSION%"=="" (
    echo [ERROR] 无法从 ImageTool.csproj 读取 ^<Version^>
    exit /b 1
)
set ZIP=ImageTool-%VERSION%-win-x64.zip
set PUB=bin\Release\net10.0-windows\win-x64\publish
set NAME=ImageTool-%VERSION%-win-x64

echo ============================================================
echo   ImageTool 发布工具  v%VERSION%
echo ============================================================

echo [1/3] 自包含单文件发布 (win-x64) ...
dotnet publish -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=none -p:EnableCompressionInBuild=true
if errorlevel 1 ( echo [FAIL] dotnet publish 失败 & exit /b 1 )

echo [2/3] 打包 %ZIP% ...
if exist "%NAME%" rmdir /s /q "%NAME%"
if exist "%ZIP%" del /q "%ZIP%"
mkdir "%NAME%"
xcopy /e /i /q "%PUB%" "%NAME%" >nul
powershell -NoProfile -Command "Compress-Archive -Path '%NAME%' -DestinationPath '%ZIP%' -Force"
rmdir /s /q "%NAME%"
echo   完成: %ZIP%

echo [3/3] 发布到 GitHub Release ...
:: 优先用 gh CLI（官方、稳、免手填 token）；其次回退 GITHUB_TOKEN + PowerShell API
where gh >nul 2>nul (
    echo   检测到 gh CLI，优先用它发布 ...
    gh release view "v%VERSION%" >nul 2>nul && (
        echo   v%VERSION% 已存在，覆盖上传附件 (--clobber) ...
        gh release upload "v%VERSION%" "%ZIP%" --clobber
    ) || (
        echo   创建 v%VERSION% 并上传附件 ...
        gh release create "v%VERSION%" "%ZIP%" --title "v%VERSION%" --notes "ImageTool %VERSION% 自包含单文件发布（图标已嵌入程序集，仅含单个 ImageTool.exe）。"
    )
) else if defined GITHUB_TOKEN (
    echo   未安装 gh，回退到 GITHUB_TOKEN (PowerShell API) ...
    set "GH_REPO=yangyuanlife/ImageTool"
    set "GH_TAG=v%VERSION%"
    set "GH_ZIP=%ZIP%"
    set "PS=%TEMP%\it_upload_%VERSION%.ps1"
    (
        echo $ErrorActionPreference='Stop'
        echo $repo=$env:GH_REPO
        echo $tag=$env:GH_TAG
        echo $zip=$env:GH_ZIP
        echo $tok=$env:GITHUB_TOKEN
        echo $base="https://api.github.com/repos/$repo"
        echo $h=@{Authorization="Bearer $tok"}
        echo try { $rel=Invoke-RestMethod -Headers $h -Uri "$base/releases/tags/$tag" }
        echo catch { $body=@{tag_name=$tag; name=$tag; body="ImageTool $tag"; draft=$false; prerelease=$false} ^| ConvertTo-Json; $rel=Invoke-RestMethod -Headers $h -Method Post -Body $body -ContentType 'application/json' -Uri "$base/releases" }
        echo $url=$rel.upload_url.Replace('{?name,label}','?name='+[IO.Path]::GetFileName($zip))
        echo $h2=$h.Clone(); $h2['Content-Type']='application/zip'
        echo Invoke-RestMethod -Headers $h2 -Method Post -InFile $zip -Uri $url
        echo Write-Output "OK: asset uploaded to $tag"
    ) > "%PS%"
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS%"
    if exist "%PS%" del /q "%PS%"
) else (
    echo   [SKIP] 未安装 gh 且未设置 GITHUB_TOKEN，跳过 Release 上传。
    echo   手动上传: GitHub 仓库 Releases -^> v%VERSION% -^> 把 %ZIP% 拖进去即可。
)

echo ============================================================
echo   全部完成。产物: %ZIP%
echo ============================================================
endlocal
