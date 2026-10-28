// EffectScript: the small language custom cards' effects are written in (docs/EffectSystem.md, docs/EffectLanguage.md).
//
// A script describes what a card DOES in the game's own vocabulary (draw / search / revive / destroy, with a plain filter);
// the WolfX "Effects" page compiles it to the "effectClone" JSON that Yu-Gi-Oh-Effects runs by re-using the game's own effect
// handlers. The runtime never parses this text.
//
//   search(deck, monster where race = Dragon and level <= 4);
//   revive(grave, monster where attribute = Dark);
//   destroy(all, opponent, monster where level >= 7);
//   destroy(target, opponent, monster where attribute = Dark);
//   destroy(target, spell);
//   draw(2);
//   gain_lp(800);
//   burn(500);
//   on sent_to_grave: search(deck, monster where atk <= 1500);   // monster trigger
//   on normal_summoned: search(deck, monster where race = Warrior); once_per_turn;
//   draw(1) then gain_lp(500) then search(deck, monster where race = Zombie);   // chain: draw / LP steps first, then one action
//   on normal_summoned: search(deck, monster where race = Dragon); once_per_turn; also on sent_to_grave: draw(1);   // two effects
//   on ignition: draw(1); once_per_turn; or on ignition: gain_lp(1000);   // "Once per turn: activate 1 of these effects"
//   cost discard(1): draw(2);                    // a cost, then the effect
//   as("Axe of Despair");                       // behave exactly like a game card (its whole effect; in a duel the card is lent that id)
//   as(4310) with atk 700;                       // ... with its own ATK/DEF amount for equip / boost cards
//   on ignition: as("Ojama Trio") effect_only;   // only the effect: the card keeps its own kind, stats and identity
//   equip(atk 500, def 300);                     // Equip Spell: the equipped monster gains ATK / DEF
//   cost banish_self: draw(1);                   // activated in the GY: "You can banish this card from your GY; draw 1 card"
//   cost discard_self: search(deck, monster where race = Warrior);   // activated in the hand: "You can discard this card; ..."
//   cost tribute_self: special_summon(hand, monster where level <= 4);   // on the field: "You can Tribute this card; ..."
//   if no_monsters: special_summon(self);        // "If you control no monsters: You can Special Summon this card from your hand"
//   special_summon(self_grave);                  // "If this card is in your GY: You can Special Summon this card"
//   special_summon(hand or deck or grave, monster where level <= 4);   // "from your hand, Deck, or GY" (also banished, extra)
//   if controls(monster where archetype = "Gem-Knight"): draw(1);   // "If you control a "Gem-Knight" monster: ..."
//   cost tribute(1): draw(2);                    // "Tribute 1 monster; draw 2 cards"
//   cost detach(1): draw(1);                     // an Xyz Monster: "You can detach 1 material from this card; draw 1 card"
//   return_to_hand(target, opponent, monster);   // "Target 1 monster your opponent controls; return it to the hand"
//   gain_atk(700);                               // "Target 1 face-up monster on the field; it gains 700 ATK until the end of this turn"
//   cost pay_lp(1000): negate(trap);              // "When a Trap Card is activated: Pay 1000 LP; negate the activation, and if you do, destroy it"
//   negate(spell, opponent, equip);              // "When your opponent activates an Equip Spell Card: Negate ..."
//   cost tribute_self: negate(any, opponent);    // a monster's Quick Effect: "You can Tribute this card; negate the activation"
//   lock(trap);                                  // Jinzo: "Trap Cards, and their effects on the field, cannot be activated. Negate all Trap effects"
//   lock(spell, opponent);                       // only the opponent's Spells; lock(monster, spelltrap_zone) = Skill Drain
//
// Regenerate the C# parser (from the repo root; the WolfX project compiles the output):
//   java -jar Dependencies\ANTLR\antlr-4.13.2-complete.jar -Dlanguage=CSharp -visitor -no-listener -Xexact-output-dir -package WolfEx.EffectScriptGenerated -o Tools\WolfX\Content\EffectScriptGenerated "Yu-Gi-Oh_Loader Plugins\Yu-Gi-Oh-Effects\Script\EffectScript.g4"
grammar EffectScript;

