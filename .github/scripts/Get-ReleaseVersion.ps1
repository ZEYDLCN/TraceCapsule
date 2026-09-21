param(
    [Parameter(Mandatory = $true)]
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
$pattern = '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$'
if ($Tag -cnotmatch $pattern) {
    throw "Invalid release tag '$Tag'. Use v1.2.3 or v1.2.3-beta.1."
}
if ($Matches[4]) {
    foreach ($identifier in $Matches[4].Split('.')) {
        if ($identifier -match '^0[0-9]+$') {
            throw 'Numeric prerelease identifiers must not have leading zeroes.'
        }
    }
}
$Tag.Substring(1)
