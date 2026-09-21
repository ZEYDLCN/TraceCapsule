param(
    [Parameter(Mandatory = $true)]
    [string]$Directory,
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$expectedIds = @('TraceCapsule.Core', 'TraceCapsule.AspNetCore', 'TraceCapsule.Http', 'TraceCapsule.OpenTelemetry', 'TraceCapsule.RabbitMQ', 'TraceCapsule.Cli')
$packages = @(Get-ChildItem -LiteralPath $Directory -Filter '*.nupkg')
if ($packages.Count -ne $expectedIds.Count) { throw 'Expected exactly six NuGet packages.' }
$seen = @()
foreach ($package in $packages) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec') })
        if ($entries.Count -ne 1) { throw "Expected one manifest in $($package.Name)." }
        $reader = [System.IO.StreamReader]::new($entries[0].Open())
        try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $manifest.package.metadata
        $id = [string]$metadata.id
        if ($id -cnotin $expectedIds -or $id -cin $seen) { throw "Unexpected or duplicate package id: $id" }
        if ($metadata.version -cne $Version) { throw "Wrong version in $id. Expected $Version." }
        if ($package.Name -cne "$id.$Version.nupkg") { throw "Unexpected package filename: $($package.Name)" }
        $symbols = Join-Path $Directory "$id.$Version.snupkg"
        if (!(Test-Path -LiteralPath $symbols -PathType Leaf)) { throw "Missing symbol package for $id." }
        $dependencies = @($metadata.dependencies.group.dependency | Where-Object { $_.id -like 'TraceCapsule.*' })
        if ($id -cnotin @('TraceCapsule.Core', 'TraceCapsule.Cli') -and
            @($dependencies | Where-Object { $_.id -ceq 'TraceCapsule.Core' }).Count -ne 1) {
            throw "Missing Core dependency in $id."
        }
        foreach ($dependency in $dependencies) {
            if ($dependency.version -cne $Version -and $dependency.version -cne "[$Version, )") {
                throw "Wrong dependency version for $($dependency.id) in $id."
            }
        }
        $seen += $id
        Write-Output "Verified $id $Version"
    } finally { $archive.Dispose() }
}
