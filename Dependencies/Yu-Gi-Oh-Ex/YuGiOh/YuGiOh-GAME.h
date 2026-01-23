#include <string>

namespace YGO
{
    namespace GAME
    {
        struct DeckListItem
        {
            wchar_t Name[33];
            short NumberOfCardsInMainDeck;
            short NumberOfCardsInExtraDeck;
            short NumberOfCardsInSideDeck;
            short CardsInMainDeck[60];
            char _pad[0x130 - 120];
        };

        inline bool& bIsGameContentLoaded = *reinterpret_cast<bool*>(0x1427D0560);

        inline auto Get_DeckTemplateAtIndex = reinterpret_cast<DeckListItem * (__fastcall*)(int Index)>(0x14076E8C0);
        inline auto Get_RandomKonamiIdFromCards = reinterpret_cast<short(*)()>(0x1407BCD70);
    }
}
