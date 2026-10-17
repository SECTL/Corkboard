# 生成更新清单签名用的 Ed25519 密钥对（照上游 SecRandom-C 的 scripts/ReleaseManifest/New-UpdateSigningKey.ps1）。
#
# 产出（只在本机临时目录里生成，**不要把任何私钥提交进仓库**）：
#   release-public-key.txt        → 32 字节 raw 公钥的 base64，复制进 Corkboard/Assets/Updates/release-public-key.txt
#   github-actions-private-key.txt → 私钥 PEM 的 base64，设置成 GitHub Actions 的
#                                    UPDATE_MANIFEST_PRIVATE_KEY_PEM_BASE64 机密
#   corkboard-update-private.pem / corkboard-update-public.der → 中间产物，用完可删
#
# 换密钥 = 换产品更新链路的信任根：旧版本应用里编着旧公钥，验不过新清单。
# 只在确实要轮换密钥时用，并且要同时上架一次带新公钥的版本。
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$privateKeyPath = Join-Path $OutputDirectory 'corkboard-update-private.pem'
$publicKeyPath = Join-Path $OutputDirectory 'corkboard-update-public.der'

& openssl genpkey -algorithm ED25519 -out $privateKeyPath
if ($LASTEXITCODE -ne 0) { throw "OpenSSL failed to generate the Ed25519 private key: $LASTEXITCODE." }

& openssl pkey -in $privateKeyPath -pubout -outform DER -out $publicKeyPath
if ($LASTEXITCODE -ne 0) { throw "OpenSSL failed to export the Ed25519 public key: $LASTEXITCODE." }

$publicDer = [IO.File]::ReadAllBytes($publicKeyPath)
if ($publicDer.Length -lt 32) { throw 'The generated Ed25519 public key is invalid.' }

# Ed25519 的 SubjectPublicKeyInfo DER 末尾就是 32 字节 raw 公钥，应用侧只认这 32 字节。
$publicKey = [Convert]::ToBase64String($publicDer[($publicDer.Length - 32)..($publicDer.Length - 1)])
$privateKeySecret = [Convert]::ToBase64String([IO.File]::ReadAllBytes($privateKeyPath))

# utf8NoBOM 等价的写法：应用按文本读这个文件再 Trim，带 BOM 不会出错，
# 但两个密钥文件都保持无 BOM，和发布 job 里的比对保持字节级一致。
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'release-public-key.txt'), $publicKey, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'github-actions-private-key.txt'), $privateKeySecret, [Text.UTF8Encoding]::new($false))

Write-Host 'Generated update signing material.'
Write-Host 'Copy release-public-key.txt into Corkboard/Assets/Updates/release-public-key.txt.'
Write-Host 'Set github-actions-private-key.txt as the UPDATE_MANIFEST_PRIVATE_KEY_PEM_BASE64 GitHub Actions secret.'
Write-Host 'Do not commit the private PEM or GitHub Actions secret file.'
