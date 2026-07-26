@echo off
:: ============================================================================
::  ImageTool 一键发布脚本
::  功能: 1) 多目标发布（自包含单文件 + 框架依赖） 2) 打包到 publish/ 目录
::        3) 优先用 gh CLI 发布到 GitHub Release；未安装 gh 时若设了 GITHUB_TOKEN
::           则回退 PowerShell API
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

:: ---- 输出目录 ----
set OUT=publish
if not exist "%OUT%" mkdir "%OUT%"

:: ---- 定义变体 ----
::  1. win-x64 自包含单文件（推荐，双击即跑，无需装 .NET）
::  2. win-arm64 自包含单文件（ARM Windows，如 Surface Pro X）
::  3. win-x64 框架依赖（需装 .NET 10 运行时，体积小 ~80%）
::
::  WPF 是 Windows-only 技术，不支持 Linux/macOS，所以只发 Windows RID。

echo ============================================================
echo   ImageTool 发布工具  v%VERSION%
echo   产出目录: %OUT%\
echo ============================================================

:: ========================================================================
:: 变体 1: win-x64 自包含单文件（主力版本）
:: ========================================================================
set RID=win-x64
set BUILD=bin\publish\%RID%-self-contained
set ZIP=%OUT%\ImageTool-%VERSION%-%RID%-self-contained.zip
set FOLDER=ImageTool-%VERSION%-%RID%-self-contained

echo.
echo [1/3] 自包含单文件发布 (%RID%) ...
dotnet publish -c Release -r %RID% --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=none -p:EnableCompressionInBuild=true ^
    -o "%BUILD%"
if errorlevel 1 ( echo [FAIL] %RID% 自包含发布失败 & exit /b 1 )

echo   打包 %ZIP% ...
if exist "%FOLDER%" rmdir /s /q "%FOLDER%"
if exist "%ZIP%" del /q "%ZIP%"
mkdir "%FOLDER%"
xcopy /e /i /q "%BUILD%" "%FOLDER%" >nul
powershell -NoProfile -Command "Compress-Archive -Path '%FOLDER%' -DestinationPath '%ZIP%' -Force"
rmdir /s /q "%FOLDER%"
rmdir /s /q "%BUILD%"
echo   完成: %ZIP%

:: ========================================================================
:: 变体 2: win-arm64 自包含单文件
:: ========================================================================
set RID=win-arm64
set BUILD=bin\publish\%RID%-self-contained
set ZIP=%OUT%\ImageTool-%VERSION%-%RID%-self-contained.zip
set FOLDER=ImageTool-%VERSION%-%RID%-self-contained

echo.
echo [2/3] 自包含单文件发布 (%RID%) ...
dotnet publish -c Release -r %RID% --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=none -p:EnableCompressionInBuild=true ^
    -o "%BUILD%"
if errorlevel 1 (
    echo   [WARN] %RID% 自包含发布失败（可能缺少 ARM64 目标包），跳过此变体。
    echo   如需支持 ARM64，请运行: dotnet workload install windows-desktop-arm64
    goto :skip_arm64
)

echo   打包 %ZIP% ...
if exist "%FOLDER%" rmdir /s /q "%FOLDER%"
if exist "%ZIP%" del /q "%ZIP%"
mkdir "%FOLDER%"
xcopy /e /i /q "%BUILD%" "%FOLDER%" >nul
powershell -NoProfile -Command "Compress-Archive -Path '%FOLDER%' -DestinationPath '%ZIP%' -Force"
rmdir /s /q "%FOLDER%"
rmdir /s /q "%BUILD%"
echo   完成: %ZIP%
:skip_arm64

:: ========================================================================
:: 变体 3: win-x64 框架依赖（轻量版，需装 .NET 10 运行时）
:: ========================================================================
set RID=win-x64
set BUILD=bin\publish\%RID%-framework-dependent
set ZIP=%OUT%\ImageTool-%VERSION%-%RID%-framework-dependent.zip
set FOLDER=ImageTool-%VERSION%-%RID%-framework-dependent

echo.
echo [3/3] 框架依赖发布 (%RID%，需 .NET 10 运行时) ...
dotnet publish -c Release -r %RID% --self-contained false ^
    -p:DebugType=none ^
    -o "%BUILD%"
