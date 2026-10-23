#pragma once

// "Connect to Server" on the main menu's Multiplayer page (Yu-Gi-Oh-RIX). It opens the Online Server page:
//   Connect / Disconnect     Live::Connect / Live::Disconnect (ServerUrl from Config.ini)
//   <status>                 "Connected to ygo-mp as Yugi"; its description is the server's message / the last notice / the error
//   <ban list>               the list the server gave us (applied while connected)
// The page refreshes itself every frame from Live::Version, so it follows the connect thread without blocking the game.
namespace ServerMenu
{
    // Adds the button (needs Yu-Gi-Oh-RIX; the manifest lists it in "requires"). Call from DllMain, before the menu is built.
    void Setup();
}
