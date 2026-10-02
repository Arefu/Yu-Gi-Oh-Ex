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
//   cost discard(1): draw(2);                    // a cost, then the effect
//   as("Axe of Despair");                       // behave exactly like a game card (its whole effect; in a duel the card is lent that id)
//   as(4310) with atk 700;                       // ... with its own ATK/DEF amount for equip / boost cards
//   equip(atk 500, def 300);                     // Equip Spell: the equipped monster gains ATK / DEF
//   cost banish_self: draw(1);                   // activated in the GY: "You can banish this card from your GY; draw 1 card"
//   cost discard_self: search(deck, monster where race = Warrior);   // activated in the hand: "You can discard this card; ..."
//   cost tribute_self: special_summon(hand, monster where level <= 4);   // on the field: "You can Tribute this card; ..."
//   if no_monsters: special_summon(self);        // "If you control no monsters: You can Special Summon this card from your hand"
//   special_summon(self_grave);                  // "If this card is in your GY: You can Special Summon this card"
//   if controls(monster where archetype = "Gem-Knight"): draw(1);   // "If you control a "Gem-Knight" monster: ..."
//   cost tribute(1): draw(2);                    // "Tribute 1 monster; draw 2 cards"
//   cost detach(1): draw(1);                     // an Xyz Monster: "You can detach 1 material from this card; draw 1 card"
//   return_to_hand(target, opponent, monster);   // "Target 1 monster your opponent controls; return it to the hand"
//   gain_atk(700);                               // "Target 1 face-up monster on the field; it gains 700 ATK until the end of this turn"
//   cost pay_lp(1000): negate(trap);              // "When a Trap Card is activated: Pay 1000 LP; negate the activation, and if you do, destroy it"
//   negate(spell, opponent, equip);              // "When your opponent activates an Equip Spell Card: Negate ..."
//   cost tribute_self: negate(any, opponent);    // a monster's Quick Effect: "You can Tribute this card; negate the activation"
//
// Regenerate the C# parser (from the repo root; the WolfX project compiles the output):
//   java -jar Dependencies\ANTLR\antlr-4.13.2-complete.jar -Dlanguage=CSharp -visitor -no-listener -package WolfEx.EffectScriptGenerated -o Tools\WolfX\Content\EffectScriptGenerated "Yu-Gi-Oh_Loader Plugins\Yu-Gi-Oh-Effects\Script\EffectScript.g4"
grammar EffectScript;

// The wrapper is optional: "on flip: burn(500);" is a whole script, "effect \"Name\" { on flip: burn(500); }" means the same.
// Several effects on one card: several effect { } blocks, or bodies joined with 'also' (each borrows its own game card; the runtime routes
// every lookup to the effect whose source answers it, docs/EffectSystem.md section 33).
script     : effect+ EOF | effectBody ('also' effectBody)* EOF | asScript EOF ;

// The whole behaviour of one game card (by its Konami id, or a name from the effect library), optionally with this card's own stat amounts.
asScript   : 'as' '(' (NUMBER | STRING) ')' ('with' statPart (',' statPart)*)? ';'? ;
statPart   : ('atk' | 'def') NUMBER ;
effect     : 'effect' STRING? '{' effectBody '}' ;
effectBody : trigger? requireClause? costClause? chain ';'? limit? ;

// A cost paid when the effect is activated ("Discard 1 card; ...", "Pay 800 LP; ..."): the game's own cost functions, docs/EffectSystem.md section 34.
costClause : 'cost' (discardCost | payCost | selfCost | detachCost | tributeCost) ':' ;
tributeCost : 'tribute' '(' NUMBER ')' ;   // Tribute N monsters you control (1 so far: Share the Pain's cost)
detachCost : 'detach' '(' NUMBER ')' ;   // detach N materials from this card (an Xyz Monster's ignition effect)
discardCost : 'discard' '(' NUMBER ')' ;
payCost    : 'pay_lp' '(' NUMBER ')' ;
// The card pays with itself, which also says where it is activated from (docs/EffectSystem.md section 37): banish this card from your GY,
// discard this card (from the hand), Tribute this card (on the field).
selfCost   : 'banish_self' | 'discard_self' | 'tribute_self' ;

