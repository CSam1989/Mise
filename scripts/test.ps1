#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs everything CI runs on every push: Release build, all five test-tier suites, and
    the format check. Mirrors docs/plan.md's "Verification" section so a phase can be
    proven green locally before it is proven green in CI.
#>
[CmdletBinding()]
param(
    [switch]$SkipFormatCheck
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

function Invoke-Step {
    param([string]$Description, [scriptblock]$Command)
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Failed: $Description (exit code $LASTEXITCODE)"
    }
}

Invoke-Step 'dotnet build (Release)' { dotnet build Mise.slnx -c Release }

$testProjects = @(
    'tests/Mise.ArchitectureTests/Mise.ArchitectureTests.csproj'
    'tests/Mise.UnitTests/Mise.UnitTests.csproj'
    'tests/Mise.Client.UnitTests/Mise.Client.UnitTests.csproj'
    'tests/Mise.IntegrationTests/Mise.IntegrationTests.csproj'
    'tests/Mise.E2ETests/Mise.E2ETests.csproj'
)

foreach ($project in $testProjects) {
    Invoke-Step "dotnet test $project" { dotnet test $project -c Release --no-build }
}

if (-not $SkipFormatCheck) {
    Invoke-Step 'dotnet format --verify-no-changes' {
        dotnet format Mise.slnx --verify-no-changes --no-restore --severity warn
    }
}

Write-Host 'All suites green.' -ForegroundColor Green