// The wrapper is optional: "on flip: burn(500);" is a whole script, "effect \"Name\" { on flip: burn(500); }" means the same.
// Several effects on one card: several effect { } blocks, or bodies joined with 'also' (each borrows its own game card; the runtime routes
// every lookup to the effect whose source answers it, docs/EffectSystem.md section 33).
// 'or' instead of 'also' (or between effect { } blocks) makes the effects one "activate 1 of these effects" group: the player picks one, and
// they share ONE once-per-turn limit (once_per_turn on any of them limits the group; docs/EffectSystem.md section 58).
//   on ignition: draw(1); once_per_turn; or on ignition: burn(500);
script     : continuous* (effect ('or'? effect)* | effectBody (('also' | 'or') effectBody)* | asScript)? continuous* EOF ;
continuous : whileClause? (auraStatement | protectStatement | directStatement) | lockStatement | handLimitStatement | nameStatement | onlyOneStatement
           | xyzAlsoStatement ;

// "You can also Xyz Summon this card by using 1 X you control (as material)": the game's alternative Xyz Summon, its one material tested by
// this rule (Xyz_CanUseAsAltMaterial 0x14003FA70, docs/EffectSystem.md section 94). xyz_monster = an Xyz Monster; rank N / rank_at_least N /
// rank_at_most N = its Rank.
//   xyz_also(monster where archetype = "Utopia", xyz_monster, rank 4);    xyz_also(card where name = 10569);
xyzAlsoStatement : 'xyz_also' '(' selector (',' xyzAlsoArg)* ')' ';'? ;
xyzAlsoArg : 'xyz_monster' | ('rank' | 'rank_at_least' | 'rank_at_most') NUMBER ;

// A condition on an aura / protection / attack_directly: it applies only while every test holds (Yu-Gi-Oh-Effects Lock.cpp "while",
// docs/EffectSystem.md section 89). A count test holds with 1 (or the NUMBER given: N or more) matching card; 'not' = fewer than that.
//   while controls(monster where archetype = "Ancient Warriors", other): untargetable(opponent_effects);   // "While you control another ..."
//   while in_grave(25): aura(atk 2500, def 2500, self);                  // "While you have 25 or more cards in your GY"
//   while not in_hand(card): aura(atk -800, opponent);                   // "While you have no cards in your hand"
//   while not controls(monster, other): indestructible(battle);          // "While you control no other monsters"
//   while has_material: attack_directly;   while lp_at_most(2000): ...;   while defense_position: ...
whileClause : 'while' whileTest ('and' whileTest)* ':' ;
whileTest   : 'not'? countTest | 'lp_at_most' '(' NUMBER ')' | 'defense_position' | 'has_material' ;
// Cards counted for this card's controller: face-up on the field (controls), in the GY / hand / banished; own (default), opponent or any
// side; 'other' leaves this card out; NUMBER = how many are needed (default 1). 'materials' = this card's Xyz materials.
countTest   : ('controls' | 'in_grave' | 'in_hand' | 'banished') '(' countArg (',' countArg)* ')' | 'materials' ;
countArg    : NUMBER | side | 'other' | selector ;

// "This card can attack directly" (Attack_GetDirectAttackRule 0x140144DF0 answers 1; docs/EffectSystem.md section 76).
directStatement : 'attack_directly' ';'? ;

// "You can only control 1 <this card>" (own, the default) / "There can only be 1 face-up <this card> on the field" (field): the game's
// uniqueness rule, Card_GetOnlyOneKind 0x1407448C0 kinds 1 / 2, read by Card_MayPlay_UniqueAndDeclared (docs/EffectSystem.md section 75).
onlyOneStatement : 'only_one' ('(' ('own' | 'field') ')')? ';'? ;

