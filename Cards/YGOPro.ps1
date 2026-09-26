<#
.SYNOPSIS
    Downloads all cards from the ygoprodeck.com search API (paginated) and dumps them into a single cards.json.

.DESCRIPTION
    Hits https://ygoprodeck.com/api/search/cards.php?num=200&offset=0 and follows the
    "next_page_offset" / "pages_remaining" fields in the "paging" object of the response
    until every page has been fetched. All card objects are merged into one array and
    written out as a single JSON file.

.PARAMETER OutFile
    Path to write the combined JSON to. Defaults to .\cards.json

.PARAMETER PageSize
    Number of cards per page (the API's "num" param). Defaults to 200.

.PARAMETER DelayMs
    Delay in milliseconds between requests, to be polite to the API. Defaults to 250.

.EXAMPLE
    .\Get-YgoCards.ps1
    .\Get-YgoCards.ps1 -OutFile C:\data\cards.json -PageSize 200 -DelayMs 500
#>

[CmdletBinding()]
param(
    [string]$OutFile = ".\cards.json",
    [int]$PageSize = 200,
    [int]$DelayMs = 250
)

$ErrorActionPreference = 'Stop'

$baseUrl = "https://ygoprodeck.com/api/search/cards.php"
$offset = 0
$allCards = [System.Collections.Generic.List[object]]::new()

$headers = @{
    "User-Agent" = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) PowerShell-Script/1.0"
    "Accept"     = "application/json"
}

$pageNum = 1
$totalPages = $null

do {
    $uri = "{0}?num={1}&offset={2}" -f $baseUrl, $PageSize, $offset
    Write-Host "Fetching page $pageNum (offset=$offset): $uri" -ForegroundColor Cyan

    $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get

    if ($null -eq $response.cards -or $response.cards.Count -eq 0) {
        Write-Warning "No cards returned at offset $offset — stopping."
        break
    }

    $allCards.AddRange([object[]]$response.cards)

    $paging = $response.paging
    if ($null -eq $totalPages) {
        $totalPages = $paging.total_pages
        Write-Host "Total pages to fetch: $totalPages (total rows: $($paging.total_rows))" -ForegroundColor Yellow
    }

    Write-Host "  -> got $($response.cards.Count) cards (running total: $($allCards.Count) / $($paging.total_rows))"

    if ($paging.pages_remaining -gt 0 -and $paging.next_page_offset) {
        $offset = [int]$paging.next_page_offset
        $pageNum++
        Start-Sleep -Milliseconds $DelayMs
    }
    else {
        break
    }

} while ($true)

Write-Host "Done. Total cards collected: $($allCards.Count)" -ForegroundColor Green

# Write out as a single JSON file (an array of card objects)
$allCards | ConvertTo-Json -Depth 10 | Out-File -FilePath $OutFile -Encoding utf8

Write-Host "Saved to $OutFile" -ForegroundColor Green