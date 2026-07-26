$ErrorActionPreference = 'Stop'

$token   = $env:GITEE_TOKEN
$version = $env:VERSION
if (-not $token)   { Write-Error "GITEE_TOKEN not set"; exit 1 }
if (-not $version) { Write-Error "VERSION not set"; exit 1 }

$owner = 'yangyuanlife'
$repo  = 'ImageTool'
$tag   = "v$version"
$apiBase = "https://gitee.com/api/v5/repos/$owner/$repo"

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$notesPath = $env:NOTES_PATH
if ($notesPath -and (Test-Path $notesPath)) {
    $notes = [System.IO.File]::ReadAllText($notesPath, $utf8NoBom)
} else {
    $notes = "ImageTool $tag`n`nWindows screenshot / image tool."
}

$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $curl) {
    Write-Error "curl.exe not found；Windows 10/11 自带 curl，请确认 PATH 含 System32"
    exit 1
}

# 用 curl 的 -o 把响应体写入临时文件、-w 只输出纯状态码，避免「切片解析」歧义
function Invoke-GiteeJson {
    param($Method, $Url, $BodyFile)
    $respFile = [System.IO.Path]::GetTempFileName()
    $args = @('-sS', '-o', $respFile, '-w', '%{http_code}', '-X', $Method, $Url)
    if ($BodyFile) {
        $args += '-H', 'Content-Type: application/json; charset=utf-8'
        $args += '--data-binary', "@$BodyFile"
    }
    $code = & curl.exe @args 2>&1
    $exit = $LASTEXITCODE
    $bodyText = [System.IO.File]::ReadAllText($respFile, $utf8NoBom)
    Remove-Item $respFile -Force -ErrorAction SilentlyContinue
    [PSCustomObject]@{ Exit = $exit; Code = ($code -split "`n")[-1].Trim(); Body = $bodyText }
}

# 鲁棒地提取 release id（兼容对象或数组，解析失败返回 $null）
function Get-ReleaseId($jsonText) {
    if (-not $jsonText) { return $null }
    try {
        $o = $jsonText | ConvertFrom-Json
        if ($o -is [System.Array]) {
            if ($o.Count -gt 0) { return $o[0].id }
            return $null
        }
        return $o.id
    } catch {
        return $null
    }
}

# 1) 按 tag 查询 Release
Write-Output "Gitee: 查询 Release $tag ..."
$getUrl = "$apiBase/releases/tags/$tag`?access_token=$token"
$r = Invoke-GiteeJson -Method GET -Url $getUrl
$releaseId = Get-ReleaseId $r.Body
if ($releaseId) {
    Write-Output ("Gitee: Release $tag 已存在 (id={0})" -f $releaseId)
} else {
    Write-Output ("Gitee: Release $tag 不存在或查询失败（GET code={0}），创建中 ..." -f $r.Code)
    $createBody = @{
        tag_name         = $tag
        name             = $tag
        body             = $notes
        target_commitish = 'master'
    } | ConvertTo-Json -Compress
    $tmpBody = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmpBody, $createBody, $utf8NoBom)
    $c = Invoke-GiteeJson -Method POST -Url "$apiBase/releases`?access_token=$token" -BodyFile $tmpBody
    Remove-Item $tmpBody -Force -ErrorAction SilentlyContinue
    Write-Output ("Gitee: create 响应（code={0}）：{1}" -f $c.Code, ($c.Body.Substring(0, [Math]::Min(200, $c.Body.Length))))
    $releaseId = Get-ReleaseId $c.Body
}

# 2) 仍拿不到 id 就明确报错退出（不再带空 URL 硬传）
if (-not $releaseId) {
    Write-Error ("Gitee: 无法获取 release id（GET code={0}，create code={1}）。响应: {2}" -f $r.Code, $c.Code, $c.Body)
    exit 1
}
Write-Output ("Gitee: target release id = {0}" -f $releaseId)

# 3) 用系统 curl 上传附件（标准 multipart，规避手写字节流的 405）
$releaseDir = $env:RELEASE_DIR
if (-not $releaseDir) { $releaseDir = "Release\v$version" }
$zips = Get-ChildItem "$releaseDir\ImageTool-$version-*.zip"
if ($zips.Count -eq 0) { Write-Error "no ImageTool-$version-*.zip under $releaseDir"; exit 1 }