// This card's name becomes another card's (Yu-Gi-Oh-Effects Lock.cpp, docs/EffectSystem.md section 73): while face-up on the field
// (Get_CardCodeAtZone 0x140047060, the game's own table 0x140ACFE80) or, with grave, everywhere it is looked up by id (Get_ProxyCardId
// 0x1400DB180 - the game's Harpie Queen / Dupe Frog "on the field or in the GY" cards use that path too).
//   name_as("Harpie Lady");   name_as(4068, field);
nameStatement : 'name_as' '(' (NUMBER | STRING) (',' ('field' | 'grave'))? ')' ';'? ;

// While this card is face-up (and its effects are not negated): cards cannot be destroyed (Yu-Gi-Oh-Effects Lock.cpp, hooks on
// Battle_CanDestroyByBattle 0x140074430 and Destroy_CanEffectDestroy 0x1400295A0, docs/EffectSystem.md section 71). What: battle, effects
// (any card effect), opponent_effects (your opponent's card effects); who: self (default, this card), own, opponent, any; an optional selector.
//   indestructible(battle);   indestructible(battle, effects);   indestructible(opponent_effects);   indestructible(battle, own, monster where race = Warrior);
// untargetable(...): cannot be targeted - effects, opponent_effects, attacks (Effect_CanTargetCard 0x140023AF0, Check_CanAttackTarget 0x140141DE0).
// unaffected(...): unaffected by spells, traps, monster_effects or their opponent_ forms (Zone_GetImmunityMasks 0x140022C70, section 72).
//   untargetable(opponent_effects);   untargetable(attacks);   unaffected(spells, traps);   unaffected(opponent_monster_effects, own, monster where race = Fiend);
protectStatement : ('indestructible' | 'untargetable' | 'unaffected') '(' protectArg (',' protectArg)* ')' ';'? ;
protectArg       : 'battle' | 'effects' | 'opponent_effects' | 'attacks' | 'spells' | 'traps' | 'monster_effects' | 'opponent_spells' | 'opponent_traps'
                 | 'opponent_monster_effects' | 'own' | 'opponent' | 'any' | 'self' | selector ;

// While this card is face-up (and its effects are not negated): monsters' ATK / DEF change (Yu-Gi-Oh-Effects Lock.cpp, the hook on
// Calc_EffectiveAtkDef_Modifiers 0x1400307C0, docs/EffectSystem.md section 69). Who: own (default), opponent, any (both players' monsters),
// self (this card only); an optional selector (race / attribute / archetype). Several aura(...) statements per card.
//   aura(atk -500, opponent);   aura(atk 300, def 300, own, monster where race = Dragon);   aura(atk 1000, self);
// 'per' multiplies the amounts by a count ("gains 300 ATK for each ..."): aura(atk 300, self, per in_grave(monster where race = Dragon));
//   aura(atk 500, self, per materials);   aura(atk 200, self, per in_hand(card));
auraStatement : 'aura' '(' auraArg (',' auraArg)* ')' ';'? ;
auraArg       : auraStat | 'own' | 'opponent' | 'any' | 'self' | 'per' countTest | selector ;
auraStat      : ('atk' | 'def') '-'? NUMBER ;

// A continuous lock while this card is face-up and its effects aren't negated (Yu-Gi-Oh-Effects Lock.cpp, docs/EffectSystem.md section 44).
// Words (checked by the compiler): what - trap, spell, monster (monster effects negated), continuous_trap, continuous_spell, field_spell,
// equip_spell; who - both (default), opponent, own; where this card must be - monster_zone (default), spelltrap_zone, any_zone.
lockStatement : 'lock' '(' lockArg (',' lockArg)* ')' ';'? ;
lockArg       : IDENT | kind | 'opponent' | 'own' | 'any' | 'field' | 'equip' ;

// While this card is face-up (and not negated): a player's hand size limit in the End Phase becomes N (Yu-Gi-Oh-Effects Lock.cpp, the hook on
// Duel_GetHandLimit 0x14005D120). Who (required): opponent, own, both; where this card must be: monster_zone (default), spelltrap_zone, any_zone.
//   hand_limit(3, opponent);  hand_limit(4, both, spelltrap_zone);
handLimitStatement : 'hand_limit' '(' NUMBER (',' lockArg)* ')' ';'? ;

