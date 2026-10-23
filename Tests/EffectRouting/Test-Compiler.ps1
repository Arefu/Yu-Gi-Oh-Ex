param([Parameter(Mandatory)][string]$BuildPath)
$ErrorActionPreference = 'Stop'
$BuildPath = (Resolve-Path -LiteralPath $BuildPath).Path
[Reflection.Assembly]::LoadFrom((Join-Path $BuildPath 'Antlr4.Runtime.Standard.dll')) | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildPath 'WolfX.dll'))
$compiler = $assembly.GetType('WolfEx.EffectScriptCompiler', $true)
$compile = $compiler.GetMethod('Compile', [Reflection.BindingFlags]'Static,Public')
$tree = $assembly.GetType('WolfEx.EffectScriptTree', $true).GetMethod('ToJson', [Reflection.BindingFlags]'Static,Public')
# The test host is PowerShell, so seed the compiler's cache from the shipped data.
$sources = [System.Text.Json.Nodes.JsonNode]::Parse([string](Get-Content -LiteralPath (Join-Path $BuildPath 'trigger_sources.json') -Raw))
$compiler.GetField('_sources', [Reflection.BindingFlags]'Static,NonPublic').SetValue($null, $sources)
$cases = @(
    @{script='as(6784); also as(4844);'; ids=@(6784,4844)},
    @{script='as(4844); also as(6784);'; ids=@(4844,6784)},
    @{script='effect "Monster" { as(6784); } effect "Pendulum" { as(4844); }'; ids=@(6784,4844)},
    @{script='as(6784); also draw(2);'; ids=@(6784,4844)},
    @{script='draw(2); also as(6784);'; ids=@(4844,6784)},
    @{script='as(6784); also as(4844); also as(7892);'; ids=@(6784,4844,7892)},
    @{script='as(4310) with atk 700; also draw(2);'; ids=@(4310,4844)},
    @{script='lock(spell); as(6784); also as(4844);'; ids=@(6784,4844)},
    @{script='as(6784);'; ids=@(6784)},
    @{script='draw(2);'; ids=@(4844)},
    @{script='on normal_summoned: draw(1); also on sent_to_grave: draw(2);'; ids=$null}
)
$snapshots = @()
foreach ($case in $cases) {
    $result = $compile.Invoke($null, @([string]$case.script))
    if (!$result.Ok) { throw "$($case.script): $($result.Error)" }
    $compiled = $result.Compiled.ToJsonString() | ConvertFrom-Json
    $ids = @($compiled.from)
    foreach ($part in $compiled.parts) { $ids += $part.from }
    if ($case.ids -and (($ids -join ',') -ne ($case.ids -join ','))) { throw "Source order changed: $($case.script)" }
    if ($case.script -match 'atk 700' -and $compiled.stats.atk -ne 700) { throw 'Clone stats were lost.' }
    if ($case.script -match '^lock' -and !$compiled.lock.spells) { throw 'Lock was lost.' }
    $parsed = $tree.Invoke($null, @([string]$case.script)) | ConvertFrom-Json
    if ($parsed.error) { throw $parsed.error }
    $snapshots += @{script=$case.script; tree=$parsed.tree; compiled=$compiled}
    Write-Output "PASS $($case.script)"
}
$snapshots | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $BuildPath 'multi-effect-test-trees.json') -Encoding utf8
$roundtripPath = Join-Path $BuildPath 'multi-effect-block-roundtrips.json'
if (Test-Path -LiteralPath $roundtripPath) {
    foreach ($case in (Get-Content -LiteralPath $roundtripPath -Raw | ConvertFrom-Json)) {
        $result = $compile.Invoke($null, @([string]$case.script))
        if (!$result.Ok) { throw "Block round trip did not compile: $($result.Error)" }
        $expected = [System.Text.Json.Nodes.JsonNode]::Parse([string]($case.expected | ConvertTo-Json -Depth 100 -Compress))
        if (![System.Text.Json.Nodes.JsonNode]::DeepEquals($result.Compiled, $expected)) { throw "Block round trip changed the effect: $($case.script)" }
        Write-Output "PASS compiled block round trip: $($case.script)"
    }
}
