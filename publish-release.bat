@echo off
chcp 936 >nul
:: ============================================================================
::  ImageTool 一键发布脚本
::  功能: 1) 多目标发布（自包含单文件 + 框架依赖） 2) 打包到 publish/ 目录
::        3) GitHub Release：优先 gh CLI，未装 gh 则回退 GITHUB_TOKEN + PowerShell API（上传后自动清理旧版单文件附件）
::        4) Gitee  Release：设了 GITEE_TOKEN 则调 upload-gitee.ps1（OpenAPI）
::  用法: 双击运行（需先 `gh auth login` 一次），或
::        set GITHUB_TOKEN=xxx && set GITEE_TOKEN=yyy && publish-release.bat
::  说明: 推荐装 gh CLI（winget install --id GitHub.cli），`gh auth login` 浏览器授权后
::        GitHub 发布全自动、无需手填 token，且 --clobber 可自动覆盖同名附件。
::        无对应 token 时该平台跳过，但仍会生成 zip，可手动到对应 Releases 拖入。
::        Gitee 私人令牌: Gitee 设置 -> 私人令牌，勾 projects 权限。
::  编码: 本文件为 UTF-8 带 BOM；配合上方 chcp 936，中文提示可正常显示不乱码。
:: ============================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"

:: ---- 读取版本号 (从 ImageTool.csproj 的 <Version>) ----
for /f %%v in ('powershell -NoProfile -Command "(Select-Xml -Path ImageTool.csproj -XPath //Version).Node.InnerText"') do set VERSION=%%v
if "%VERSION%"=="" (
    echo [错误] 无法从 ImageTool.csproj 读取 ^<Version^>
    exit /b 1
)

:: ---- 输出目录 ----
set OUT=publish
if not exist "%OUT%" mkdir "%OUT%"

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
if errorlevel 1 ( echo [失败] %RID% 自包含发布失败 & exit /b 1 )

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
    echo   [警告] %RID% 自包含发布失败（可能缺少 ARM64 目标包），跳过此变体。
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
:: 变体 3: win-x64 框架依赖（需 .NET 10 运行时）
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
if errorlevel 1 ( echo [失败] %RID% 框架依赖发布失败 & exit /b 1 )

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
for %%f in ("%OUT%\ImageTool-%VERSION%-*.zip") do if exist "%%f" set ZIPS=!ZIPS! "%%f"
if "%ZIPS%"=="" (
    echo   [错误] 没有生成任何 zip，跳过上传。
    goto :done
)

set HAS_GH=0
where gh >nul 2>nul && set HAS_GH=1

if "%HAS_GH%"=="1" (
    echo   gh CLI 已安装，优先用 gh 发布 ...
    gh release view "v%VERSION%" >nul 2>nul
    if not errorlevel 1 (
        echo   v%VERSION% 已存在，覆盖上传附件 (--clobber) ...
        gh release upload "v%VERSION%" %ZIPS% --clobber
    ) else (
        echo   创建 v%VERSION% 并上传附件 ...
        gh release create "v%VERSION%" %ZIPS% ^
            --title "v%VERSION%" ^
            --notes "ImageTool %VERSION%
下载说明：
- win-x64-self-contained.zip — 推荐，单文件双击即跑，无需装 .NET
- win-arm64-self-contained.zip — ARM Windows（如 Surface Pro X）
- win-x64-framework-dependent.zip — 需装 .NET 10 运行时，体积小约 80%"
    )
    echo   清理 GitHub 旧版单文件附件（如有）...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0clean-github-stale.ps1"
) else if defined GITHUB_TOKEN (
    echo   gh 未安装，回退到 GITHUB_TOKEN (PowerShell API) ...
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
        echo     Write-Output "Uploading $($_.Name) ..."
        echo     Invoke-RestMethod -Headers $h2 -Method Post -InFile $_.FullName -Uri $url
        echo }
        echo Write-Output "OK: all assets uploaded to $tag"
    ) > "%PS%"
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS%"
    if exist "%PS%" del /q "%PS%"
) else (
    echo   [跳过] gh 未安装且未设置 GITHUB_TOKEN，跳过 GitHub Release 上传。
    echo   手动上传: GitHub 仓库 Releases -> v%VERSION% -> 把 %OUT%\ 下的 zip 拖进去即可。
)

:: ========================================================================
:: 上传到 Gitee Release（与 GitHub 平行；gh 管不了 Gitee，只能走 OpenAPI）
:: 需设置环境变量 GITEE_TOKEN（Gitee 私人令牌，勾 projects 权限）
:: ========================================================================
echo.
echo === 上传到 Gitee Release ===
if defined GITEE_TOKEN (
    echo   检测到 GITEE_TOKEN，调用 upload-gitee.ps1 上传 ...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0upload-gitee.ps1"
    if errorlevel 1 ( echo   [警告] Gitee 上传失败（见上方错误）。GitHub 产物不受影响。 )
) else (
    echo   [跳过] 未设置 GITEE_TOKEN，跳过 Gitee Release 上传。
    echo   如需 Gitee 自动发版: 在 Gitee 设置 -> 私人令牌 生成（勾 projects），
    echo   然后 set GITEE_TOKEN=xxx 后再运行本脚本。
)

:done
echo.
echo ============================================================
echo   全部完成。产物:
dir /b "%OUT%\ImageTool-%VERSION%-*.zip" 2>nul
echo ============================================================
endlocal