// The whole behaviour of one game card (by its Konami id, or a name from the effect library), optionally with this card's own stat amounts.
// effect_only: copy the effect and nothing else - the card is not lent the source's id, so it keeps its own kind (monster / Spell / Trap),
// stats and identity (docs/EffectSystem.md section 32). With a trigger in front (on ignition: as("Ojama Trio") effect_only;) the card's
// own monster trigger activates the source's action (composed, "actionFrom", docs/EffectSystem.md section 33).
asScript   : 'as' '(' (NUMBER | STRING) ')' ('with' statPart (',' statPart)*)? effectOnly? ';'? ;
effectOnly : 'effect_only' ;
statPart   : ('atk' | 'def') NUMBER ;
effect     : 'effect' STRING? '{' effectBody '}' ;
effectBody : trigger? asScript | trigger? requireClause? costClause? chain ';'? quickMark? limit? noSummon? ;

// "You cannot Special Summon (monsters) (from the Extra Deck) the turn you activate this (effect), except ...": activating it puts a marker
// keyed by the card's id in the player's list for the turn; while it is there Summon_CheckPlayerRestrictions (0x140058440) refuses the
// summons the rule forbids - the game's own mechanism (docs/EffectSystem.md section 77). Words: extra (only from the Extra Deck), fusion /
// synchro / xyz / link / ritual (the allowed kind), except <selector> (race / attribute / archetype of the allowed monsters), resolved ("for
// the rest of this turn after this card resolves": the marker is added when the effect has resolved, not when it is activated).
//   on ignition: draw(1); no_special_summon;   on ignition: draw(1); no_special_summon(extra, synchro, except monster where attribute = Dark);
noSummon    : 'no_special_summon' ('(' noSummonArg (',' noSummonArg)* ')')? ';'? ;
noSummonArg : 'extra' | 'except' selector | IDENT ;

// "(Quick Effect)": spell speed 2, usable in either player's turn and in a chain (Yu-Gi-Oh-Effects "quick", docs/EffectSystem.md section 52).
quickMark  : 'quick' ';'? ;

// A cost paid when the effect is activated ("Discard 1 card; ...", "Pay 800 LP; ..."): the game's own cost functions, docs/EffectSystem.md section 34.
costClause : 'cost' (discardCost | payCost | selfCost | detachCost | tributeCost | pickCost) ':' ;
// Pick costs with no donor card (docs/EffectSystem.md section 90): the game's cost machine for the verb runs as this card, with this filter.
//   cost tribute(1, monster where race = Dragon):   cost discard(1, spell):   cost send_hand(1, monster where attribute = Water):
//   cost reveal(monster where archetype = "Fiend"):   (also banish_grave / banish_self_and below)
tributeCost : 'tribute' '(' NUMBER (',' selector)? ')' ;   // Tribute N monsters you control (that match)
detachCost : 'detach' '(' NUMBER ')' ;   // detach N materials from this card (an Xyz Monster's ignition effect)
discardCost : 'discard' '(' NUMBER (',' selector)? ')' ;   // discard N cards (that match) from your hand
payCost    : 'pay_lp' '(' NUMBER ')' ;
// The card pays with itself, which also says where it is activated from (docs/EffectSystem.md section 37): banish this card from your GY,
// discard this card (from the hand), Tribute this card (on the field), send this card from your hand to the GY (send_self: a move, not a
// discard), send this card from the field to the GY (send_self_field) - section 84.
selfCost   : 'banish_self' | 'discard_self' | 'tribute_self' | 'send_self' | 'send_self_field'
           | 'banish_self_hand' | 'banish_self_field' | 'tribute_self_hand' ;   // banish this card from your hand / the field, Tribute it from your hand