if errorlevel 1 ( echo [FAIL] %RID% 框架依赖发布失败 & exit /b 1 )

echo   打包 %ZIP% ...
if exist "%FOLDER%" rmdir /s /q "%FOLDER%"
if exist "%ZIP%" del /q "%ZIP%"
mkdir "%FOLDER%"
xcopy /e /i /q "%BUILD%" "%FOLDER%" >nul
powershell -NoProfile -Command "Compress-Archive -Path '%FOLDER%' -DestinationPath '%ZIP%' -Force"
rmdir /s /q "%FOLDER%"
rmdir /s /q "%BUILD%"
echo   完成: %ZIP%

:: ========================================================================
:: 上传到 GitHub Release
:: ========================================================================
echo.
echo === 上传到 GitHub Release ===

:: 收集所有成功生成的 zip
set ZIPS=
for %%f in ("%OUT%\ImageTool-%VERSION%-*.zip") do set ZIPS=!ZIPS! "%%f"
if "%ZIPS%"=="" (
    echo   [ERROR] 没有生成任何 zip，跳过上传。
    goto :done
)

where gh >nul 2>nul (
    echo   检测到 gh CLI，优先用它发布 ...
    gh release view "v%VERSION%" >nul 2>nul && (
        echo   v%VERSION% 已存在，覆盖上传附件 (--clobber) ...
        gh release upload "v%VERSION%" %ZIPS% --clobber
    ) || (
        echo   创建 v%VERSION% 并上传附件 ...
        gh release create "v%VERSION%" %ZIPS% ^
            --title "v%VERSION%" ^
            --notes "ImageTool %VERSION%

**下载说明：**
- `win-x64-self-contained.zip` — 推荐，单文件双击即跑，无需装 .NET
- `win-arm64-self-contained.zip` — ARM Windows（如 Surface Pro X）
- `win-x64-framework-dependent.zip` — 需装 [.NET 10 运行时](https://dotnet.microsoft.com/download/dotnet/10.0)，体积小 ~80%"
    )
) else if defined GITHUB_TOKEN (
    echo   未安装 gh，回退到 GITHUB_TOKEN (PowerShell API) ...
    set "GH_REPO=yangyuanlife/ImageTool"
    set "GH_TAG=v%VERSION%"
    set "PS=%TEMP%\it_upload_%VERSION%.ps1"
    (
        echo $ErrorActionPreference='Stop'
        echo $repo=$env:GH_REPO
        echo $tag=$env:GH_TAG
        echo $tok=$env:GITHUB_TOKEN
        echo $base="https://api.github.com/repos/$repo"
        echo $h=@{Authorization="Bearer $tok"}
        echo try { $rel=Invoke-RestMethod -Headers $h -Uri "$base/releases/tags/$tag" }
        echo catch { $body=@{tag_name=$tag; name=$tag; body="ImageTool $tag"; draft=$false; prerelease=$false} ^| ConvertTo-Json; $rel=Invoke-RestMethod -Headers $h -Method Post -Body $body -ContentType 'application/json' -Uri "$base/releases" }
        echo $h2=$h.Clone(); $h2['Content-Type']='application/zip'
        echo $dir=Resolve-Path "publish"
        echo Get-ChildItem "$dir\ImageTool-$($env:VERSION)-*.zip" ^| ForEach-Object {
        echo     $url=$rel.upload_url.Replace('{?name,label}','?name='+$_.Name)
        echo     Write-Output "上传 $($_.Name) ..."
        echo     Invoke-RestMethod -Headers $h2 -Method Post -InFile $_.FullName -Uri $url
        echo }
        echo Write-Output "OK: 所有附件已上传到 $tag"
    ) > "%PS%"
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS%"
    if exist "%PS%" del /q "%PS%"
) else (
    echo   [SKIP] 未安装 gh 且未设置 GITHUB_TOKEN，跳过 Release 上传。
    echo   手动上传: GitHub 仓库 Releases -^> v%VERSION% -^> 把 %OUT%\ 下的 zip 拖进去即可。
)

:done
echo.
echo ============================================================
echo   全部完成。产物:
dir /b "%OUT%\ImageTool-%VERSION%-*.zip" 2>nul
echo ============================================================
endlocal
