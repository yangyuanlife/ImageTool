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

$uploadUrl = "$apiBase/releases/$releaseId/attach_files"
Write-Output ("Gitee: upload url = {0}" -f ($uploadUrl -replace [regex]::Escape($token), '***'))

foreach ($zip in $zips) {
    Write-Output ("Gitee: uploading {0} ({1:N1} MB) ..." -f $zip.Name, ($zip.Length / 1MB))
    Write-Output "  -> 进度条（#）会实时显示；若长时间不动说明连接卡住，最多 600s 超时后报错"
    $respFile = [System.IO.Path]::GetTempFileName()
    # 关键修复：进度条（-# 写 stderr）必须实时输出到控制台，绝不能捕获进变量（会缓冲，导致「卡住没动静」）。
    # 响应体用 -o 存临时文件，结束后再读取做成功/失败判断。access_token 按 Gitee 文档放 query。
    $upUrl = $uploadUrl + '?access_token=' + $token
    & curl.exe -sS -# --connect-timeout 30 --max-time 600 -X POST $upUrl -F "file=@$($zip.FullName)" -o $respFile
    $curlExit = $LASTEXITCODE
    $result = [System.IO.File]::ReadAllText($respFile, $utf8NoBom)
    Remove-Item $respFile -Force -ErrorAction SilentlyContinue
    $preview = $result.Trim()
    if ($preview.Length -gt 400) { $preview = $preview.Substring(0, 400) }
    Write-Output ("  -> curl exit={0}，HTTP 响应: {1}" -f $curlExit, $preview)
    if ($result -match '"id"\s*:') {
        Write-Output "  -> done"
    } elseif ($result -match '重复|已存在|exist|already') {
        Write-Output ("  -> already exists, skip: {0}" -f $zip.Name)
    } else {
        Write-Error ("  -> upload failed: {0}" -f $preview)
        exit 1
    }
}

Write-Output "Gitee: all uploaded -> https://gitee.com/$owner/$repo/releases/$tag"
