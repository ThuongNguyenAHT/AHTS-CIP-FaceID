param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$siteRoot = Join-Path $repositoryRoot "tests\Cip.Server.Net40.SmokeHost\bin\$Configuration\net40\site"
$hostAssembly = Join-Path $repositoryRoot "tests\Cip.Server.Net40.SmokeHost\bin\$Configuration\net40\Cip.Server.Net40.SmokeHost.dll"

if (-not (Test-Path -LiteralPath $hostAssembly)) {
    throw "Smoke host has not been built: $hostAssembly"
}

Add-Type -AssemblyName System.Web
Add-Type -Path $hostAssembly

$aspHost = [System.Web.Hosting.ApplicationHost]::CreateApplicationHost(
    [Cip.Server.Net40.SmokeHost.AspNetSmokeHost],
    '/',
    $siteRoot + '\')

[xml]$siteConfig = Get-Content -Raw -Encoding UTF8 (Join-Path $siteRoot 'web.config')
$adminUsername = [string]($siteConfig.configuration.appSettings.add | Where-Object key -eq 'Admin.Username').value
$adminPassword = [string]($siteConfig.configuration.appSettings.add | Where-Object key -eq 'Admin.Password').value
$results = New-Object System.Collections.Generic.List[object]
$createdIds = New-Object System.Collections.Generic.List[int]
$authCookie = $null
$baselineRows = $aspHost.CountConsentRows()

function Invoke-LegacyRequest {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body,
        [string]$Cookie
    )

    $request = New-Object Cip.Server.Net40.SmokeHost.SmokeRequest
    $request.Method = $Method
    $request.Path = $Path
    if ($null -ne $Body) {
        $request.ContentType = 'application/json; charset=utf-8'
        $request.Body = $Body | ConvertTo-Json -Compress
    }
    if ($Cookie) {
        $request.Cookie = $Cookie
    }
    return $aspHost.Process($request)
}

function Assert-Status {
    param(
        [string]$Name,
        $Response,
        [int]$Expected
    )

    if ($Response.StatusCode -ne $Expected) {
        throw "$Name returned HTTP $($Response.StatusCode); expected $Expected. Body: $($Response.Body)"
    }
    $results.Add([pscustomobject]@{ Test = $Name; Status = 'PASS'; Detail = "HTTP $Expected" })
}