# ---- 文件日志：排障用（Release 目录已被 gitignore，不会进库）----
$logPath = Join-Path $releaseDir 'gitee-upload.log'
function Write-Log($m) {
    $line = ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $m)
    Write-Output $line
    try { Add-Content -Path $logPath -Value $line -Encoding UTF8 } catch {}
}
function Mask($s) { if (($s -is [string]) -and $s.Length -gt 8) { $s.Substring(0,4) + '...' + $s.Substring($s.Length-4) } else { '***' } }
try { Set-Content -Path $logPath -Value ('=== Gitee 上传日志 {0} ===' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8 } catch {}
Write-Log ('VERSION={0} token={1} RELEASE_DIR={2}' -f $version, (Mask $token), $releaseDir)
Write-Log ('curl path: {0}' -f $curl.Path)
try { $cv = & curl.exe --version 2>&1 | Select-Object -First 1; Write-Log ('curl: {0}' -f $cv) } catch {}

$uploadUrl = "$apiBase/releases/$releaseId/attach_files"
Write-Log ("upload url = {0}" -f ($uploadUrl -replace [regex]::Escape($token), '***'))

foreach ($zip in $zips) {
    Write-Log ('=== uploading {0} ({1:N1} MB) ===' -f $zip.Name, ($zip.Length / 1MB))
    Write-Output ("Gitee: uploading {0} ({1:N1} MB) ..." -f $zip.Name, ($zip.Length / 1MB))
    $respFile = [System.IO.Path]::GetTempFileName()
    $errFile  = [System.IO.Path]::GetTempFileName()
    # access_token 作为表单字段（贴合 Gitee 文档，亦为之前采用的形式）
    $curlArgs = @('-s', '-S', '--connect-timeout', '30', '--max-time', '600',
                   '-X', 'POST', $uploadUrl,
                   '-F', "access_token=$token",
                   '-F', "file=@$($zip.FullName)",
                   '-o', $respFile)
    $cmdLine = 'curl ' + (($curlArgs | ForEach-Object { if ($_ -match '\s') { '"{0}"' -f $_ } else { $_ } }) -join ' ')
    Write-Log ('cmd: {0}' -f ($cmdLine -replace [regex]::Escape($token), '***'))
    $t0 = Get-Date
    $curlExit = $null
    try {
        # Start-Process 可轮询心跳 + 捕获 stderr（写日志）；若环境异常则回退直接调用
        $proc = Start-Process -FilePath $curl.Path -ArgumentList $curlArgs -NoNewWindow -PassThru -RedirectStandardError $errFile
        $elapsed = 0
        while (-not $proc.HasExited) {
            Start-Sleep -Seconds 10
            $elapsed += 10
            Write-Log ('... 上传进行中（已 {0}s，请勿关闭窗口）' -f $elapsed)
            Write-Output ('  -> 上传进行中（已 {0}s，请勿关闭窗口）' -f $elapsed)
        }
        $curlExit = $proc.ExitCode
    } catch {
        Write-Log ('Start-Process 失败，回退直接调用：{0}' -f $_.Exception.Message)
        & $curl.Path @curlArgs 2>> $errFile
        $curlExit = $LASTEXITCODE
    }
    $dur = ((Get-Date) - $t0).TotalSeconds
    $result = [System.IO.File]::ReadAllText($respFile, $utf8NoBom)
    $errText = ''
    if (Test-Path $errFile) { $errText = [System.IO.File]::ReadAllText($errFile, $utf8NoBom) }
    Remove-Item $respFile -Force -ErrorAction SilentlyContinue
    Remove-Item $errFile -Force -ErrorAction SilentlyContinue
    Write-Log ('curl exit={0} 耗时={1:N1}s' -f $curlExit, $dur)
    Write-Log ('stderr: {0}' -f $(if ($errText) { $errText.Trim() } else { '(空)' }))
    Write-Log ('response body: {0}' -f $(if ($result) { $result.Trim() } else { '(空)' }))
    $preview = $result.Trim()
    if ($preview.Length -gt 400) { $preview = $preview.Substring(0, 400) }
    Write-Output ("  -> curl exit={0}，HTTP 响应: {1}" -f $curlExit, $preview)
    if ($result -match '"id"\s*:') {
        Write-Output "  -> done"
        Write-Log 'result: SUCCESS (response 含 id)'
    } elseif ($result -match '重复|已存在|exist|already') {
        Write-Output ("  -> already exists, skip: {0}" -f $zip.Name)
        Write-Log 'result: already exists, skipped'
    } else {
        Write-Error ("  -> upload failed: {0}" -f $preview)
        Write-Log 'result: FAILED'
        exit 1
    }
}

Write-Output "Gitee: all uploaded -> https://gitee.com/$owner/$repo/releases/$tag"
