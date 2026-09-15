#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the unit-tier suites with coverage collection and enforces the tiered gate
    (docs/plan.md decision #6): 90% line coverage, hard-gated, on every *.Domain and
    *.Application assembly; everything else is reported but never gates the build.

    -filefilters:-*.g.cs excludes source-generated files (notably [LoggerMessage]'s
    generated method bodies, CLAUDE.md's "Logging" standard) from both reports. That code
    is Microsoft's source generator's responsibility to be correct, not something a
    handler's own unit test should need to exercise just to keep the gate green — without
    this, every handler that adds a log call would otherwise silently eat into its own
    coverage number for a branch (logger.IsEnabled(...)) nobody actually wrote.
#>
[CmdletBinding()]
param(
    [double]$GateThresholdPercent = 90.0
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$rawDir = 'coverage-raw'
$reportDir = 'coverage-report'
Remove-Item -Recurse -Force $rawDir, $reportDir -ErrorAction SilentlyContinue

$unitTierProjects = @(
    'tests/Mise.UnitTests/Mise.UnitTests.csproj'
    'tests/Mise.Client.UnitTests/Mise.Client.UnitTests.csproj'
)

foreach ($project in $unitTierProjects) {
    dotnet test $project -c Release --collect:"XPlat Code Coverage" --results-directory $rawDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed for $project" }
}

$reportGenerator = 'dotnet-reportgenerator-globaltool'
& dotnet tool run reportgenerator `
    "-reports:$rawDir/**/coverage.cobertura.xml" `
    "-targetdir:$reportDir/all" `
    '-reporttypes:Cobertura;MarkdownSummaryGithub' `
    '-filefilters:-*.g.cs' | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'reportgenerator failed (all assemblies)' }

& dotnet tool run reportgenerator `
    "-reports:$rawDir/**/coverage.cobertura.xml" `
    "-targetdir:$reportDir/gated" `
    '-reporttypes:Cobertura' `
    '-assemblyfilters:+*.Domain;+*.Application' `
    '-filefilters:-*.g.cs' | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'reportgenerator failed (gated assemblies)' }

$summaryPath = Join-Path $reportDir 'all/SummaryGithub.md'
if (Test-Path $summaryPath) {
    $summary = Get-Content $summaryPath -Raw
    Write-Host $summary
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $summary
    }
}

[xml]$gatedCobertura = Get-Content (Join-Path $reportDir 'gated/Cobertura.xml')
$packages = $gatedCobertura.coverage.packages.package

if (-not $packages -or $packages.Count -eq 0) {
    Write-Host 'Coverage gate: no *.Domain/*.Application assembly exists yet — nothing to gate.' -ForegroundColor Yellow
    exit 0
}

$lineRatePercent = [double]$gatedCobertura.coverage.'line-rate' * 100
Write-Host ("Coverage gate: {0:N1}% on *.Domain/*.Application (threshold {1:N1}%)" -f $lineRatePercent, $GateThresholdPercent)

if ($lineRatePercent -lt $GateThresholdPercent) {
    Write-Error ("Coverage gate FAILED: {0:N1}% is below the {1:N1}% floor." -f $lineRatePercent, $GateThresholdPercent)
    exit 1
}

Write-Host 'Coverage gate passed.' -ForegroundColor Green
