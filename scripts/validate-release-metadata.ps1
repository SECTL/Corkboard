# 发布频道元数据门禁（照上游 SecRandom-C 的 scripts/validate-release-metadata.ps1）。
#
# 更新客户端先按通道从 metadata.yaml 解析出发布 tag，再校验发布清单里的 tag/channel；
# 两者不一致时整份更新会被拒收。也就是说，频道写旧的版本发出去等于发了一个谁都拿不到的更新。
# 所以这里在构建前就把「这次要发的 tag」和 metadata.yaml 对上，几秒钟失败，
# 而不是等整套构建跑完、发布 job 才炸。
[CmdletBinding()]
param(
    [string]$Tag = $env:RELEASE_TAG,
    [string]$MetadataPath = 'metadata.yaml'
)

if ([string]::IsNullOrWhiteSpace($Tag)) {
    throw 'A release tag is required. Set the RELEASE_TAG environment variable or pass -Tag.'
}

$channel = if ($Tag -match '-alpha(?:\.|$)') { 'alpha' }
    elseif ($Tag -match '-beta(?:\.|$)') { 'beta' }
    elseif ($Tag -match '-') { throw "Unsupported prerelease tag: $Tag" }
    else { 'release' }

if (-not (Test-Path -LiteralPath $MetadataPath -PathType Leaf)) {
    throw "Release metadata file '$MetadataPath' was not found."
}

$metadata = Get-Content -LiteralPath $MetadataPath -Raw
$channelMatch = [regex]::Match($metadata, "(?ms)^  ${channel}:\s*\r?\n\s+tag:\s*(?<tag>\S+)\s*$")
if (-not $channelMatch.Success -or $channelMatch.Groups['tag'].Value -ne $Tag) {
    throw "metadata.yaml channel '$channel' must point to release tag $Tag."
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "channel=$channel"
}

Write-Host "metadata.yaml channel '$channel' -> $Tag"
