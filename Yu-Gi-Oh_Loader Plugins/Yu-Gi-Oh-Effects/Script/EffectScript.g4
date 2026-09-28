// EffectScript: the small language custom cards' effects are written in (docs/EffectSystem.md, docs/EffectLanguage.md).
//
// A script describes what a card DOES in the game's own vocabulary (draw / search / revive / destroy, with a plain filter);
// the WolfEx "Effects" tab compiles it to the "effectClone" JSON that Yu-Gi-Oh-Effects runs by re-using the game's own effect
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
//
// Regenerate the C# parser (from the repo root; the WolfEx project compiles the output):
//   java -jar Dependencies\ANTLR\antlr-4.13.2-complete.jar -Dlanguage=CSharp -visitor -no-listener -package WolfEx.EffectScriptGenerated -o Tools\WolfEx\EffectScriptGenerated "Yu-Gi-Oh_Loader Plugins\Yu-Gi-Oh-Effects\Script\EffectScript.g4"
grammar EffectScript;

// The wrapper is optional: "on flip: burn(500);" is a whole script, "effect \"Name\" { on flip: burn(500); }" means the same.
script     : effect EOF | effectBody EOF ;
effect     : 'effect' STRING? '{' effectBody '}' ;
effectBody : trigger? chain ';'? limit? ;

// "You can only use this effect once per turn" (per card name; the game's use-limit class 5, docs/EffectSystem.md section 20).
limit      : 'once_per_turn' ';'? ;

// When the effect happens, for a MONSTER effect. No trigger = a Spell (activated from the hand / field). A card that lists no trigger gets the
// behaviour of a vanilla Normal Spell; with a trigger it borrows a vanilla monster that has that trigger and that action (trigger_sources.json).
trigger    : 'on' triggerName ':' ;
triggerName : 'normal_summoned' | 'special_summoned' | 'summoned' | 'normal_or_special_summoned' | 'flip'
            | 'destroyed_by_battle' | 'sent_to_grave' | 'sent_from_field_to_grave' ;

// One action, or several joined with 'then' (run in order, each after the previous resolved).
chain      : action ('then' action)* ;

action     : drawAction | searchAction | reviveAction | destroyAction | gainAction | burnAction | sendAction ;

drawAction    : 'draw' '(' NUMBER ')' ;
searchAction  : 'search' '(' 'deck' (',' selector)? ')' ;
reviveAction  : 'revive' '(' graveyard (',' selector)? ')' ;
destroyAction : 'destroy' '(' scope (',' side)? (',' selector)? ')' ;   // 'all' matching cards, or 'target' 1 chosen card
scope      : 'all' | 'target' ;
sendAction    : 'send_to_grave' '(' 'deck' (',' selector)? ')' ;   // send 1 card from your Deck to the Graveyard
gainAction    : 'gain_lp' '(' NUMBER ')' ;     // you gain N Life Points
burnAction    : 'burn' '(' NUMBER ')' ;        // inflict N damage to your opponent

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
