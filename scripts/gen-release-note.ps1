# 生成 GitHub Release 正文：标签对应的 CHANGELOG + 下载表 + SHA256 折叠表。
#
# 照上游 SecRandom-C 的 scripts/gen-release-note.ps1 改写：
#   · 产品与仓库可配（-Repository，缺省读环境变量 repoName，再缺省 SECTL/Corkboard）；
#   · CHANGELOG 不再写死 ./CHANGELOG/v3/<tag>/CHANGELOG.md，而是在 CHANGELOG 下任意层级
#     找目录名正好等于 tag 的 CHANGELOG.md（CHANGELOG/**/<tag>/CHANGELOG.md）；
#   · 找不到对应 CHANGELOG 时回退到「发布说明待补充」；
#   · 提供可选环境变量 SECTL_MIRROR_BASE 时，下载表多一列镜像下载
#     （镜像链接 = 前缀 + GitHub 下载链接，例如 ghproxy 风格的
#      https://ghproxy.sectl.cn/https://github.com/SECTL/Corkboard/releases/download/<tag>/<file>）。
#
# 默认模式（Publish.yml 的 publish job）对 ./artifacts/release/output 里的产物现算 SHA256。
# -InventoryPath / -BodyPath 是留给以后再补产物的流程（例如移动端单独出包后回头刷新发布说明）：
# 前者用一个「文件名 + SHA256」的 JSON 清单描述发布资产，后者保留已经上线的正文、
# 只替换其中由本脚本生成的那几张表。
[CmdletBinding()]
param(
    [string]$InventoryPath = '',
    [string]$BodyPath = '',
    [string]$OutFile = './release-note.md',
    [string]$Repository = $(if (-not [string]::IsNullOrWhiteSpace($env:repoName)) { $env:repoName } else { 'SECTL/Corkboard' })
)

$tag = $env:tagName
if ([string]::IsNullOrWhiteSpace($Repository)) {
    throw "Repository name is required. Pass -Repository or set the repoName environment variable."
}
if ([string]::IsNullOrWhiteSpace($tag)) {
    throw "Environment variable 'tagName' is required."
}

$mirrorBase = $env:SECTL_MIRROR_BASE
$hasMirror = -not [string]::IsNullOrWhiteSpace($mirrorBase)
$releaseNotePath = $OutFile
$outDir = './artifacts/release/output'

if (-not [string]::IsNullOrWhiteSpace($InventoryPath)) {
    if (-not (Test-Path -LiteralPath $InventoryPath -PathType Leaf)) {
        throw "Inventory file not found: $InventoryPath"
    }

    $inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
    $inventoryEntries = foreach ($entry in @($inventory)) {
        if ([string]::IsNullOrWhiteSpace($entry.name)) {
            throw "Every inventory entry needs a file name."
        }
        if ([string]::IsNullOrWhiteSpace($entry.sha256)) {
            throw "Inventory entry '$($entry.name)' has no SHA256 digest."
        }
        [pscustomobject]@{ Name = $entry.name; Sha256 = $entry.sha256.ToUpperInvariant() }
    }
    $files = @($inventoryEntries | Sort-Object Name)

    if (-not $files) {
        throw "No assets found in $InventoryPath"
    }
} else {
    if (-not (Test-Path $outDir)) {
        throw "Output directory not found: $outDir"
    }

    $localFiles = Get-ChildItem -Path $outDir -File | Sort-Object Name
    if (-not $localFiles) {
        throw "No files found in $outDir"
    }

    $files = foreach ($file in $localFiles) {
        [pscustomobject]@{ Name = $file.Name; Sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash }
    }
}

$downloadSummary = @"
**下载链接**

| 文件名 | GitHub |
| --- | --- |
"@
if ($hasMirror) {
    $downloadSummary = @"
**下载链接**

| 文件名 | GitHub | 镜像 |
| --- | --- | --- |
"@
}

foreach ($file in $files) {
    $gh = "https://github.com/${Repository}/releases/download/${tag}/$($file.Name)"
    if ($hasMirror) {
        $mirror = "$($mirrorBase.TrimEnd('/'))/$gh"
        $downloadSummary += "`n| $($file.Name) | [下载](${gh}) | [下载](${mirror}) |"
    } else {
        $downloadSummary += "`n| $($file.Name) | [下载](${gh}) |"
    }
}

$sha256Summary = @"
> [!important]
> 下载时请核对文件 SHA256。

<details>
<summary>展开 SHA256 </summary>

| 文件名 | SHA256 |
| --- | --- |
"@

foreach ($file in $files) {
    $sha256Summary += "`n| $($file.Name) | ``$($file.Sha256)`` |"
}
$sha256Summary += "`n`n</details>"

# CHANGELOG 目录规约：CHANGELOG/<大版本>/<tag>/CHANGELOG.md，大版本树随版本走，
# 所以这里不猜层级，只按目录名找 tag。
$changelogPath = $null
if (Test-Path -LiteralPath './CHANGELOG' -PathType Container) {
    $changelogMatch = Get-ChildItem -LiteralPath './CHANGELOG' -Recurse -File -Filter 'CHANGELOG.md' |
        Where-Object { $_.Directory.Name -eq $tag } |
        Sort-Object FullName |
        Select-Object -First 1
    if ($null -ne $changelogMatch) {
        $changelogPath = $changelogMatch.FullName
    }
}

$changelog = if (-not [string]::IsNullOrWhiteSpace($BodyPath)) {
    if (-not (Test-Path -LiteralPath $BodyPath -PathType Leaf)) {
        throw "Release body file not found: $BodyPath"
    }

    $body = Get-Content -LiteralPath $BodyPath -Raw
    $tableMarker = $body.LastIndexOf('**下载链接**')
    if ($tableMarker -ge 0) {
        $body.Substring(0, $tableMarker).TrimEnd()
    } else {
        $body.TrimEnd()
    }
} elseif ($null -ne $changelogPath) {
    (Get-Content -LiteralPath $changelogPath -Raw).TrimEnd()
} else {
    Write-Warning "CHANGELOG for tag '$tag' was not found under ./CHANGELOG; using the placeholder release note."
    "- 发布说明待补充。`n---"
}

$fullContent = "$changelog`n`n$downloadSummary`n`n$sha256Summary"
$outDirectory = Split-Path -Parent $releaseNotePath
if (-not [string]::IsNullOrWhiteSpace($outDirectory) -and -not (Test-Path -LiteralPath $outDirectory)) {
    New-Item -ItemType Directory -Path $outDirectory -Force | Out-Null
}
# 无 BOM：Markdown 正文带 BOM 时 GitHub 会在正文开头渲染出一个零宽字符。
[IO.File]::WriteAllText([IO.Path]::GetFullPath($releaseNotePath), $fullContent, [Text.UTF8Encoding]::new($false))

Write-Host "Release Note generated with $($files.Count) asset(s)"