try {
    $response = Invoke-LegacyRequest GET '/health/live'
    Assert-Status 'Liveness' $response 200

    $response = Invoke-LegacyRequest GET '/health/ready'
    Assert-Status 'Database readiness' $response 200
    $ready = $response.Body | ConvertFrom-Json
    if (-not $ready.vendorDatabase -or -not $ready.consentDatabase) {
        throw "Readiness did not confirm both databases: $($response.Body)"
    }

    $response = Invoke-LegacyRequest GET '/tablet'
    Assert-Status 'Tablet page' $response 200
    if (-not $response.Body.Contains('waitingView')) { throw 'Tablet page is missing waitingView.' }

    $response = Invoke-LegacyRequest GET '/css/site.css'
    Assert-Status 'Embedded CSS' $response 200
    if (-not $response.Body.Contains(':root')) { throw 'Embedded CSS content is invalid.' }

    $response = Invoke-LegacyRequest GET '/js/tablet.js'
    Assert-Status 'Tablet polling script' $response 200
    if (-not $response.Body.Contains("setConnection('online'") -or $response.Body.Contains('        connectSignalR();')) {
        throw 'Tablet script was not converted to polling-only mode.'
    }

    $response = Invoke-LegacyRequest GET '/api/tablet/config'
    Assert-Status 'Tablet polling config' $response 200
    if (($response.Body | ConvertFrom-Json).transport -ne 'PollingOnly') {
        throw "Tablet transport is not PollingOnly: $($response.Body)"
    }

    $response = Invoke-LegacyRequest GET '/admin'
    Assert-Status 'Admin requires authentication' $response 302

    $response = Invoke-LegacyRequest POST '/api/auth/login' @{ username = $adminUsername; password = 'invalid-password' }
    Assert-Status 'Reject invalid login' $response 401

    $response = Invoke-LegacyRequest POST '/api/auth/login' @{ username = $adminUsername; password = $adminPassword }
    Assert-Status 'Admin login' $response 200
    $setCookie = [string]$response.Headers['Set-Cookie']
    if (-not $setCookie) { throw 'Login did not return Set-Cookie.' }
    $authCookie = $setCookie.Split(';')[0]

    $response = Invoke-LegacyRequest GET '/api/auth/me' $null $authCookie
    Assert-Status 'Authenticated user' $response 200

    $response = Invoke-LegacyRequest GET '/admin' $null $authCookie
    Assert-Status 'Authenticated admin page' $response 200

    $response = Invoke-LegacyRequest GET '/api/admin/dashboard' $null $authCookie
    Assert-Status 'Dashboard' $response 200
    $baselineDashboard = $response.Body | ConvertFrom-Json

    $response = Invoke-LegacyRequest GET '/api/admin/source-passengers' $null $authCookie
    Assert-Status 'Source passenger list' $response 200
    $sourcePassengers = $response.Body | ConvertFrom-Json
    $sourcePassengerCount = if ($sourcePassengers -is [array]) { $sourcePassengers.Length } else { 1 }
    if ($sourcePassengerCount -eq 0) { throw 'Source passenger list is empty.' }
    $results.Add([pscustomobject]@{ Test = 'Source passenger count'; Status = 'PASS'; Detail = "$sourcePassengerCount rows" })

    $candidates = @($aspHost.PrepareCandidates(2))
    if ($candidates.Count -lt 2) { throw 'Could not find two passengers that have never been queued.' }
    $first = $candidates[0]
    $second = $candidates[1]

    $response = Invoke-LegacyRequest POST '/api/admin/display' @{ sourcePassengerId = $first.Id } $authCookie
    Assert-Status 'Queue first passenger' $response 200
    if (($response.Body | ConvertFrom-Json).status -ne 'Queued') { throw "First passenger was not queued: $($response.Body)" }
    $createdIds.Add([int]$first.Id)

    $response = Invoke-LegacyRequest GET '/api/tablet/current'
    Assert-Status 'Tablet receives pending passenger' $response 200
    if (($response.Body | ConvertFrom-Json).id -ne $first.Id) { throw 'Tablet received an unexpected passenger ID.' }

    $response = Invoke-LegacyRequest POST "/api/tablet/requests/$($first.Id)/decision" @{ decision = 'Invalid' }
    Assert-Status 'Reject invalid decision' $response 400

    $response = Invoke-LegacyRequest POST '/api/admin/display' @{ sourcePassengerId = $second.Id } $authCookie
    Assert-Status 'Reject second passenger while pending' $response 409

    $response = Invoke-LegacyRequest POST "/api/tablet/requests/$($first.Id)/decision" @{ decision = 'Accepted' }
    Assert-Status 'Accept first passenger' $response 200
    if (($response.Body | ConvertFrom-Json).status -ne 'Accepted') { throw 'Accept result is invalid.' }

    $response = Invoke-LegacyRequest POST '/api/admin/display' @{ sourcePassengerId = $first.Id } $authCookie
    Assert-Status 'Reject completed passenger resend' $response 409

    $response = Invoke-LegacyRequest POST '/api/admin/display' @{ sourcePassengerId = $second.Id } $authCookie
    Assert-Status 'Queue second passenger' $response 200
    if (($response.Body | ConvertFrom-Json).status -ne 'Queued') { throw "Second passenger was not queued: $($response.Body)" }
    $createdIds.Add([int]$second.Id)

    $response = Invoke-LegacyRequest POST "/api/tablet/requests/$($second.Id)/decision" @{ decision = 'Declined' }
    Assert-Status 'Decline second passenger' $response 200
    if (($response.Body | ConvertFrom-Json).status -ne 'Declined') { throw 'Decline result is invalid.' }

    $response = Invoke-LegacyRequest GET '/api/admin/dashboard' $null $authCookie
    Assert-Status 'Dashboard after decisions' $response 200
    $dashboard = $response.Body | ConvertFrom-Json
    if ($dashboard.accepted -lt ($baselineDashboard.accepted + 1) -or
        $dashboard.declined -lt ($baselineDashboard.declined + 1)) {
        throw "Dashboard does not reflect accept/decline: $($response.Body)"
    }

    $response = Invoke-LegacyRequest GET '/api/tablet/current'
    Assert-Status 'Tablet empty after decisions' $response 204

    $response = Invoke-LegacyRequest POST '/api/auth/logout' $null $authCookie
    Assert-Status 'Admin logout' $response 204
    if (-not $response.Headers['Set-Cookie']) { throw 'Logout did not expire the authentication cookie.' }

    # A browser applies the expired Set-Cookie response and stops sending the old ticket.
    $response = Invoke-LegacyRequest GET '/api/auth/me'
    Assert-Status 'Reject signed-out session' $response 401

    $results.Add([pscustomobject]@{
        Test = 'Business candidates'
        Status = 'PASS'
        Detail = "Accepted ID $($first.Id); Declined ID $($second.Id)"
    })
}
finally {
    if ($createdIds.Count -gt 0) {
        $cleanupIds = [int[]]$createdIds.ToArray()
        $deleted = $aspHost.CleanupCreatedRows($cleanupIds)
        $results.Add([pscustomobject]@{ Test = 'Cleanup'; Status = 'PASS'; Detail = "$deleted test rows deleted" })
    }

    $finalRows = $aspHost.CountConsentRows()
    if ($finalRows -ne $baselineRows) {
        throw "Cleanup did not restore DPO_CIP: before=$baselineRows, after=$finalRows."
    }
}

$results | Format-Table -AutoSize
Write-Output "DPO_CIP rows before/after: $baselineRows/$($aspHost.CountConsentRows())"
