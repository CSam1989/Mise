#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Mints a placeholder-auth JWT for manually testing protected endpoints with curl.

    Phase 2's auth spine (CLAUDE.md, "Placeholder auth spine") has no login endpoint yet —
    Mise.Web mints its own token internally, but there is no way for a human to obtain one.
    This script mints an equivalent token directly, using the same jwt-signing-key user
    secret `aspire run` already reads, so the result is valid against a locally running
    Mise.ApiService.

.EXAMPLE
    $token = ./scripts/mint-dev-token.ps1
    curl -H "Authorization: Bearer $token" https://localhost:<port>/api/reservations -d '...'
#>
[CmdletBinding()]
param(
    [string]$Name = "manual-tester"
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

# The long URI form, not the short "name" claim: PlaceholderAuthTokenHandler and
# ReservationsApiFixture's own token minting both use System.Security.Claims.ClaimTypes.Name
# directly, which is this exact string — matching it here is what makes
# httpContext.User.Identity.Name (and therefore CreateReservationCommand.PerformedBy)
# resolve to -Name below instead of coming back empty.
$nameClaimType = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'

$now = [DateTimeOffset]::UtcNow
$header = @{ alg = 'HS256'; typ = 'JWT' } | ConvertTo-Json -Compress
$payload = [ordered]@{
    iss            = 'mise-placeholder-auth'
    aud            = 'mise-api'
    $nameClaimType = $Name
    nbf            = $now.ToUnixTimeSeconds()
    exp            = $now.AddHours(1).ToUnixTimeSeconds()
} | ConvertTo-Json -Compress

$headerB64 = ConvertTo-Base64Url([System.Text.Encoding]::UTF8.GetBytes($header))
$payloadB64 = ConvertTo-Base64Url([System.Text.Encoding]::UTF8.GetBytes($payload))
$signingInput = "$headerB64.$payloadB64"

$hmac = [System.Security.Cryptography.HMACSHA256]::new([System.Text.Encoding]::UTF8.GetBytes($signingKey))
$signature = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($signingInput))
$signatureB64 = ConvertTo-Base64Url($signature)

Write-Output "$signingInput.$signatureB64"