// "banish N <selector> from your GY" / "banish this card and N <selector> from your GY" (section 85): the game's Cost_BanishPickedCards with
// this effect's own pick list.
// send_hand: send N matching cards from your hand to the GY; reveal: reveal 1 matching card in your hand; send_hand_field: send 1 matching
// card from your hand or field to the GY (section 90).
pickCost   : ('banish_grave' | 'banish_self_and' | 'send_hand') '(' NUMBER ',' selector ')' | ('reveal' | 'send_hand_field') '(' selector ')' ;

// A condition the game checks before the effect may be activated ("If you control no monsters: ...").
// Several conditions: 'and' ("During your opponent's Main Phase, if you control no monsters": if opponent_main_phase and no_monsters:).
requireClause : 'if' condName ('and' condName)* ':' ;
condName   : 'no_monsters' | 'controls' '(' selector ')'   // you control no monsters / you control a face-up monster matching the selector
           | 'battle_phase' | 'your_battle_phase' | 'opponent_battle_phase'   // during the (your / your opponent's) Battle Phase (section 81)
           | 'main_phase' | 'your_main_phase' | 'opponent_main_phase'   // during the (your / your opponent's) Main Phase (section 93)
           | 'main_or_battle_phase' | 'your_main_or_battle_phase' | 'opponent_main_or_battle_phase'
           | 'your_turn' | 'opponent_turn'   // during your / your opponent's turn
           | 'battled'   // this card battled this turn (the game's marker 1024 on its zone)
           | 'lp_at_most' '(' NUMBER ')' | 'lp_lower' ;   // your LP are N or less / lower than your opponent's (section 82)

// "You can only use this effect of X once per turn": per card name (the game's use-limit classes 5-8, docs/EffectSystem.md section 20).
// once_per_turn(copy): "Once per turn: ..." - each copy on the field once per turn (classes 1-4, a marker on the card's own zone, section 59).
// once_per_duel: "You can only use this effect of this card once per Duel" (classes 9 / 10: the same keys, a marker that is never swept, s74).
// uses_per_turn(N): "... twice / thrice per turn" - N uses per copy on the field (classes 1-4 with Get_UseLimitPerCopyCount answering N, s74).
limit      : 'once_per_turn' ('(' IDENT ')')? ';'? | 'once_per_duel' ';'? | 'uses_per_turn' '(' NUMBER ')' ';'? ;

