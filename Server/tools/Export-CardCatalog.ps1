<#
    Writes the server's card-catalog.json (Konami id -> monster/spell/trap, frame, attribute, race, level) so stats can say
    "Dragons summoned", "Fusion summons". No names: the server works in Konami ids; names are resolved later, elsewhere.

    Game cards: read with the WolfX format library (CARD_Props.dll) from an extracted YGO_2020 folder.
    Custom cards: the game's Yu-Gi-Oh-Ex\cards.json (same kind/type/attribute names), when -GameDir is given.

    .\Export-CardCatalog.ps1 -Out ..\src\YgoMp.SelfHost\bin\Debug\net10.0\data\card-catalog.json `
        -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution"
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [string] $Tools = (Join-Path $PSScriptRoot "..\..\Binaries\Debug\Tools"),
    [string] $GameDir = ""
)
$ErrorActionPreference = "Stop"

Add-Type -Path (Join-Path $Tools "CARD_Props.dll")
$bin = Join-Path $Tools "YGO_2020\bin"
$props = [Types.CardPropTable]::Parse([IO.File]::ReadAllBytes((Join-Path $bin "CARD_Prop.bin")))

# CARDS_INFO.CARD_Kind name -> the card's frame (the summon family boards count by).
function Get-Frame([string] $kind) {
    switch -Regex ($kind) {
        '^Spell$' { 'spell'; break }
        '^Trap$' { 'trap'; break }
        'Token' { 'token'; break }
        'Link' { 'link'; break }
        'Xyz|XYZ' { 'xyz'; break }
        'Synchro' { 'synchro'; break }
        'Fusion' { 'fusion'; break }
        'Ritual' { 'ritual'; break }
        'Pendulum' { 'pendulum'; break }
        '^Normal$|Tuner_Normal' { 'normal'; break }
        default { 'effect' }
    }
}

# "BeastWarrior" -> "beast-warrior", "LightMonster" / "Light" -> "light"
function Get-Word([string] $value) {
    (($value -replace 'Monster$', '') -creplace '(?<=[a-z])(?=[A-Z])', '-').ToLowerInvariant()
}

function New-Entry([string] $kind, [string] $type, [string] $attribute, [int] $level) {
    $frame = Get-Frame $kind
    $entry = [ordered]@{ kind = $(if ($frame -in 'spell', 'trap') { $frame } else { 'monster' }); frame = $frame }
    if ($entry.kind -eq 'monster') {
        $entry.attribute = Get-Word $attribute
        $entry.race = Get-Word $type
        if ($level -gt 0) { $entry.level = $level }
    }
    $entry
}

$cards = [ordered]@{}
for ($i = 0; $i -lt $props.Records.Count; $i++) {
    $r = $props.Records[$i]
    if ($r.KonamiId -le 0) { continue }
    $cards["$($r.KonamiId)"] = New-Entry "$($r.Kind)" "$($r.Type)" "$($r.Attribute)" $r.Level
}
$gameCount = $cards.Count

if ($GameDir) {
    $customPath = Join-Path $GameDir "Yu-Gi-Oh-Ex\cards.json"
    if (Test-Path $customPath) {
        foreach ($c in (Get-Content $customPath -Raw | ConvertFrom-Json).cards) {
            $cards["$($c.id)"] = New-Entry "$($c.kind)" "$($c.type)" "$($c.attribute)" ([int]$c.level)
        }
    }
}

New-Item -ItemType Directory -Force (Split-Path -Parent ([IO.Path]::GetFullPath($Out))) | Out-Null
@{ cards = $cards } | ConvertTo-Json -Depth 4 -Compress | Set-Content -Encoding utf8 $Out
"Wrote $($cards.Count) cards ($gameCount game, $($cards.Count - $gameCount) custom) to $Out"
