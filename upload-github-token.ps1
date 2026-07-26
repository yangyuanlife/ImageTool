$ErrorActionPreference = 'Stop'

# ---- 读取环境变量（由 publish-release.bat 传入）----
$repo = $env:GH_REPO
if (-not $repo) { $repo = 'yangyuanlife/ImageTool' }
$tag  = $env:GH_TAG
if (-not $tag)  { $tag = "v$($env:VERSION)" }
$tok  = $env:GITHUB_TOKEN
if (-not $tok)  { Write-Output '[跳过] GITHUB_TOKEN 未设置，跳过 GitHub 上传。'; exit 0 }

$base = "https://api.github.com/repos/$repo"
$h = @{
    Authorization = "Bearer $tok"
    'User-Agent'  = 'ImageTool-Release'
}

# ---- 查找或创建 Release ----
try {
    $rel = Invoke-RestMethod -Headers $h -Uri "$base/releases/tags/$tag"
    Write-Output "Release $tag 已存在，追加上传附件 ..."
}
catch {
    $body = @{
        tag_name   = $tag
        name       = $tag
        body       = "ImageTool $tag"
        draft      = $false
        prerelease = $false
    } | ConvertTo-Json -Compress
    $rel = Invoke-RestMethod -Headers $h -Method Post -Body $body -ContentType 'application/json' -Uri "$base/releases"
    Write-Output "已创建 Release $tag ..."
}

# ---- 上传 publish/ 下的 zip 附件 ----
$h2 = $h.Clone()
$h2['Content-Type'] = 'application/zip'
$dir = Resolve-Path 'publish'
$files = Get-ChildItem "$dir\ImageTool-$($env:VERSION)-*.zip"
if ($files.Count -eq 0) { Write-Output '[警告] 未找到任何 zip，跳过上传。'; exit 0 }
foreach ($f in $files) {
    $url = $rel.upload_url.Replace('{?name,label}', "?name=$($f.Name)")
    Write-Output "Uploading $($f.Name) ..."
    Invoke-RestMethod -Headers $h2 -Method Post -InFile $f.FullName -Uri $url
}
Write-Output "OK: 全部附件已上传到 $tag"
