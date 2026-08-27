<#
.SYNOPSIS
    Raises Ui/AppVersion.cs to the next build number and clears the pre-release suffix.

.DESCRIPTION
    Every push to the default branch publishes a full GitHub release, so the version that ends up in
    the binary has to be minted by CI before the compile, not by hand afterwards. Build is
    incremented by one; PreRelease is emptied so Get-Version.ps1 produces "1.3.0.N" instead of
    "1.3.0.N-beta" and AppVersion.UpdateCheckUrls points at /releases/latest.

    A candidate whose tag already exists is skipped - an interrupted run may have pushed the tag
    without the release, and re-using it would either fail the publish or attach new assets to an old
    release.

.OUTPUTS
    An object with Version ("1.3.0.19"), Tag ("v1.3.0.19") and Build (19). When GITHUB_ENV is set the
    same values are appended to it as NewBuildVersion / NewReleaseTag.
#>
[CmdletBinding()]
param(
    # Defaults to Ui/AppVersion.cs next to this script's repository root.
    [string] $FilePath,

    # Tags that must not be re-used. Read from the local clone when omitted.
    [string[]] $TakenTags
)

$ErrorActionPreference = 'Stop'

if (-not $FilePath) {
    $FilePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Ui/AppVersion.cs'
}

if (-not (Test-Path -LiteralPath $FilePath)) {
    throw "Version file not found: $FilePath"
}

$content = Get-Content -LiteralPath $FilePath -Raw

function Get-Number([string] $name) {
    $match = [regex]::Match($content, "public const uint $name = (\d+);")
    if (-not $match.Success) {
        throw "Could not read '$name' from $FilePath"
    }
    return [int] $match.Groups[1].Value
}

$major = Get-Number 'Major'
$minor = Get-Number 'Minor'
$patch = Get-Number 'Patch'
$build = Get-Number 'Build'

if ($null -eq $TakenTags) {
    $TakenTags = @()
    try {
        $TakenTags = @(git tag --list)
    }
    catch {
        Write-Host "::warning::Could not list git tags, assuming none are taken."
    }
}
$taken = [System.Collections.Generic.HashSet[string]]::new([string[]] $TakenTags, [System.StringComparer]::OrdinalIgnoreCase)

do {
    $build++
    $version = "$major.$minor.$patch.$build"
    $tag = "v$version"
} while ($taken.Contains($tag))

$content = [regex]::Replace($content, 'public const uint Build = \d+;', "public const uint Build = $build;")
$content = [regex]::Replace($content, 'public const string PreRelease = "[^"]*";', 'public const string PreRelease = "";')

# -NoNewline keeps the trailing newline that -Raw already carries, and pwsh's utf8 writes no BOM, so
# the diff stays limited to the two constants.
Set-Content -LiteralPath $FilePath -Value $content -NoNewline -Encoding utf8

Write-Host "Bumped $FilePath to $version"

if ($env:GITHUB_ENV) {
    "NewBuildVersion=$version" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
    "NewReleaseTag=$tag" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
}

[pscustomobject]@{
    Version = $version
    Tag     = $tag
    Build   = $build
}
