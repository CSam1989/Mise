#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Mints a JWT for manually testing protected endpoints with curl, without going through a
    real /api/auth/login call.

    Phase 3 replaced Mise.Web's fixed system-identity token with real per-staff sign-in
    (docs/plan.md, ADR-004) — for most manual testing, prefer logging in for real via
    /api/auth/login (see docs/Phase-3-Manual-Test-Checklist.md) and using the returned token.
    This script stays useful for testing an *arbitrary* staff id/role combination directly
    (e.g. simulating a FloorStaff caller against a Manager-only endpoint) without needing a
    seeded account for every combination, using the same jwt-signing-key user secret
    `aspire run` already reads.

.EXAMPLE
    $token = ./scripts/mint-dev-token.ps1 -Role Manager
    curl -H "Authorization: Bearer $token" https://localhost:<port>/api/staff -d '...'
#>
[CmdletBinding()]
param(
    [string]$StaffId = [Guid]::NewGuid().ToString(),
    [string]$FullName = "manual-tester",
    [ValidateSet("FloorStaff", "Manager")]
    [string]$Role = "Manager"
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$secretLine = dotnet user-secrets list --project src/Mise.AppHost |
    Where-Object { $_ -like 'Parameters:jwt-signing-key*' }

if (-not $secretLine) {
    throw "jwt-signing-key isn't set. Run: dotnet user-secrets set Parameters:jwt-signing-key <any-random-string> --project src/Mise.AppHost"
}

$signingKey = ($secretLine -split ' = ', 2)[1]

function ConvertTo-Base64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

# The long URI forms, not the short "sub"/"name"/"role" claims: JwtTokenIssuer and
# ReservationsApiFixture/StaffIdentityApiFixture's own token minting all use
# System.Security.Claims.ClaimTypes directly, which are these exact strings.
$nameIdentifierClaimType = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'
$nameClaimType = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'
$roleClaimType = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'

$now = [DateTimeOffset]::UtcNow
$header = @{ alg = 'HS256'; typ = 'JWT' } | ConvertTo-Json -Compress
$payload = [ordered]@{
    iss                     = 'mise-auth'
    aud                     = 'mise-api'
    $nameIdentifierClaimType = $StaffId
    $nameClaimType          = $FullName
    $roleClaimType          = $Role
    nbf                     = $now.ToUnixTimeSeconds()
    exp                     = $now.AddHours(8).ToUnixTimeSeconds()
} | ConvertTo-Json -Compress

$headerB64 = ConvertTo-Base64Url([System.Text.Encoding]::UTF8.GetBytes($header))
$payloadB64 = ConvertTo-Base64Url([System.Text.Encoding]::UTF8.GetBytes($payload))
$signingInput = "$headerB64.$payloadB64"

$hmac = [System.Security.Cryptography.HMACSHA256]::new([System.Text.Encoding]::UTF8.GetBytes($signingKey))
$signature = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($signingInput))
$signatureB64 = ConvertTo-Base64Url($signature)

Write-Output "$signingInput.$signatureB64"
