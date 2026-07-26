@echo off
chcp 65001 >nul
:: ============================================================================
::  ImageTool one-click release script
::  Builds self-contained + framework-dependent packages, zips into publish/,
::  then uploads to GitHub (gh CLI preferred, GITHUB_TOKEN fallback) and Gitee.
::  Usage: double-click (run `gh auth login` once), or
::         set GITHUB_TOKEN=xxx & set GITEE_TOKEN=yyy & publish-release.bat
:: ============================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"

:: ---- Read version from ImageTool.csproj <Version> ----
for /f %%v in ('powershell -NoProfile -Command "(Select-Xml -Path ImageTool.csproj -XPath //Version).Node.InnerText"') do set VERSION=%%v
if "%VERSION%"=="" (
    echo [ERROR] Cannot read ^<Version^> from ImageTool.csproj
    exit /b 1
)

:: ---- Output directory ----
set OUT=publish
if not exist "%OUT%" mkdir "%OUT%"

echo ============================================================
echo   ImageTool Release Tool  v%VERSION%
echo   Output dir: %OUT%\
echo ============================================================

:: ========================================================================
:: Variant 1: win-x64 self-contained single file (main release)
:: ========================================================================
set RID=win-x64
set BUILD=bin\publish\%RID%-self-contained
set ZIP=%OUT%\ImageTool-%VERSION%-%RID%-self-contained.zip
set FOLDER=ImageTool-%VERSION%-%RID%-self-contained

echo.
echo [1/3] Self-contained single file (%RID%) ...
dotnet publish -c Release -r %RID% --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=none -p:EnableCompressionInBuild=true ^
    -o "%BUILD%"
if errorlevel 1 ( echo [FAIL] %RID% self-contained publish failed & exit /b 1 )

echo    Zipping %ZIP% ...
if exist "%FOLDER%" rmdir /s /q "%FOLDER%"
if exist "%ZIP%" del /q "%ZIP%"
mkdir "%FOLDER%"
xcopy /e /i /q "%BUILD%" "%FOLDER%" >nul
powershell -NoProfile -Command "Compress-Archive -Path '%FOLDER%' -DestinationPath '%ZIP%' -Force"
rmdir /s /q "%FOLDER%"
rmdir /s /q "%BUILD%"
echo    Done: %ZIP%

:: ========================================================================
:: Variant 2: win-arm64 self-contained single file
:: ========================================================================
set RID=win-arm64
set BUILD=bin\publish\%RID%-self-contained
set ZIP=%OUT%\ImageTool-%VERSION%-%RID%-self-contained.zip
set FOLDER=ImageTool-%VERSION%-%RID%-self-contained

echo.
echo [2/3] Self-contained single file (%RID%) ...
dotnet publish -c Release -r %RID% --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=none -p:EnableCompressionInBuild=true ^
    -o "%BUILD%"
if errorlevel 1 (
    echo   [WARN] %RID% self-contained publish failed (ARM64 workload may be missing), skipping.
    echo   To enable ARM64, run: dotnet workload install windows-desktop-arm64
    goto :skip_arm64
)

echo    Zipping %ZIP% ...
if exist "%FOLDER%" rmdir /s /q "%FOLDER%"
if exist "%ZIP%" del /q "%ZIP%"
mkdir "%FOLDER%"
xcopy /e /i /q "%BUILD%" "%FOLDER%" >nul
powershell -NoProfile -Command "Compress-Archive -Path '%FOLDER%' -DestinationPath '%ZIP%' -Force"
rmdir /s /q "%FOLDER%"
rmdir /s /q "%BUILD%"
echo    Done: %ZIP%
:skip_arm64

:: ========================================================================
:: Variant 3: win-x64 framework-dependent (needs .NET 10 runtime)
:: ========================================================================
set RID=win-x64
set BUILD=bin\publish\%RID%-framework-dependent
set ZIP=%OUT%\ImageTool-%VERSION%-%RID%-framework-dependent.zip
set FOLDER=ImageTool-%VERSION%-%RID%-framework-dependent

echo.
echo [3/3] Framework-dependent (%RID%, requires .NET 10 runtime) ...
dotnet publish -c Release -r %RID% --self-contained false ^
    -p:DebugType=none ^
    -o "%BUILD%"
if errorlevel 1 ( echo [FAIL] %RID% framework-dependent publish failed & exit /b 1 )

echo    Zipping %ZIP% ...
if exist "%FOLDER%" rmdir /s /q "%FOLDER%"
if exist "%ZIP%" del /q "%ZIP%"
mkdir "%FOLDER%"
xcopy /e /i /q "%BUILD%" "%FOLDER%" >nul
powershell -NoProfile -Command "Compress-Archive -Path '%FOLDER%' -DestinationPath '%ZIP%' -Force"
rmdir /s /q "%FOLDER%"
rmdir /s /q "%BUILD%"
echo    Done: %ZIP%

:: ========================================================================
:: Upload to GitHub Release
:: ========================================================================
echo.
echo === Uploading to GitHub Release ===

:: Collect all successfully built zips
set ZIPS=
for %%f in ("%OUT%\ImageTool-%VERSION%-*.zip") do if exist "%%f" set ZIPS=!ZIPS! "%%f"
if "%ZIPS%"=="" (
    echo   [ERROR] No zip was generated, skipping upload.
    goto :done
)

set HAS_GH=0
where gh >nul 2>nul && set HAS_GH=1

if "%HAS_GH%"=="1" (
    echo   gh CLI detected, publishing via gh ...
    gh release view "v%VERSION%" >nul 2>nul
    if not errorlevel 1 (
        echo   v%VERSION% exists, uploading assets with --clobber ...
        gh release upload "v%VERSION%" %ZIPS% --clobber
    ) else (
        echo   Creating v%VERSION% and uploading assets ...
        gh release create "v%VERSION%" %ZIPS% --title "v%VERSION%" --notes "ImageTool %VERSION% - win-x64-self-contained (recommended, no .NET needed), win-arm64-self-contained (ARM Windows), win-x64-framework-dependent (needs .NET 10 runtime, ~80% smaller)"
    )
    echo   Cleaning up stale GitHub assets (if any)...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0clean-github-stale.ps1"
) else if defined GITHUB_TOKEN (
    echo   gh not installed, falling back to GITHUB_TOKEN (PowerShell API) ...
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
    echo   [SKIP] gh not installed and GITHUB_TOKEN not set, skipping GitHub upload.
    echo   Manual: GitHub repo Releases -^> v%VERSION% -^> drag zips from %OUT%\ into it.
)

:: ========================================================================
:: Upload to Gitee Release (parallel to GitHub; gh cannot do Gitee)
:: Requires env var GITEE_TOKEN (Gitee private token with projects scope)
:: ========================================================================
echo.
echo === Uploading to Gitee Release ===
if defined GITEE_TOKEN (
    echo   GITEE_TOKEN detected, calling upload-gitee.ps1 ...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0upload-gitee.ps1"
    if errorlevel 1 ( echo   [WARN] Gitee upload failed (see error above). GitHub artifacts are unaffected. )
) else (
    echo   [SKIP] GITEE_TOKEN not set, skipping Gitee upload.
    echo   To enable: generate a Gitee private token (scope projects), then
    echo   set GITEE_TOKEN=xxx  and run this script again.
)

:done
echo.
echo ============================================================
echo   All done. Artifacts:
dir /b "%OUT%\ImageTool-%VERSION%-*.zip" 2>nul
echo ============================================================
endlocal
