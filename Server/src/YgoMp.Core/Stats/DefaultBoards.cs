namespace YgoMp.Core.Stats;

// Written to boards.json on first run; hosts edit that file to add/remove boards (any stat key StatExtractor emits).
public static class DefaultBoards
{
    public static List<BoardDefinition> Create() =>
    [
        new() { Id = "duels-won", Title = "Duels won", Stat = "duels:won" },
        new() { Id = "duels-played", Title = "Duels played", Stat = "duels:played" },
        new() { Id = "cards-played", Title = "Total cards played", Stat = "played:any" },
        new() { Id = "monsters-summoned", Title = "Monsters summoned", Stat = "summoned:any" },
        new() { Id = "normal-summons", Title = "Normal Summons", Stat = "summon:any" },
        new() { Id = "special-summons", Title = "Special Summons", Stat = "special_summon:any" },
        new() { Id = "extra-deck", Title = "Summons from the Extra Deck", Stat = "special_summon:from=extra" },
        new() { Id = "graveyard", Title = "Summons from the Graveyard", Stat = "special_summon:from=grave" },
        new() { Id = "sets", Title = "Cards Set", Stat = "set:any" },
        new() { Id = "draws", Title = "Cards drawn", Stat = "draw:any" },
        new() { Id = "spells", Title = "Spells activated", Stat = "activate:kind=spell" },
        new() { Id = "traps", Title = "Traps activated", Stat = "activate:kind=trap" },
        new() { Id = "fusion", Title = "Fusion summons", Stat = "special_summon:frame=fusion" },
        new() { Id = "synchro", Title = "Synchro summons", Stat = "special_summon:frame=synchro" },
        new() { Id = "xyz", Title = "Xyz summons", Stat = "special_summon:frame=xyz" },
        new() { Id = "link", Title = "Link summons", Stat = "special_summon:frame=link" },
        new() { Id = "ritual", Title = "Ritual summons", Stat = "special_summon:frame=ritual" },
        new() { Id = "dragons", Title = "Dragons summoned", Stat = "summoned:race=dragon" },
        new() { Id = "damage", Title = "Total damage dealt", Stat = "damage:total" },
        new() { Id = "biggest-hit", Title = "Biggest single hit", Stat = "damage:max" },
        new() { Id = "longest-chain", Title = "Longest chain", Stat = "chain:longest" },
    ];
}
