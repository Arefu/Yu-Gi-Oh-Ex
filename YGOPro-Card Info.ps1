<#
.SYNOPSIS
    Reads a card list JSON (shape: {"data":[{"name": "..."}, ...]}), looks up each card's
    full info via the YGOProDeck v7 API (with misc=yes for konami_id etc.) IN PARALLEL,
    and dumps each card's JSON into a .\Cards folder. Only failures are printed to the
    console; everything else runs quietly. Cards that can't be found are logged to
    NeedsReview.txt.

    Requires PowerShell 7+ (uses ForEach-Object -Parallel).

.PARAMETER InputFile
    Path to the source card list JSON. Defaults to .\Cards_LOTD-LE.json

.PARAMETER OutDir
    Folder to dump per-card JSON files into. Defaults to .\Cards

.PARAMETER ThrottleLimit
    Max number of concurrent requests. Defaults to 10. Push higher at your own risk
    (the API may start rate-limiting/throttling you).

.EXAMPLE
    .\Get-CardInfo.ps1
    .\Get-CardInfo.ps1 -ThrottleLimit 20
#>

[CmdletBinding()]
param(
    [string]$InputFile = ".\Cards_LOTD-LE.json",
    [string]$OutDir = ".\Cards",
    [int]$ThrottleLimit = 10
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "This script needs PowerShell 7+ for -Parallel support. You're running $($PSVersionTable.PSVersion). Install pwsh 7 and re-run with 'pwsh .\Get-CardInfo.ps1'."
}

if (-not (Test-Path $InputFile)) {
    throw "Input file not found: $InputFile"
}

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir | Out-Null
}

$reviewFile = ".\NeedsReview.txt"
"# Cards needing review — run started $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File $reviewFile -Encoding utf8

$source = Get-Content $InputFile -Raw | ConvertFrom-Json
$cardEntries = $source.data
$names = $cardEntries.name | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

$total = $names.Count
Write-Host "Loaded $total unique card names from $InputFile" -ForegroundColor Cyan
Write-Host "Fetching with throttle limit $ThrottleLimit... (only failures will be printed)" -ForegroundColor Cyan

# Thread-safe collections to gather results back from the parallel runspaces
$reviewLog = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$counter = [System.Collections.Concurrent.ConcurrentDictionary[string,int]]::new()
$counter['success'] = 0
$counter['failed'] = 0
$counter['skipped'] = 0

$names | ForEach-Object -Parallel {
    $cardName   = $_
    $outDir     = $using:OutDir
    $reviewLog  = $using:reviewLog
    $counter    = $using:counter

    function Get-SafeFileName {
        param([string]$Name)
        $invalid = [System.IO.Path]::GetInvalidFileNameChars() -join ''
        $pattern = "[{0}]" -f [System.Text.RegularExpressions.Regex]::Escape($invalid)
        $clean = [System.Text.RegularExpressions.Regex]::Replace($Name, $pattern, '_')
        return $clean.Trim()
    }

    $safeName = Get-SafeFileName -Name $cardName
    $outPath = Join-Path $outDir "$safeName.json"

    if (Test-Path $outPath) {
        [void]$counter.AddOrUpdate('skipped', 1, { param($k,$v) $v + 1 })
        return
    }

    $headers = @{
        "User-Agent" = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) PowerShell-Script/1.0"
        "Accept"     = "application/json"
    }

    # Normalize dash variants (en dash, em dash, minus sign, etc.) to a plain ASCII hyphen.
    # The API's exact-name index doesn't match on the fancy dash characters some source lists use.
    $normalizedName = $cardName -replace '[\u2010\u2011\u2012\u2013\u2014\u2015\u2212]', '-'

    $encodedName           = [uri]::EscapeDataString($cardName)
    $encodedNormalizedName = [uri]::EscapeDataString($normalizedName)

    $uriWithMisc     = "https://db.ygoprodeck.com/api/v7/cardinfo.php?name=$encodedName&misc=yes"
    $uriNoMisc       = "https://db.ygoprodeck.com/api/v7/cardinfo.php?name=$encodedName"
    $uriNormMisc     = "https://db.ygoprodeck.com/api/v7/cardinfo.php?name=$encodedNormalizedName&misc=yes"
    $uriNormNoMisc   = "https://db.ygoprodeck.com/api/v7/cardinfo.php?name=$encodedNormalizedName"
    $uriFname        = "https://db.ygoprodeck.com/api/v7/cardinfo.php?fname=$encodedNormalizedName&misc=yes"

    $response = $null
    $lastError = $null

    function Try-Fetch {
        param([string]$Uri)
        try {
            $r = Invoke-RestMethod -Uri $Uri -Headers $headers -Method Get
            if ($null -ne $r.data -and $r.data.Count -gt 0) { return $r }
            return $null
        }
        catch {
            $script:lastError = $_.Exception.Message
            return $null
        }
    }

    # 1) exact name, with misc
    $response = Try-Fetch -Uri $uriWithMisc
    # 2) exact name, without misc
    if ($null -eq $response) { $response = Try-Fetch -Uri $uriNoMisc }
    # 3) dash-normalized exact name, with misc
    if ($null -eq $response -and $normalizedName -ne $cardName) { $response = Try-Fetch -Uri $uriNormMisc }
    # 4) dash-normalized exact name, without misc
    if ($null -eq $response -and $normalizedName -ne $cardName) { $response = Try-Fetch -Uri $uriNormNoMisc }
    # 5) fuzzy/partial name search as a last resort (may return multiple matches; take the first)
    if ($null -eq $response) {
        $fuzzy = Try-Fetch -Uri $uriFname
        if ($null -ne $fuzzy) {
            $response = $fuzzy
            $reviewLog.Add("$cardName  (matched via fuzzy fname search - verify correct card)")
        }
    }

    if ($null -eq $response) {
        $reviewLog.Add("$cardName  (no data returned$(if ($lastError) { " / last error: $lastError" }))")
        [void]$counter.AddOrUpdate('failed', 1, { param($k,$v) $v + 1 })
        Write-Host "FAILED: $cardName" -ForegroundColor Red
    }
    else {
        $response | ConvertTo-Json -Depth 10 | Out-File -FilePath $outPath -Encoding utf8
        [void]$counter.AddOrUpdate('success', 1, { param($k,$v) $v + 1 })
    }

} -ThrottleLimit $ThrottleLimit

# Flush review log to file
$reviewLog | Sort-Object | Out-File $reviewFile -Append -Encoding utf8

Write-Host ""
Write-Host "Done. $($counter['success']) saved, $($counter['skipped']) already existed, $($counter['failed']) need review." -ForegroundColor Yellow
if ($counter['failed'] -gt 0) {
    Write-Host "See $reviewFile for the full list." -ForegroundColor Yellow
}