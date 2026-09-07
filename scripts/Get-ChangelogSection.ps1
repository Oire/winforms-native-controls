<#
.SYNOPSIS
    Extracts one version's section from a Keep a Changelog file.

.DESCRIPTION
    The release workflow uses this to put the hand-written changelog entry on the GitHub release
    page, instead of notes derived from commit and pull request titles. Writes nothing and
    returns nothing when the version has no section, which the caller treats as "fall back to
    generated notes" rather than as an error - a release should not fail for want of prose.

.PARAMETER Version
    The version to extract, without a leading "v". For example 1.0.0.

.PARAMETER Path
    Path to the changelog. Defaults to CHANGELOG.md beside this script's parent directory.

.EXAMPLE
    ./scripts/Get-ChangelogSection.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [string] $Path = (Join-Path $PSScriptRoot '../CHANGELOG.md')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Verbose "No changelog at $Path."
    return
}

$lines = Get-Content -LiteralPath $Path

# Headings look like "## [1.0.0] - 2026-09-06". Match the version in brackets exactly, so 1.0.0
# never matches 1.0.10.
$escaped = [regex]::Escape($Version)
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^##\s+\[$escaped\]") {
        $start = $i + 1
        break
    }
}

if ($start -lt 0) {
    Write-Verbose "No section for $Version in $Path."
    return
}

# Runs to the next version heading, or to the link definitions at the foot of the file.
$end = $lines.Count
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s+\[' -or $lines[$i] -match '^\[[^\]]+\]:\s') {
        $end = $i
        break
    }
}

# A range where the end precedes the start counts *down* in PowerShell, so an empty section
# would silently yield its own heading and the next one, reversed. Guard rather than slice.
if ($end -le $start) {
    Write-Verbose "Section for $Version is empty."
    return
}

$section = @($lines[$start..($end - 1)])

# Trim blank lines from both ends without disturbing the blank lines inside.
#
# Both loops slice, and a slice whose end precedes its start counts *down* in PowerShell, which
# under Set-StrictMode is a terminating error rather than an empty result. A section of exactly
# one blank line - "## [Unreleased]" with nothing under it yet - reaches that case, so the
# single-element step is handled before either slice can run.
while ($section.Count -gt 0 -and [string]::IsNullOrWhiteSpace($section[0])) {
    if ($section.Count -eq 1) { $section = @(); break }
    $section = @($section[1..($section.Count - 1)])
}
while ($section.Count -gt 0 -and [string]::IsNullOrWhiteSpace($section[-1])) {
    if ($section.Count -eq 1) { $section = @(); break }
    $section = @($section[0..($section.Count - 2)])
}

if ($section.Count -eq 0) {
    Write-Verbose "Section for $Version is empty."
    return
}

$section -join "`n"
