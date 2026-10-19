<#
.SYNOPSIS
    Reports line coverage per product assembly from Cobertura reports.

.DESCRIPTION
    Optionally runs the whole solution with the Microsoft code-coverage collector in Cobertura format, then merges every
    *.cobertura.xml under the results directory: a source line counts as covered when any report shows it hit, so an
    assembly exercised by several test projects (the core is covered by every provider's tests) is reported once.

.PARAMETER ResultsDirectory
    Where the Cobertura reports are (or are written). Defaults to TestResults under the repository root.

.PARAMETER Collect
    Run `dotnet test ArgentSea.slnx --collect:"Code Coverage;Format=Cobertura"` into the results directory first.

.PARAMETER Markdown
    Emit the table as Markdown.

.EXAMPLE
    pwsh scripts/coverage-summary.ps1 -Collect -Markdown
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $ResultsDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'TestResults'),
    [switch] $Collect,
    [switch] $Markdown
)

$ErrorActionPreference = 'Stop'
$assemblies = 'ArgentSea', 'ArgentSea.Sql', 'ArgentSea.Pg', 'ArgentSea.Orleans', 'ArgentSea.Orleans.Sql'

if ($Collect) {
    if (Test-Path $ResultsDirectory) { Remove-Item $ResultsDirectory -Recurse -Force }
    & dotnet test (Join-Path $RepoRoot 'ArgentSea.slnx') --collect:"Code Coverage;Format=Cobertura" --results-directory $ResultsDirectory | Out-Host
}

$reports = Get-ChildItem $ResultsDirectory -Recurse -Filter *.cobertura.xml -File
if (-not $reports) { throw "No *.cobertura.xml report under $ResultsDirectory (run with -Collect)." }

# assembly -> (file:line -> hit?)
$lines = @{}
foreach ($report in $reports) {
    $xml = [xml](Get-Content $report.FullName -Raw)
    foreach ($package in $xml.coverage.packages.package) {
        $name = [string]$package.name
        if ($assemblies -notcontains $name) { continue }
        if (-not $lines.ContainsKey($name)) { $lines[$name] = @{} }
        foreach ($class in $package.classes.class) {
            foreach ($line in $class.lines.line) {
                $key = "$($class.filename):$($line.number)"
                $hit = [int]$line.hits -gt 0
                if ($lines[$name].ContainsKey($key)) { $lines[$name][$key] = $lines[$name][$key] -or $hit } else { $lines[$name][$key] = $hit }
            }
        }
    }
}

$rows = foreach ($name in $assemblies) {
    if (-not $lines.ContainsKey($name)) { continue }
    $all = $lines[$name]
    $covered = @($all.Values | Where-Object { $_ }).Count
    [pscustomobject]@{
        Assembly = $name -replace '\.dll$', ''
        Lines    = $all.Count
        Covered  = $covered
        Percent  = if ($all.Count -gt 0) { [math]::Round(100.0 * $covered / $all.Count) } else { 0 }
    }
}

if ($Markdown) {
    '| Assembly | Lines | Covered | % |'
    '| --- | ---: | ---: | ---: |'
    foreach ($row in $rows) { "| $($row.Assembly) | $($row.Lines.ToString('N0')) | $($row.Covered.ToString('N0')) | $($row.Percent)% |" }
    ''
    "Merged from $($reports.Count) Cobertura report(s) under $ResultsDirectory."
}
else {
    $rows | Format-Table -AutoSize
    "Merged from $($reports.Count) Cobertura report(s) under $ResultsDirectory."
}