// When the effect happens, for a MONSTER effect. No trigger = a Spell (activated from the hand / field). A card that lists no trigger gets the
// behaviour of a vanilla Normal Spell; with a trigger it borrows a vanilla monster that has that trigger and that action (trigger_sources.json).
trigger    : 'on' triggerName summonConditions? ':' ;
// How the card must have been summoned, for the summon triggers: on special_summoned(from grave, by effect): / on normal_summoned(tribute 2):
//   from hand | deck | extra | grave | banished | spelltrap      by effect | not_effect | own | spell | trap | monster | "Archetype" | <card id>
//   fusion | synchro | xyz | ritual | link (Summoned as its kind)   pendulum   tribute [N]
summonConditions : '(' summonCondition (',' summonCondition)* ')' ;
// The summon reactions also take the summoned monster and whose field it went to (section 87):
//   on monster_summoned(is monster where race = Insect, own): ...   ("If an Insect monster(s) is Normal or Special Summoned to your field")
summonCondition  : 'from' summonFrom | 'by' summonBy | 'is' selector | side | ('tribute' | IDENT) NUMBER? | 'other' | 'in_grave' ;
// The watcher triggers (another card was destroyed / left the field / was sent to the GY / was banished, section 91) take the same list:
//   is <selector> (the card), own / opponent (whose it was), from <zone>, by battle | effect | opponent (an opponent's card effect),
//   other (not a card with this card's name), in_grave (this card watches from the GY: "..., while this card is in your GY").
//   on card_destroyed(is monster where race = Insect, own, by battle): ...   on card_sent_to_grave(is monster, opponent, from hand, from deck, in_grave): ...
summonFrom : 'hand' | 'deck' | 'extra' | 'grave' | 'banished' | 'field' | kind | IDENT ;   // every zone word that is a keyword elsewhere must be listed, IDENT never matches a keyword
// The move triggers (sent_to_grave, sent_from_field_to_grave, destroyed_by_battle, banished) take the same list as a "moveCondition"
// (docs/EffectSystem.md section 65): from hand | deck | grave | field | monster | spelltrap, by battle | effect.
//   on banished(from grave): ...   on sent_to_grave(from hand, from field): ...   on sent_to_grave(by effect): ...
summonBy   : 'effect' | 'own' | 'battle' | 'opponent' | kind | STRING | NUMBER | IDENT ;
triggerName : 'normal_summoned' | 'special_summoned' | 'summoned' | 'normal_or_special_summoned' | 'flip'
            | 'destroyed_by_battle' | 'sent_to_grave' | 'sent_from_field_to_grave'
            | 'standby_phase' | 'end_phase' | 'destroys_by_battle' | 'battle_damage' | 'attack_declared' | 'ignition'
            // whose phase (a monster on the field): your opponent's / each player's Standby or End Phase (docs/EffectSystem.md section 60)
            | 'opponent_standby_phase' | 'each_standby_phase' | 'opponent_end_phase' | 'each_end_phase'
            | 'grave_end_phase' | 'banished_end_phase'   // End Phase, in the GY / banished because it went there this turn (section 79)
            | 'damage_step_battles' | 'damage_step_attacks'   // at the start of the Damage Step, if this card battles / attacks (section 80)
            // at the start of the (your / your opponent's) Battle Phase, at the end of the (your) Battle Phase (section 93)
            | 'battle_phase_start' | 'your_battle_phase_start' | 'opponent_battle_phase_start' | 'battle_phase_end' | 'your_battle_phase_end'
            | 'banished'   // "If this card is banished": the plugin's own move trigger (section 65)
            | 'in_hand'    // "If this card is in your hand: You can ...": an own hand effect (section 66)
            | 'opponent_attack_declared' | 'attacked'   // "When an opponent's monster declares an attack", "When this card is targeted for an attack" (section 67)
            // reacting to another card's summon: "If your opponent Special Summons a monster(s)", "... Normal or Special Summons ...",
            // "If a monster(s) is Special Summoned" (section 68)
            | 'opponent_special_summoned' | 'opponent_summoned' | 'monster_special_summoned' | 'monster_summoned'
            // another card destroyed / left the field / sent to the GY / banished (section 91)
            | 'card_destroyed' | 'card_left_field' | 'card_sent_to_grave' | 'card_banished'
            | 'in_grave' ;   // "If this card is in your GY: You can ...": activated from the GY (the GY / hand effects table, no cost)

// One action, or several joined with 'then' (run in order, each after the previous resolved).
chain      : action ('then' action)* ;

action     : drawAction | searchAction | reviveAction | destroyAction | gainAction | burnAction | sendAction | equipAction
           | summonAction | salvageAction | banishAction | banishFromAction | gainAtkAction | returnAction | negateAction | lockTurnAction
           | damageAction | setAction | fusionAction | moveAction | targetAction ;

// Act on 1 card on the field chosen when the effect is activated, with the game's own target machines (docs/EffectSystem.md section 92):
// change_position = change its battle position (Attack <-> Defense, a face-down one is flipped face-up), set_face_down = change it to
// face-down Defense Position, take_control = take control of it until the End Phase, negate_effects = negate its effects until the end of this
// turn; change_atk = it gains (loses, with '-') N ATK until the end of this turn. 'attack' / 'defense' = only a monster in that position.
//   change_position(target, opponent, monster, attack)    set_face_down(target, any, monster where race = Fiend)    take_control(target, opponent)
//   negate_effects(target, opponent)    change_atk(target, own, monster where archetype = "Penguin", 600)    change_atk(target, opponent, -1500)
targetAction : ('change_position' | 'set_face_down' | 'take_control' | 'negate_effects') '(' 'target' (',' side)? (',' selector)? (',' targetPosition)? ')'
             | 'change_atk' '(' 'target' (',' side)? (',' selector)? (',' targetPosition)? ',' '-'? NUMBER ')' ;
targetPosition : 'attack' | 'defense' ;

