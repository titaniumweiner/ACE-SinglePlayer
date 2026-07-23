param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "mod-package-tools.ps1")

$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stage = Join-Path $tempRoot "OpenDereth-Mod-Convert-$PID-$([Guid]::NewGuid().ToString('N'))"

try {
    New-Item -ItemType Directory -Force -Path $stage, $OutputDirectory | Out-Null
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $stage -Force

    $manifestPath = Join-Path $stage "ace-mod.json"
    $modDirectory = Join-Path $stage "mod"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $modDirectory -PathType Container)) {
        throw "The input is not an OpenDereth mod package."
    }

    $sourceManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($sourceManifest.formatVersion -eq 1) {
        $sidecarPath = $PackagePath + ".sha256"
        if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) {
            throw "A legacy format-1 package must pass its external checksum before conversion."
        }
        $expected = (Get-Content -LiteralPath $sidecarPath -Raw).Trim()
        $actual = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash
        if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The legacy package checksum does not match."
        }
    } elseif ($sourceManifest.formatVersion -ne 2) {
        throw "Only OpenDereth package formats 1 and 2 can be converted."
    }

    Write-EmbeddedModPackageManifest -SourceManifestPath $manifestPath -PackageDirectory $stage

    $outputPath = Join-Path $OutputDirectory (Split-Path $PackagePath -Leaf)
    foreach ($oldOutput in @($outputPath, ($outputPath + ".sha256"))) {
        if (Test-Path -LiteralPath $oldOutput) {
            Remove-Item -LiteralPath $oldOutput -Force
        }
    }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $outputPath -CompressionLevel Optimal
    Write-Host "Converted package: $outputPath"
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
