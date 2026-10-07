#pragma once

// The deck editor's Related cards panel, from Yu-Gi-Oh-Ex/relatedcards.json (written by WolfEx's Related cards tab, docs/RelatedCards.md):
//
//   { "tags":  [ { "id": 1802, "group": "name", "key": "MyArchetype", "text": { "E": "Related to: My Archetype" } },
//                { "id": 1803, "group": "affects", "conditions": [ { "type": "ATK", "op": "<=", "value": 1000 } ] } ],
//     "cards": [ { "card": 15300, "add": [ { "card": 4007, "tag": 1802 } ] },
//                { "card": 4041, "remove": [ { "card": 4007, "tag": 12 } ] } ] }
//
// "tags": a new tag (id past the game's 0-1801) or a changed game tag, whole. "cards": per Konami id (custom cards too), related cards added
// to or removed from the game's list; each unit is (related card, tag that explains why). The game reads bin/tagdata.bin by internal id, so
// custom cards had no list; the hooks answer by Konami id instead: YGO::CARDS::Get_RelatedCardCount (0x14076D640), Get_RelatedCardList
// (0x14076D690) and Get_TagInfoRecord (0x14076DF60), all MS Detours.
// A custom card's own "related": [ { "card": 4007, "tag": 12 } ] in cards.json (Card.cpp) is its whole list, over relatedcards.json's.
namespace Related
{
    // Reads relatedcards.json (again) and builds the merged lists. Call after each card setup (the game's tables are reloaded then).
    void Load();

    // Attaches the three hooks; call inside the plugin's Detours transaction.
    void Attach();
}