// A condition the game checks before the effect may be activated ("If you control no monsters: ...").
requireClause : 'if' condName ':' ;
condName   : 'no_monsters' | 'controls' '(' selector ')' ;   // you control no monsters / you control a face-up monster matching the selector

// "You can only use this effect once per turn" (per card name; the game's use-limit class 5, docs/EffectSystem.md section 20).
limit      : 'once_per_turn' ';'? ;

// When the effect happens, for a MONSTER effect. No trigger = a Spell (activated from the hand / field). A card that lists no trigger gets the
// behaviour of a vanilla Normal Spell; with a trigger it borrows a vanilla monster that has that trigger and that action (trigger_sources.json).
trigger    : 'on' triggerName ':' ;
triggerName : 'normal_summoned' | 'special_summoned' | 'summoned' | 'normal_or_special_summoned' | 'flip'
            | 'destroyed_by_battle' | 'sent_to_grave' | 'sent_from_field_to_grave'
            | 'standby_phase' | 'end_phase' | 'destroys_by_battle' | 'battle_damage' | 'attack_declared' | 'ignition' ;

// One action, or several joined with 'then' (run in order, each after the previous resolved).
chain      : action ('then' action)* ;

action     : drawAction | searchAction | reviveAction | destroyAction | gainAction | burnAction | sendAction | equipAction
           | summonAction | salvageAction | banishAction | gainAtkAction | returnAction | negateAction ;

drawAction    : 'draw' '(' NUMBER ')' ;
searchAction  : 'search' '(' 'deck' (',' selector)? ')' ;
reviveAction  : 'revive' '(' graveyard (',' selector)? ')' ;
destroyAction : 'destroy' '(' scope (',' side)? (',' selector)? ')' ;   // 'all' matching cards, or 'target' 1 chosen card
scope      : 'all' | 'target' ;
sendAction    : 'send_to_grave' '(' 'deck' (',' selector)? ')' ;   // send 1 card from your Deck to the Graveyard
gainAction    : 'gain_lp' '(' NUMBER ')' ;     // you gain N Life Points
burnAction    : 'burn' '(' NUMBER ')' ;        // inflict N damage to your opponent
equipAction   : 'equip' '(' statPart (',' statPart)* ')' ;
summonAction  : 'special_summon' '(' summonZone (',' selector)? ')' ;   // Special Summon 1 matching monster from your hand / Deck / GY, or this card from the hand
summonZone    : 'hand' | 'deck' | 'grave' | 'self' | 'self_grave' ;   // self = this card from the hand, self_grave = this card from the GY
returnAction  : 'return_to_hand' '(' 'target' (',' side)? (',' selector)? ')' ;   // target 1 matching card on the field; return it to the hand
gainAtkAction : 'gain_atk' '(' (side ',')? NUMBER ')' ;   // target 1 face-up monster (on the field / you control); it gains N ATK until the end of this turn
salvageAction : 'add_to_hand' '(' ('grave' | 'banished') (',' selector)? ')' ;   // add 1 matching card from your GY / banished cards to your hand
banishAction  : 'banish' '(' scope (',' side)? (',' selector)? ')' ;   // banish 'all' matching cards, or 'target' 1 chosen card (as destroy)
// Negate the activation of a card / effect, and if you do, destroy it (docs/EffectSystem.md section 39). First argument = what: spell, trap,
// monster (a monster effect), spelltrap, any, or a card ("Monster Reborn" / id). Then any of: opponent (only the opponent's), your_turn,
// opponent_turn, battle_phase, targets_one, effect (an effect too, not only a card's activation), and a Spell/Trap property: normal, counter,
// field, equip, continuous, quickplay, ritual. The words are checked by the compiler (no new reserved words).
negateAction  : 'negate' '(' negateArg (',' negateArg)* ')' ;
negateArg     : IDENT | NUMBER | STRING | kind | 'any' | 'opponent' | 'field' | 'equip' | 'effect' ;

graveyard  : 'grave' | 'opponent_grave' | 'either_grave' ;
side       : 'own' | 'opponent' | 'any' ;

// "monster where race = Dragon and level <= 4"  (the kind word and the where-part are both optional)
selector   : (kind ('where' condition ('and' condition)*)? | 'where'? condition ('and' condition)*) ;
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
