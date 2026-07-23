function Write-EmbeddedModPackageManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SourceManifestPath,
        [Parameter(Mandatory = $true)]
        [string] $PackageDirectory
    )

    $sourceManifest = Get-Content -LiteralPath $SourceManifestPath -Raw | ConvertFrom-Json
    foreach ($field in @("id", "name", "version", "folderName", "entryAssembly")) {
        if ([string]::IsNullOrWhiteSpace($sourceManifest.$field)) {
            throw "ace-mod.json is missing $field."
        }
    }

    $modDirectory = Join-Path $PackageDirectory "mod"
    if (-not (Test-Path -LiteralPath $modDirectory -PathType Container)) {
        throw "The staged package has no mod directory."
    }

    $files = [ordered]@{}
    $packagePrefix = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($file in @(Get-ChildItem -LiteralPath $modDirectory -Recurse -File | Sort-Object FullName)) {
        $fullPath = [IO.Path]::GetFullPath($file.FullName)
        if (-not $fullPath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "A staged package file is outside the package directory: $fullPath"
        }
        $relative = $fullPath.Substring($packagePrefix.Length).Replace('\', '/')
        $files[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    }
    if ($files.Count -eq 0) {
        throw "The staged package contains no mod files."
    }

    $manifest = [ordered]@{
        formatVersion = 2
        id = $sourceManifest.id
        name = $sourceManifest.name
        version = $sourceManifest.version
        folderName = $sourceManifest.folderName
        entryAssembly = $sourceManifest.entryAssembly
        integrity = [ordered]@{
            algorithm = "SHA256"
            files = $files
        }
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PackageDirectory "ace-mod.json") -Encoding utf8
}