// Move cards (docs/EffectSystem.md section 86): 'target' = 1 card on the field chosen when the effect is activated, 'self' = this card,
// hand / grave = 1 card picked from that list when it resolves. 'target', N = up to N targets. Destinations: to_hand, to_grave (sent, not
// destroyed), to_banished, to_deck (shuffled in), to_deck_top, to_deck_bottom, to_extra, destroy.
//   move(target, opponent, monster, to_deck)    move(target, 2, any, card, to_hand)    move(self, to_hand)    move(grave, monster where race = Fish, to_deck)
moveAction : 'move' '(' moveWhat ',' moveDest ')' ;
// moveFrom with a NUMBER: up to N cards, picked one after another when it resolves (section 95)
moveWhat   : 'target' (',' NUMBER)? (',' side)? (',' selector)? | 'self' | moveFrom ('or' moveFrom)* (',' NUMBER)? (',' choices)? ;
moveFrom   : 'hand' | 'grave' ;
moveDest   : 'to_hand' | 'to_grave' | 'to_banished' | 'to_deck' | 'to_deck_top' | 'to_deck_bottom' | 'to_extra' | 'destroy' ;

// "Fusion Summon 1 "X" Fusion Monster from your Extra Deck, using monsters from your hand or field as material": Polymerization's machine
// (0x14000A710) with its Fusion list row filtered (deck.scan "fusion", docs/EffectSystem.md section 64)
// banish_materials: "by banishing the Fusion Materials listed on it from your field or GY" (Dragon's Mirror's material route, section 94)
fusionAction  : 'fusion_summon' '(' (choices (',' fusionRoute)? | fusionRoute)? ')' ;
fusionRoute   : 'banish_materials' ;

// "Set 1 X Spell/Trap directly from your Deck": the game's set-from-Deck machine (0x14022A0A0, Altergeist Marionetter's) picks from the list,
// asks the Spell & Trap Zone and Sets it (docs/EffectSystem.md section 63)
setAction     : 'set' '(' 'deck' (',' choices)? ')' ;

