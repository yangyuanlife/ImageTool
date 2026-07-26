@echo off
chcp 936 >nul
:: ============================================================================
::  ImageTool 一键发布脚本
::  功能: 1) 多目标发布（自包含单文件 + 框架依赖） 2) 打包到 Release\v<版本>\ 目录
::        3) GitHub Release：优先 gh CLI，未装 gh 则回退 GITHUB_TOKEN + PowerShell API（上传后自动清理旧版单文件附件）
::        4) Gitee  Release：设了 GITEE_TOKEN 则调 upload-gitee.ps1（OpenAPI）
::  用法: 双击运行（需先 `gh auth login` 一次），或
::        set GITHUB_TOKEN=xxx && set GITEE_TOKEN=yyy && publish-release.bat
::  说明: 推荐装 gh CLI（winget install --id GitHub.cli），`gh auth login` 浏览器授权后
::        GitHub 发布全自动、无需手填 token，且 --clobber 可自动覆盖同名附件。
::        无对应 token 时该平台跳过，但仍会生成 zip，可手动到对应 Releases 拖入。
::        Gitee 私人令牌: Gitee 设置 -> 私人令牌，勾 projects 权限。
::  编码: 本文件为 GBK(cp936) 无 BOM；配合上方 chcp 936，中文提示可正常显示不乱码。
::  产物: Release\v<版本>\ 下含所有 zip 与 release notes.txt
:: ============================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"

:: ---- 读取版本号 (从 ImageTool.csproj 的 <Version>) ----
for /f %%v in ('powershell -NoProfile -Command "(Select-Xml -Path ImageTool.csproj -XPath //Version).Node.InnerText"') do set VERSION=%%v
if "%VERSION%"=="" (
    echo [错误] 无法从 ImageTool.csproj 读取 ^<Version^>
    exit /b 1
)

:: ---- 输出目录: Release/v<version>/ ----
set OUT=Release\v%VERSION%
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
dotnet publish ImageTool.csproj -c Release -r %RID% --self-contained true ^
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
dotnet publish ImageTool.csproj -c Release -r %RID% --self-contained true ^
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
dotnet publish ImageTool.csproj -c Release -r %RID% --self-contained false ^
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
:: 生成 Release notes 并询问是否修改
:: ========================================================================
set NOTES_PATH=%OUT%\release notes.txt
echo.
echo ============================================================
echo   生成 Release notes: %NOTES_PATH%
echo ============================================================
set "NOTES_DIR=%OUT%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0gen-release-notes.ps1"

echo.
echo ---- 当前 Release notes 内容 ----
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0show-notes.ps1"
echo --------------------------------

:: 询问是否修改（10 秒倒计时，超时默认不需要修改）
choice /c YN /t 10 /d N /m "release notes 如上，是否需要修改【Y】修改，【N】不需要"
if errorlevel 2 goto :skip_notes_edit
if errorlevel 1 goto :edit_notes

:edit_notes
echo.
echo   请修改以下目录下的 release notes.txt：
echo   %NOTES_PATH%
echo   【Y】修改完成后输入 Y 继续发布
start /wait notepad "%NOTES_PATH%"
set "MODIFY_OK="
:wait_confirm
set /p MODIFY_OK=   修改完毕？输入 Y 继续发布: 
if /i "%MODIFY_OK%"=="Y" goto :skip_notes_edit
echo   未收到 Y，请先修改文件...
start /wait notepad "%NOTES_PATH%"
goto :wait_confirm

:skip_notes_edit
echo   使用当前 Release notes 继续发布 ...

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
        echo   v%VERSION% 已存在，更新说明并更新附件（--clobber） ...
        gh release edit "v%VERSION%" --notes-file "%NOTES_PATH%"
        gh release upload "v%VERSION%" %ZIPS% --clobber
    ) else (
        echo   创建 v%VERSION% 并上传附件 ...
        gh release create "v%VERSION%" %ZIPS% --title "v%VERSION%" --notes-file "%NOTES_PATH%"
    )
    echo   清理 GitHub 旧版单文件附件（如有）...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0clean-github-stale.ps1"
) else if defined GITHUB_TOKEN (
    echo   gh 未安装，回退到 GITHUB_TOKEN（PowerShell API） ...
    set "GH_REPO=yangyuanlife/ImageTool"
    set "GH_TAG=v%VERSION%"
    set "NOTES_PATH=%NOTES_PATH%"
    set "RELEASE_DIR=%OUT%"
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0upload-github-token.ps1"
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
    set "RELEASE_DIR=%OUT%"
    set "NOTES_PATH=%NOTES_PATH%"
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
echo   全部完成。产物位于:
echo   %OUT%\
dir /b "%OUT%\ImageTool-%VERSION%-*.zip" 2>nul
echo ============================================================
endlocal
