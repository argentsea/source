<#
.SYNOPSIS
    Counts the spec blocks that have at least one test citing them with [Trait("Spec", "<block id>")].

.DESCRIPTION
    Reads every block under specs/*/blocks, ignores blocks whose metadata.status is Rejected, scans the test sources
    (every *.cs file under a test project folder) for Spec traits, and reports per spec how many non-rejected blocks have
    at least one citing test. The four "testable" specs (behavior, interfaces, invariants, schemas) are the ones the
    coverage KPI in TEST-COVERAGE-ANALYSIS.md is computed over; the others are listed for completeness.

.PARAMETER RepoRoot
    The repository root. Defaults to the parent of the scripts folder.

.PARAMETER ShowMissing
    Also list every testable non-rejected block that has no citing test, grouped by section.

.PARAMETER Markdown
    Emit the summary table (and the missing list) as Markdown instead of PowerShell objects.

.EXAMPLE
    pwsh scripts/spec-evidence.ps1 -ShowMissing -Markdown
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [switch] $ShowMissing,
    [switch] $Markdown
)

$ErrorActionPreference = 'Stop'
$testableSpecs = 'behavior', 'interfaces', 'invariants', 'schemas'

# 1. Every block, with its spec, status, type and section. A block adopted from a brief keeps the brief block's id in
#    metadata.sources (label adoptedFrom); tests written against the brief cite that id, so it is an alias of the block.
$blocks = Get-ChildItem (Join-Path $RepoRoot 'specs/*/blocks/*.json') | ForEach-Object {
    $d = Get-Content $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    [pscustomobject]@{
        Id      = [string]$d.id
        Spec    = $_.Directory.Parent.Name
        Status  = [string]$d.metadata.status
        Type    = [string]$d.type
        Section = [string]$d.metadata.sectionId
        Aliases = @($d.metadata.sources | Where-Object { $_.label -eq 'adoptedFrom' -and $_.blockId } | ForEach-Object { [string]$_.blockId })
    }
}
$aliasOf = @{}
foreach ($block in $blocks) { foreach ($alias in $block.Aliases) { $aliasOf[$alias] = $block.Id } }

# 2. Every Spec trait in the test sources: project folders named test / tests, plus the integration test project.
$testFiles = Get-ChildItem (Join-Path $RepoRoot 'ArgentSea.*') -Directory | ForEach-Object {
    Get-ChildItem $_.FullName -Recurse -Filter *.cs -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.FullName -match '[\\/](test|tests|ArgentSea\.Integration\.Test)[\\/]' }
}
$traitPattern = [regex]'Trait\(\s*"Spec"\s*,\s*"(blk-[0-9a-f]{32})"\s*\)'
$citations = @{}
foreach ($file in $testFiles) {
    $relative = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    foreach ($m in $traitPattern.Matches((Get-Content $file.FullName -Raw))) {
        $id = $m.Groups[1].Value
        if ($aliasOf.ContainsKey($id)) { $id = $aliasOf[$id] }
        if (-not $citations.ContainsKey($id)) { $citations[$id] = [System.Collections.Generic.List[string]]::new() }
        if (-not $citations[$id].Contains($relative)) { $citations[$id].Add($relative) }
    }
}

# 3. Per-spec summary.
$nonRejected = $blocks | Where-Object { $_.Status -ne 'Rejected' }
$summary = $blocks | Group-Object Spec | Sort-Object Name | ForEach-Object {
    $live = @($_.Group | Where-Object { $_.Status -ne 'Rejected' })
    $cited = @($live | Where-Object { $citations.ContainsKey($_.Id) })
    [pscustomobject]@{
        Spec        = $_.Name
        Testable    = $testableSpecs -contains $_.Name
        Blocks      = $_.Count
        NonRejected = $live.Count
        WithTest    = $cited.Count
        Percent     = if ($live.Count -gt 0) { [math]::Round(100.0 * $cited.Count / $live.Count) } else { 0 }
    }
}
$testable = @($nonRejected | Where-Object { $testableSpecs -contains $_.Spec })
$testableCited = @($testable | Where-Object { $citations.ContainsKey($_.Id) })
$total = [pscustomobject]@{
    Spec        = 'Testable total'
    Testable    = $true
    Blocks      = @($blocks | Where-Object { $testableSpecs -contains $_.Spec }).Count
    NonRejected = $testable.Count
    WithTest    = $testableCited.Count
    Percent     = if ($testable.Count -gt 0) { [math]::Round(100.0 * $testableCited.Count / $testable.Count) } else { 0 }
}
$danglingIds = @($citations.Keys | Where-Object { -not ($blocks.Id -contains $_) })
$missing = @($testable | Where-Object { -not $citations.ContainsKey($_.Id) } | Sort-Object Section, Spec, Id)

if ($Markdown) {
    '| Spec | Blocks | Non-rejected | Cite a test | % |'
    '| --- | ---: | ---: | ---: | ---: |'
    foreach ($row in ($summary | Where-Object Testable)) {
        "| $($row.Spec) | $($row.Blocks) | $($row.NonRejected) | $($row.WithTest) | $($row.Percent)% |"
    }
    "| **$($total.Spec)** | **$($total.Blocks)** | **$($total.NonRejected)** | **$($total.WithTest)** | **$($total.Percent)%** |"
    ''
    "Distinct block ids cited by tests: $($citations.Count); cited ids that match no block: $($danglingIds.Count)."
    if ($ShowMissing) {
        ''
        "Testable non-rejected blocks with no citing test ($($missing.Count)):"
        ''
        foreach ($group in ($missing | Group-Object Section | Sort-Object Name)) {
            $name = if ($group.Name) { $group.Name } else { '(no section)' }
            "- **$name** ($($group.Count)): " + (($group.Group | ForEach-Object { "$($_.Id) [$($_.Type)]" }) -join ', ')
        }
    }
}
else {
    $summary + $total | Format-Table -AutoSize
    "Distinct block ids cited by tests: $($citations.Count); cited ids that match no block: $($danglingIds.Count)"
    if ($danglingIds.Count -gt 0) { $danglingIds }
    if ($ShowMissing) {
        ''
        "Testable non-rejected blocks with no citing test ($($missing.Count)):"
        $missing | Format-Table Section, Spec, Type, Id -AutoSize
    }
}