// draw / gain_lp / damage take an optional player: own (default for draw / gain_lp), opponent, any (= both players). A chain of these, or one
// that names a player, is run by Yu-Gi-Oh-Effects itself ("steps": the game's draw and life point functions, docs/EffectSystem.md section 50).
drawAction    : 'draw' '(' NUMBER (',' side)? ')' ;
// For the rest of this turn: trap (Traps on the field negated, Trap Stun), spell (Equip / Field / Continuous / Quick-Play Spells negated, Magic
// Deflector), defense_position (Defense Position monsters' effects negated, Gorgon's Eye). The card's resolve step adds that card's turn
// marker (Action_AddTurnFlagMarker 0x140177EE0), which the game's lock builder reads (docs/EffectSystem.md section 48).
lockTurnAction : 'lock_turn' '(' lockTurnArg ')' ;
lockTurnArg    : IDENT | kind ;
// add 1 (or up to NUMBER) matching card(s) from your Deck (or GY / banished cards) to your hand: the plugin's own pick-and-act step, no donor
// (docs/EffectSystem.md section 98)
searchAction  : 'search' '(' searchZones (',' NUMBER)? (',' choices)? ')' ;
searchZones   : searchZone ('or' searchZone)* ;
searchZone    : 'deck' | 'grave' | 'banished' ;
reviveAction  : 'revive' '(' graveyard (',' choices)? ')' ;
// 'all' matching cards, 'target' 1 card chosen at activation, or 'one' card chosen when it resolves (no target; the plugin's own field pick,
// docs/EffectSystem.md section 56)
destroyAction : 'destroy' '(' scope (',' side)? (',' selector)? ')' ;
scope      : 'all' | 'target' | 'one' ;
// send 1 matching card from your Deck / hand / Extra Deck to the Graveyard (not the Deck: the plugin's own pick-and-act machine, section 55)
sendAction    : 'send_to_grave' '(' ('deck' | 'hand' | 'extra') (',' choices)? ')' ;
gainAction    : 'gain_lp' '(' NUMBER (',' side)? ')' ;     // you (or the named player) gain N Life Points
damageAction  : 'damage' '(' NUMBER (',' side)? ')' ;      // N damage to the named player (default: your opponent, like burn)
burnAction    : 'burn' '(' NUMBER ')' ;        // inflict N damage to your opponent
equipAction   : 'equip' '(' statPart (',' statPart)* ')' ;
// Special Summon 1 matching monster from your hand / Deck / GY / banished cards / Extra Deck, or this card from the hand. Several zones joined
// with 'or' make one list over all of them ("from your hand, Deck, or GY": special_summon(hand or deck or grave, monster where level <= 4)).
summonAction  : 'special_summon' '(' summonZones (',' choices)? ')' ;
summonZones   : summonZone ('or' summonZone)* ;
summonZone    : 'hand' | 'deck' | 'grave' | 'banished' | 'extra' | 'self' | 'self_grave' ;   // self = this card from the hand, self_grave = this card from the GY
returnAction  : 'return_to_hand' '(' 'target' (',' side)? (',' selector)? ')' ;   // target 1 matching card on the field; return it to the hand
gainAtkAction : 'gain_atk' '(' (side ',')? NUMBER ')' ;   // target 1 face-up monster (on the field / you control); it gains N ATK until the end of this turn
// add 1 (or up to NUMBER, picked one after another: section 95) matching card(s) from your GY / banished cards to your hand
salvageAction : 'add_to_hand' '(' ('grave' | 'banished') (',' NUMBER)? (',' choices)? ')' ;
banishAction  : 'banish' '(' scope (',' side)? (',' selector)? ')' ;   // banish 'all' matching cards, or 'target' 1 chosen card (as destroy)
// banish 1 matching card from your Deck / hand / Extra Deck / GY (own machine, sections 55-56)
banishFromAction : 'banish' '(' ('deck' | 'hand' | 'extra' | 'grave') (',' choices)? ')' ;
// Negate the activation of a card / effect, and if you do, destroy it (docs/EffectSystem.md section 39). First argument = what: spell, trap,
// monster (a monster effect), spelltrap, any, or a card ("Monster Reborn" / id). Then any of: opponent (only the opponent's), your_turn,
// opponent_turn, battle_phase, targets_one, effect (an effect too, not only a card's activation), and a Spell/Trap property: normal, counter,
// field, equip, continuous, quickplay, ritual. The words are checked by the compiler (no new reserved words).
negateAction  : 'negate' '(' negateArg (',' negateArg)* ')' ;
negateArg     : IDENT | NUMBER | STRING | kind | 'any' | 'opponent' | 'field' | 'equip' | 'effect' ;

graveyard  : 'grave' | 'opponent_grave' | 'either_grave' ;
side       : 'own' | 'opponent' | 'any' ;

// "monster where race = Dragon and level <= 4"  (the kind word and the where-part are both optional)
// A search / revive / add-to-hand never picks a card with this card's own name ("except <this card>", as real cards say it) unless the
// selector ends with 'including self'.
selector   : (kind ('where' condition ('and' condition)*)? | 'where'? condition ('and' condition)*) includingSelf? ;
includingSelf : 'including' 'self' ;
// The cards a search / revive / add-to-hand / Special Summon / send may pick: one selector, or several joined with 'or' (any of them):
//   search(deck, monster where race = Warrior or monster where race = Dragon)
choices    : selector ('or' selector)* ;
kind       : 'monster' | 'spell' | 'trap' | 'card' ;
condition  : field op value ;
field      : 'race' | 'attribute' | 'level' | 'atk' | 'archetype' | 'name' ;
op         : '=' | '<=' | '>=' | '<' | '>' ;
value      : NUMBER | IDENT | STRING ;

NUMBER       : [0-9]+ ;
IDENT        : [A-Za-z_] [A-Za-z_0-9]* ;
STRING       : '"' (~["\r\n])* '"' ;
WS           : [ \t\r\n]+ -> skip ;
LINE_COMMENT : '//' ~[\r\n]* -> skip ;
