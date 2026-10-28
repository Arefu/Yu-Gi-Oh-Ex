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
    @{script='on normal_summoned: draw(1); also on sent_to_grave: draw(2);'; ids=$null},
    @{script='as(5738) effect_only;'; ids=@(5738)},   # Ojama Trio (names need card_names.json next to the host)
    @{script='on ignition: as(5738) effect_only;'; ids=$null},
    @{script='on in_grave: as(5738);'; ids=$null},
    # Special Summon from several zones: one own summon step on Unexpected Dai's row, one "or" alternative per zone (and selector)
    @{script='special_summon(hand or deck or grave, monster where level <= 4);'; ids=$null; scans=@('handSummon','deckSummon','graveSummon')},
    @{script='special_summon(banished, monster);'; ids=$null; scans=@('banishedSummon')},
    @{script='special_summon(extra, monster where race = Dragon);'; ids=$null; scans=@('extraSummon')},
    @{script='special_summon(grave or banished, monster where race = Zombie or monster where race = Fiend);'; ids=$null; scans=@('graveSummon','graveSummon','banishedSummon','banishedSummon')},
    @{script='on ignition: special_summon(hand or deck or grave or banished or extra, monster);'; ids=$null; scans=@('handSummon','deckSummon','graveSummon','banishedSummon','extraSummon')},
    @{script='special_summon(hand, monster);'; ids=$null; scans=@('handSummon')},   # section 98: every zone is the own step on a raw row
    @{script='special_summon(deck, monster);'; ids=$null; scans=@('deckSummon')},
    # "activate 1 of these effects": 'or' joins effects into one limit group (shared once-per-turn)
    @{script='on ignition: draw(1); once_per_turn; or on ignition: gain_lp(1000);'; ids=$null; groups=@(0,0); once=@($true,$true)},
    @{script='on ignition: draw(1); or on ignition: gain_lp(1000); also on sent_to_grave: draw(1); once_per_turn;'; ids=$null; groups=@(0,0,$null); once=@($false,$false,$true)},
    @{script='on ignition: burn(500); also on ignition: draw(1); once_per_turn; or on ignition: gain_lp(500); once_per_turn;'; ids=$null; groups=@($null,1,1); once=@($false,$true,$true)},
    @{script='effect "Draw" { on ignition: draw(1); once_per_turn; } or effect "Gain" { on ignition: gain_lp(1000); } effect { on sent_to_grave: draw(1); }'; ids=$null; groups=@(0,0,$null); once=@($true,$true,$false)},
    # once_per_turn(copy): "Once per turn: ..." (each copy on the field) -> oncePerTurnPerCopy; a group takes per name when any member says so
    @{script='on ignition: draw(1); once_per_turn(copy);'; ids=$null; groups=@($null); once=@($false); copy=@($true)},
    @{script='on ignition: draw(1); once_per_turn(copy); or on ignition: gain_lp(500);'; ids=$null; groups=@(0,0); once=@($false,$false); copy=@($true,$true)},
    @{script='on ignition: draw(1); once_per_turn(copy); or on ignition: gain_lp(500); once_per_turn;'; ids=$null; groups=@(0,0); once=@($true,$true); copy=@($false,$false)},
    @{script='on ignition: draw(1); once_per_turn(copy); also on ignition: burn(300); once_per_turn;'; ids=$null; groups=@($null,$null); once=@($false,$true); copy=@($true,$false)},
    # whose phase: the plugin's own trigger ("ownTrigger": own row + own phase row), "from" = the action's card, never lent (effectOnly)
    @{script='on opponent_standby_phase: burn(300);'; ids=$null; phase='opponent_standby_phase'},
    @{script='on each_standby_phase: draw(1); once_per_turn(copy);'; ids=@(4844); phase='each_standby_phase'},
    @{script='on opponent_end_phase: gain_lp(500);'; ids=$null; phase='opponent_end_phase'},
    @{script='on each_end_phase: search(deck, monster where race = Dragon);'; ids=$null; phase='each_end_phase'},
    @{script='on standby_phase: search(deck, monster where race = Dragon);'; ids=$null; phase='standby_phase'},   # no game card: own trigger too
    @{script='on end_phase: burn(300);'; ids=@(5974); phase='end_phase'; lent=$true},                              # a game card does exactly this: kept
    @{script='on each_end_phase: as(4844) effect_only;'; ids=@(4844); phase='each_end_phase'},
    # set(deck, ...): Pot of Greed's Spell row + Altergeist Marionetter's set-from-Deck machine (actionFrom 13228); with a trigger, composed
    @{script='set(deck, spell);'; ids=@(4844); setFrom=$true},
    @{script='on normal_summoned: set(deck, trap where archetype = "Altergeist");'; ids=$null; setFrom=$true},
    @{script='on each_standby_phase: set(deck, card);'; ids=$null; setFrom=$true; phase='each_standby_phase'},
    # fusion_summon(...): Polymerization (4837) with its Fusion list row filtered (deck.scan "fusion"); with a trigger, composed onto it
    @{script='fusion_summon();'; ids=@(4837); fusion=$true},
    @{script='fusion_summon(monster where archetype = "Frightfur");'; ids=@(4837); fusion=$true},
    @{script='on normal_summoned: fusion_summon(monster where race = Dragon);'; ids=$null; fusion=$true},
    # own move triggers: banished / sent_to_grave with a moveCondition (section 65)
    @{script='on banished: draw(1);'; ids=$null; move='banished'},
    @{script='on banished(from grave): search(deck, monster);'; ids=$null; move='banished'; moveFrom='grave'},
    @{script='on sent_to_grave(from hand, by effect): draw(1);'; ids=$null; move='sent_to_grave'; moveFrom='hand'},
    # aura(...): continuous ATK / DEF changes (section 69); with and without an effect, with a selector
    @{script='aura(atk -500, opponent);'; ids=$null; aura=1},
    @{script='aura(atk 300, def 300, own, monster where race = Dragon); aura(atk 1000, self); on ignition: draw(1);'; ids=$null; aura=2},
    # indestructible(...): cannot be destroyed (section 71); self by battle, own Warriors by both, by the opponent's effects beside an effect
    @{script='indestructible(battle);'; ids=$null; protect='battle,self,'},
    @{script='indestructible(battle, effects, own, monster where race = Warrior);'; ids=$null; protect='both,you,'},
    @{script='indestructible(opponent_effects); on ignition: draw(1);'; ids=$null; protect='effect,self,opponent'},
    # untargetable(...) / unaffected(...) (section 72)
    @{script='untargetable(opponent_effects);'; ids=$null; protect='target,self,opponent'},
    @{script='untargetable(attacks, own, monster where race = Warrior);'; ids=$null; protect='attack,you,'},
    @{script='unaffected(spells, traps); on ignition: draw(1);'; ids=$null; protect='spells traps,self,'},
    @{script='unaffected(opponent_monster_effects);'; ids=$null; protect='monsters,self,opponent'},
    # name_as(...) (section 73)
    @{script='name_as(4068);'; ids=$null; nameAs='4068,grave'},
    @{script='name_as(6390, field); on ignition: draw(1);'; ids=$null; nameAs='6390,field'},
    # once_per_duel / uses_per_turn(N) (section 74)
    @{script='on ignition: draw(1); once_per_duel;'; ids=$null; duel=$true},
    @{script='on ignition: draw(1); uses_per_turn(2);'; ids=$null; uses=2},
    @{script='on in_hand: draw(1) or on in_grave: draw(1); once_per_duel;'; ids=$null; duel=$true},
    # only_one (section 75)
    @{script='only_one; on ignition: draw(1);'; ids=$null; onlyOne='you'},
    @{script='only_one(field);'; ids=$null; onlyOne='field'},
    # attack_directly (section 76)
    @{script='attack_directly; on ignition: draw(1);'; ids=$null; direct=$true},
    # while <tests>: before aura / protection / attack_directly, and aura(..., per <count>) (section 89)
    @{script='while controls(monster where archetype = 74, other): untargetable(opponent_effects);'; ids=$null
      check={ param($c) $w = @(@($c.protect)[0].while)[0]; $w.count -eq 'field' -and $w.other -and $w.kind -eq 'monster' -and $w.archetype -eq 74 -and $null -eq $w.min }},
    @{script='while in_grave(25): aura(atk 2500, def 2500, self);'; ids=$null; check={ param($c) $w = @(@($c.aura)[0].while)[0]; $w.count -eq 'grave' -and $w.min -eq 25 }},
    @{script='while not in_hand(card): aura(atk -800, opponent);'; ids=$null; check={ param($c) $w = @(@($c.aura)[0].while)[0]; $w.count -eq 'hand' -and $w.min -eq 0 -and $w.max -eq 0 -and $null -eq $w.kind }},
    @{script='while not controls(monster, other) and lp_at_most(2000): indestructible(battle);'; ids=$null
      check={ param($c) $w = @(@($c.protect)[0].while); $w.Count -eq 2 -and $w[0].max -eq 0 -and $w[0].other -and $w[1].count -eq 'lp' -and $w[1].max -eq 2000 }},
    @{script='while has_material: attack_directly;'; ids=$null; check={ param($c) @($c.attackDirectly.while)[0].count -eq 'materials' }},
    @{script='aura(atk 500, self, per materials);'; ids=$null; check={ param($c) @($c.aura)[0].per.count -eq 'materials' -and $null -eq @($c.aura)[0].while }},
    @{script='aura(atk 300, self, per in_grave(monster where race = Dragon));'; ids=$null; check={ param($c) $p = @($c.aura)[0].per; $p.count -eq 'grave' -and $p.race -eq 'dragon' -and $p.kind -eq 'monster' }},
    @{script='while controls(opponent, monster where attribute = Light): aura(atk 300, self); aura(atk 100, self); on ignition: draw(1);'; ids=$null
      check={ param($c) $a = @($c.aura); @($a[0].while)[0].side -eq 'opponent' -and @($a[0].while)[0].attribute -eq 'light' -and $null -eq $a[1].while }},
    # no_special_summon (section 77)
    @{script='on ignition: draw(1); no_special_summon;'; ids=$null; lock='any,,'},
    @{script='on ignition: draw(1); once_per_turn; no_special_summon(extra, synchro, except monster where attribute = Dark);'; ids=$null; lock='extra,synchro,Dark'},
    @{script='on ignition: draw(1); no_special_summon(extra, resolved);'; ids=$null; lock='extra,,'; lockWhen='resolve'},
    # send-to-GY self costs (section 84)
    @{script='cost send_self: draw(1);'; ids=$null; costSlot='sendSelfFromHand'},
    @{script='cost send_self_field: draw(1);'; ids=$null; costSlot='sendSelfFromField'},
    # banish / Tribute this card from the hand or field, two-part costs (section 85)
    @{script='cost banish_self_hand: draw(1);'; ids=$null; costSlot='banishSelfFromHand'},
    @{script='cost banish_self_field: draw(1);'; ids=$null; costSlot='banishSelfFromField'},
    @{script='cost tribute_self_hand: draw(1);'; ids=$null; costSlot='tributeSelfFromHand'},
    @{script='cost tribute_self_hand: draw(1) or cost tribute_self: draw(1); once_per_turn;'; ids=$null; groups=@(0,0); once=@($true,$true); costSlot='tributeSelfFromHand'},
    @{script='cost banish_grave(2, monster where race = Dragon): draw(1);'; ids=$null; costPick='2,'},
    @{script='cost banish_self_and(1, monster where archetype = 74): draw(1);'; ids=$null; costPick='1,True'},
    # raw pick costs (section 90): the game's cost machine runs as this card - no "cost.from" donor, the verb and filter in costPick
    @{script='cost discard(1, spell): draw(1);'; ids=$null; check={ param($c) $p = $c.costPick; $p.verb -eq 'discard' -and $p.raw -and $p.count -eq 1 -and $p.deck.type -eq 'spell' -and $null -eq $c.cost }},
    @{script='cost discard(2): draw(1);'; ids=$null; check={ param($c) $c.costPick.verb -eq 'discard' -and $c.costPick.count -eq 2 -and $null -eq $c.costPick.deck.type -and $null -eq $c.cost }},
    @{script='cost tribute(1, monster where race = Dragon): draw(2);'; ids=$null; check={ param($c) $p = $c.costPick; $p.verb -eq 'tribute' -and $p.raw -and $p.deck.race -eq 'Dragon' -and $null -eq $c.cost }},
    @{script='cost tribute(2): draw(2);'; ids=$null; check={ param($c) $c.costPick.verb -eq 'tribute' -and $c.costPick.count -eq 2 }},
    @{script='cost send_hand(1, monster where attribute = Water): draw(1);'; ids=$null; check={ param($c) $c.costPick.verb -eq 'send' -and $c.costPick.deck.attribute -eq 'Water' }},
    @{script='cost reveal(monster where archetype = 74): draw(1);'; ids=$null; check={ param($c) $c.costPick.verb -eq 'reveal' -and $c.costPick.count -eq 1 -and $c.costPick.deck.archetype -eq 74 }},
    @{script='cost send_hand_field(card): draw(1);'; ids=$null; check={ param($c) $c.costPick.verb -eq 'sendHandOrField' -and $c.costPick.raw }},
    @{script='cost banish_grave(1, monster): draw(1);'; ids=$null; check={ param($c) $c.costPick.verb -eq 'banish' -and $c.costPick.raw }},
    # move(...) (section 86): targets / this card / a list pick to the Deck; a target-first chain
    # raw='row': no donor at all (rawRow, no "from"); raw='slots': raw slots on a trigger row's place (section 88)
    @{script='move(target, opponent, monster, to_deck);'; ids=$null; mv='target,deck'; raw='row'},
    @{script='move(target, any, card, to_hand);'; ids=$null; mv='target,hand'; raw='row'},
    # section 99: a summon trigger is an own trigger, so a raw action has no donor at all (was raw slots on Green Gadget's row)
    @{script='on special_summoned: move(target, opponent, monster where level <= 4, to_deck_bottom);'; ids=$null; mv='target,deckBottom'; raw='row'; own='special_summoned'},
    @{script='move(self, to_hand);'; ids=$null; mv='self,hand'; raw='row'},
    @{script='move(target, 2, any, card, to_hand);'; ids=$null; mv='target,hand'; raw='row'},
    @{script='move(target, 3, opponent, monster, destroy);'; ids=$null; mv='target,destroy'; raw='row'},
    @{script='move(grave, monster where race = Fish, to_deck);'; ids=$null; ownStep='toDeck'; raw='row'},
    @{script='on end_phase: move(target, opponent, card, to_hand);'; ids=$null; mv='target,hand'; raw='row'; own='end_phase'},
    @{script='move(target, any, card, to_deck) then draw(1);'; ids=$null; targetSlots=$true},
    # LP requirements (section 82)
    @{script='if lp_at_most(500): draw(1);'; ids=$null; requireName='lpAtMost'},
    @{script='if lp_lower: draw(1);'; ids=$null; requireName='lpLower'},
    # if controls(card where name = N) (section 78): by name, beside a destroy (no filter row needed)
    @{script='if controls(card where name = 4996): destroy(target, opponent, card);'; ids=$null; controlsName=4996},
    # grave_end_phase (section 79): an own trigger, no game card behind it
    @{script='on grave_end_phase: search(deck, monster where race = Warrior); once_per_turn;'; ids=$null; phase='grave_end_phase'},
    # damage_step_battles (section 80): an own trigger
    @{script='on damage_step_battles: destroy(target, opponent, monster);'; ids=$null; own='damage_step_battles'},
    # if opponent_battle_phase (section 81)
    @{script='if opponent_battle_phase: destroy(target, opponent, monster); quick;'; ids=$null; requireName='opponentBattlePhase'},
    # watcher triggers (section 91): another card destroyed / left the field / sent to the GY / banished; always own, "watch" always written
    @{script='on card_destroyed(is monster where race = Insect, own, by battle): draw(1);'; ids=$null
      check={ param($c) $w = $c.watch; $c.ownTrigger -and $c.trigger -eq 'card_destroyed' -and $w.kind -eq 'monster' -and $w.race -eq 'Insect' -and $w.side -eq 'you' -and $w.by -eq 'battle' }},
    @{script='on card_left_field(own, other, by opponent): search(deck, monster);'; ids=$null
      check={ param($c) $c.watch.other -and $c.watch.by -eq 'opponent' -and $c.ownTrigger }},
    @{script='on card_sent_to_grave(is monster, opponent, from hand, from deck): burn(300);'; ids=$null
      check={ param($c) $w = $c.watch; $w.side -eq 'opponent' -and @($w.from).Count -eq 2 -and $c.trigger -eq 'card_sent_to_grave' }},
    @{script='on card_destroyed(is monster where archetype = 74, own, in_grave): revive(grave, monster); once_per_turn;'; ids=$null
      check={ param($c) $c.watch.inGrave -and $c.watch.archetype -eq 74 }},
    @{script='on card_banished: draw(1);'; ids=$null; check={ param($c) $c.ownTrigger -and $null -ne $c.watch }},
    # target verbs (section 92): the game's own target machines, raw rows, the verb's filter tests
    @{script='change_position(target, opponent, monster, attack);'; ids=$null
      check={ param($c) $c.rawRow -and $c.targetDo -eq 'position' -and $c.filter.position -eq 'attack' -and $c.filter.canChangePosition -and $c.filter.side -eq 'opponent' -and $c.filter.target }},
    @{script='change_position(target, any, monster where race = Insect);'; ids=$null
      check={ param($c) $c.targetDo -eq 'position' -and $c.filter.faceUp -and $c.filter.race -eq 'Insect' -and $null -eq $c.filter.position }},
    @{script='set_face_down(target, opponent);'; ids=$null; check={ param($c) $c.targetDo -eq 'faceDown' -and $c.filter.canChangePosition }},
    @{script='take_control(target, opponent, monster where level <= 6);'; ids=$null
      check={ param($c) $c.targetDo -eq 'control' -and $c.filter.canChangeControl -and !$c.filter.canChangePosition }},
    @{script='take_control(target, opponent) then draw(1);'; ids=$null
      check={ param($c) $c.targetSlots -and $c.filter.canChangeControl -and $c.before[0].targetDo -eq 'control' }},
    @{script='negate_effects(target, opponent, monster where attribute = Dark);'; ids=$null
      check={ param($c) $c.rawRow -and $c.targetDo -eq 'negate' -and $c.filter.faceUp -and $c.filter.attribute -eq 'Dark' -and !$c.filter.canChangePosition }},
    @{script='change_atk(target, opponent, -1500);'; ids=$null; check={ param($c) $c.targetDo -eq 'atk' -and $c.amount -eq -1500 -and $c.filter.faceUp }},
    # up to N cards from the GY / banished, picked one after another (section 95)
    @{script='add_to_hand(banished, 2, monster where race = Cyberse);'; ids=$null
      check={ param($c) $c.rawRow -and $c.own -eq 'toHand' -and $c.ownCount -eq 2 -and $c.deck.scan -eq 'banished' }},
    @{script='add_to_hand(grave, monster where race = Plant);'; ids=$null; check={ param($c) $c.rawRow -and $null -eq $c.from -and $c.own -eq 'toHand' -and $c.deck.scan -eq 'grave' -and $null -eq $c.ownCount }},
    # section 98: search is the own step on a raw row (no Reinforcement of the Army), over every zone named
    @{script='search(deck, monster where race = Warrior);'; ids=$null; check={ param($c) $c.rawRow -and $null -eq $c.from -and $c.own -eq 'toHand' -and $c.deck.scan -eq 'deck' }},
    @{script='search(deck or grave, monster where race = Warrior);'; ids=$null; check={ param($c) $c.rawRow -and $c.deck.scan -eq 'deck' -and @($c.deck.or).Count -eq 1 -and @($c.deck.or)[0].scan -eq 'grave' }},
    @{script='search(grave or banished, 2, card where archetype = 74);'; ids=$null; check={ param($c) $c.ownCount -eq 2 -and $c.deck.scan -eq 'grave' -and @($c.deck.or)[0].scan -eq 'banished' }},
    @{script='on normal_summoned: search(deck or grave, monster);'; ids=$null; check={ param($c) $c.rawRow -and $c.ownTrigger -and $null -eq $c.from -and $c.trigger -eq 'normal_summoned' -and $null -eq $c.actionFrom -and $c.own -eq 'toHand' }},
    @{script='on normal_summoned: draw(1);'; ids=$null; own='normal_summoned'; check={ param($c) $c.from -eq 4844 -and $c.ownTrigger -and $null -eq $c.actionFrom }},
    @{script='on special_summoned(from grave): special_summon(hand, monster where level <= 4);'; ids=$null; own='special_summoned'; check={ param($c) $c.rawRow -and $c.summonCondition -and $null -eq $c.from }},
    @{script='draw(1) then search(deck, spell);'; ids=$null; check={ param($c) $c.rawRow -and $c.own -eq 'toHand' -and $c.before[0].from -eq 4844 }},
    @{script='move(grave, 3, monster where race = Fish, to_deck) then draw(1);'; ids=$null; check={ param($c) $c.before[0].ownCount -eq 3 -and $c.before[0].own -eq 'toDeck' }},
    # alternative Xyz Summon (section 94)
    @{script='xyz_also(monster where archetype = 153, xyz_monster, rank 4);'; ids=$null
      check={ param($c) $c.xyzAlso.archetype -eq 153 -and $c.xyzAlso.xyz -and $c.xyzAlso.rankMin -eq 4 -and $c.xyzAlso.rankMax -eq 4 }},
    @{script='xyz_also(card where name = 10569); on ignition: draw(1);'; ids=$null; check={ param($c) $c.xyzAlso.name -eq 10569 }},
    # ignition is an own trigger (table 3, section 96): no game card's row, so no donor's slot-3 cost comes along (issue #39: Caam 9168)
    @{script='on ignition: draw(1); once_per_turn;'; ids=$null; own='ignition'; check={ param($c) $c.from -eq 4844 -and $c.effectOnly -and $null -eq $c.actionFrom }},
    @{script='effect "destroy yom" { on ignition: destroy(all, opponent); once_per_turn; } or effect "inflict 1000" { on ignition: damage(1000); once_per_turn; } or effect "gain 1000" { on ignition: gain_lp(1000); once_per_turn; }'
      ids=$null; groups=@(0,0,0); once=@($true,$true,$true)
      check={ param($c) $all = @($c) + @($c.parts); @($all | Where-Object { !$_.ownTrigger -or $_.trigger -ne 'ignition' -or $_.from -eq 9168 -or $_.actionFrom -eq 9168 }).Count -eq 0 }},
    @{script='xyz_also(monster, xyz_monster, rank_at_least 5, rank_at_most 6);'; ids=$null; check={ param($c) $c.xyzAlso.rankMin -eq 5 -and $c.xyzAlso.rankMax -eq 6 }},
    # Fusion Summon by banishing the materials from the field / GY (section 94): Dragon's Mirror's route
    @{script='fusion_summon(monster where race = Dragon, banish_materials);'; ids=$null; check={ param($c) $c.from -eq 6433 -and $c.deck.scan -eq 'fusion' }},
    @{script='fusion_summon(banish_materials);'; ids=$null; check={ param($c) $c.from -eq 6433 }},
    @{script='fusion_summon(monster where archetype = 74);'; ids=$null; check={ param($c) $c.from -eq 4837 }},
    # start / end of the Battle Phase (section 93): own triggers; "if this card battled" = battledThisTurn
    @{script='on battle_phase_start: destroy(target, opponent, monster);'; ids=$null; own='battle_phase_start'},
    @{script='on your_battle_phase_start: draw(1);'; ids=$null; own='your_battle_phase_start'},
    @{script='on battle_phase_end: if battled: move(self, to_hand);'; ids=$null; own='battle_phase_end'
      check={ param($c) @($c.require) -contains 'battledThisTurn' }},
    # phase windows (section 93): several conditions with 'and'
    @{script='if opponent_main_phase and no_monsters: special_summon(self); quick;'; ids=$null
      check={ param($c) @($c.require) -contains 'opponentMainPhase' -and @($c.require) -contains 'noMonsters' -and $c.selfDo -eq 'summonFromHand' }},
    # section 100: "Special Summon this card from your hand" = own hand effect + the game's summon-self machine, no Watch Cat
    @{script='special_summon(self);'; ids=$null; own='in_hand'; raw='row'
      check={ param($c) $c.selfDo -eq 'summonFromHand' -and @($c.require) -contains 'inHand' -and @($c.require) -contains 'canSummonSelf' }},
    @{script='if main_or_battle_phase: destroy(target, opponent, monster); quick;'; ids=$null; requireName='mainOrBattlePhase'},
    @{script='if opponent_turn and lp_at_most(2000): draw(1);'; ids=$null
      check={ param($c) @($c.require) -contains 'opponentTurn' -and @($c.require) -contains 'lpAtMost' -and $c.lpAtMost -eq 2000 }},
    @{script='change_atk(target, own, monster where race = Aqua, attack, 600);'; ids=$null
      check={ param($c) $c.targetDo -eq 'atk' -and $c.amount -eq 600 -and $c.filter.position -eq 'attack' -and $c.filter.side -eq 'own' }}
)
foreach ($bad in @('on ignition: draw(1); or on ignition: draw(2); or on ignition: burn(100); or on ignition: burn(200); or on ignition: gain_lp(1);',
                   'on ignition: draw(1); once_per_turn(twice);')) {
    if ($compile.Invoke($null, @($bad)).Ok) { throw "Should not compile: $bad" }
    Write-Output "PASS refused $bad"
}
foreach ($bad in @('special_summon(self or hand);', 'special_summon(hand or hand, monster);', 'indestructible(attacks);', 'unaffected(spells, opponent_traps);', 'untargetable(spells);',
                   'on ignition: draw(1); uses_per_turn(2); or on ignition: burn(100);', 'on ignition: draw(1); uses_per_turn(12);',
                   'cost tribute(1, spell): draw(1);', 'cost discard(6): draw(1);',
                   'on card_destroyed(from hand): draw(1);', 'on card_sent_to_grave(by battle): draw(1);', 'on card_destroyed(is monster where level <= 4): draw(1);',
                   'take_control(target, opponent, spell);', 'xyz_also(monster where level = 4);', 'xyz_also(spell);', 'add_to_hand(grave, 11, monster);', 'change_atk(target, any, 0);', 'change_atk(target, any, 40000);','draw(1) then change_position(target, any) then draw(1);',
                   'while has_material: name_as(4068);','while controls(monster where level <= 4): attack_directly;', 'aura(atk 100, self, per materials, per materials);')) {
    if ($compile.Invoke($null, @($bad)).Ok) { throw "Should not compile: $bad" }
    Write-Output "PASS refused $bad"
}
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
    # (whose-phase triggers and send-to-GY self costs set effectOnly themselves: checked below)
    if (!$case.phase -and !$case.costSlot -and !$case.costPick -and !$compiled.ownTrigger -and !$compiled.rawSlots -and (($case.script -match 'effect_only') -ne [bool]$compiled.effectOnly)) { throw "effect_only was lost or added: $($case.script)" }
    # donor-free (section 88): a raw row has no "from" and no action card; raw slots sit on a row's place without lending it
    if ($case.raw -eq 'row' -and (!$compiled.rawRow -or $null -ne $compiled.from -or $null -ne $compiled.actionFrom)) { throw "not a raw row: $($compiled | ConvertTo-Json -Compress -Depth 6)" }
    if ($case.raw -eq 'slots' -and (!$compiled.rawSlots -or $compiled.rawRow -or $null -ne $compiled.actionFrom -or !$compiled.effectOnly)) { throw "not raw slots: $($compiled | ConvertTo-Json -Compress -Depth 6)" }
    if ($case.script -match '^on \w+: as\((\d+)\)' -and ($compiled.actionFrom -ne [int]$Matches[1] -or !$compiled.trigger)) { throw "Triggered as(...) was not composed: $($case.script)" }
    if ($case.groups) {
        $all = @($compiled) + @($compiled.parts)
        for ($i = 0; $i -lt $case.groups.Count; $i++) {
            if ("$($all[$i].limitGroup)" -ne "$($case.groups[$i])") { throw "Effect $i limit group $($all[$i].limitGroup), expected $($case.groups[$i]): $($case.script)" }
            if ([bool]$all[$i].oncePerTurn -ne $case.once[$i]) { throw "Effect $i once per turn $([bool]$all[$i].oncePerTurn): $($case.script)" }
            $copy = if ($case.copy) { $case.copy[$i] } else { $false }
            if ([bool]$all[$i].oncePerTurnPerCopy -ne $copy) { throw "Effect $i once per turn per copy $([bool]$all[$i].oncePerTurnPerCopy): $($case.script)" }
        }
    }
    if ($case.aura -and (@($compiled.aura).Count -ne $case.aura)) { throw "aura count $(@($compiled.aura).Count): $($case.script)" }
    if ($case.controlsName -and ($compiled.controlsName -ne $case.controlsName -or @($compiled.require) -notcontains 'controlsName')) { throw "controlsName lost: $($case.script)" }
    if ($case.lock -and "$($compiled.summonLock.from),$($compiled.summonLock.kind),$($compiled.summonLock.attribute)" -ne $case.lock) { throw "summonLock $($compiled.summonLock | ConvertTo-Json -Compress): $($case.script)" }
    if ($case.lock -and "$($compiled.summonLock.when)" -ne "$($case.lockWhen)") { throw "summonLock when '$($compiled.summonLock.when)': $($case.script)" }
    if ($case.direct -and !$compiled.attackDirectly) { throw "attackDirectly lost: $($case.script)" }
    if ($case.requireName -and @($compiled.require) -notcontains $case.requireName) { throw "require $($case.requireName) lost: $($case.script)" }
    if ($case.costSlot -and ($compiled.costSlot -ne $case.costSlot -or !$compiled.effectOnly)) { throw "costSlot $($compiled.costSlot): $($case.script)" }
    if ($case.costPick -and ("$($compiled.costPick.count),$($compiled.costPick.self)" -ne $case.costPick -or $compiled.costPick.deck.scan -ne 'grave')) { throw "costPick $($compiled.costPick | ConvertTo-Json -Compress): $($case.script)" }
    if ($case.mv -and "$($compiled.move.scope),$($compiled.move.to)" -ne $case.mv) { throw "move $($compiled.move | ConvertTo-Json -Compress): $($case.script)" }
    if ($case.ownStep -and $compiled.own -ne $case.ownStep) { throw "own $($compiled.own): $($case.script)" }
    if ($case.targetSlots -and (!$compiled.targetSlots -or !$compiled.filter.target -or $compiled.before[0].move.scope -ne 'target')) { throw "targetSlots: $($compiled | ConvertTo-Json -Compress -Depth 6)" }
    if ($case.requireName -eq 'lpAtMost' -and $compiled.lpAtMost -ne 500) { throw "lpAtMost $($compiled.lpAtMost): $($case.script)" }
    if ($case.own -and ($compiled.trigger -ne $case.own -or !$compiled.ownTrigger)) { throw "Not an own $($case.own) trigger: $($case.script)" }
    if ($case.onlyOne -and $compiled.onlyOne -ne $case.onlyOne) { throw "onlyOne $($compiled.onlyOne): $($case.script)" }
    if ($case.check -and !(& $case.check $compiled)) { throw "while / per: $($compiled | ConvertTo-Json -Compress -Depth 8)" }
    if ($case.duel -and !$compiled.oncePerDuel) { throw "oncePerDuel lost: $($case.script)" }
    if ($case.uses -and $compiled.usesPerTurn -ne $case.uses) { throw "usesPerTurn $($compiled.usesPerTurn): $($case.script)" }
    if ($case.nameAs -and ("$($compiled.nameAs.id),$($compiled.nameAs.where)" -ne $case.nameAs)) { throw "nameAs $($compiled.nameAs.id),$($compiled.nameAs.where): $($case.script)" }
    if ($case.protect) {
        $p = @($compiled.protect)[0]
        if ("$($p.from),$($p.side),$($p.by)" -ne $case.protect) { throw "protect $($p.from),$($p.side),$($p.by): $($case.script)" }
    }
    if ($case.move) {
        if ($compiled.trigger -ne $case.move -or !$compiled.ownTrigger) { throw "Move trigger is not an own trigger: $($case.script)" }
        if ($case.moveFrom -and (@($compiled.moveCondition.from) -notcontains $case.moveFrom)) { throw "moveCondition lost its zone: $($case.script)" }
    }
    if ($case.fusion -and ($compiled.deck.scan -ne 'fusion' -or (($compiled.from -ne 4837) -and ($compiled.actionFrom -ne 4837)))) { throw "fusion_summon is not Polymerization with the fusion scan: $($case.script)" }
    if ($case.setFrom -and ($compiled.actionFrom -ne 13228 -or !$compiled.deck)) { throw "set(deck) did not compose the set-from-Deck machine: $($case.script)" }
    if ($case.phase) {
        if ($compiled.trigger -ne $case.phase) { throw "Phase trigger $($compiled.trigger): $($case.script)" }
        if ([bool]$compiled.effectOnly -eq [bool]$case.lent) { throw "effectOnly $([bool]$compiled.effectOnly) for $($case.phase): $($case.script)" }
        if ([bool]$compiled.ownTrigger -eq [bool]$case.lent) { throw "ownTrigger $([bool]$compiled.ownTrigger) for $($case.phase): $($case.script)" }
        if ($compiled.ownTrigger -and (@(6608, 9295, 13914, 6720, 5687, 5974) -contains $compiled.from)) { throw "Own trigger still names a phase donor: $($case.script)" }
    }
    if ($case.scans) {
        $scans = @($compiled.deck.scan) + @($compiled.deck.or | Where-Object { $_ } | ForEach-Object { $_.scan })
        if (($scans -join ',') -ne ($case.scans -join ',')) { throw "Zones changed ($($scans -join ',')): $($case.script)" }
        if ($compiled.own -ne 'summon' -or $compiled.condition -ne 'listHasMatch' -or ($case.script -notmatch '^on ' -and !$compiled.rawRow) -or $null -ne $compiled.from) { throw "Not an own summon step: $($case.script)" }
        if ($case.script -match '^on ' -and (!($compiled.rawSlots -or $compiled.rawRow) -or !$compiled.ownTrigger -or $compiled.trigger -ne 'ignition')) { throw "Triggered zones summon is not an own ignition on its action: $($case.script)" }
    }
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
