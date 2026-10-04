# Menu files (codeless buttons)

Every `*.json` in `<game folder>\Yu-Gi-Oh-Ex\menus\` is read by the Yu-Gi-Oh-RIX plugin when the game starts. Nothing is compiled and no
game file is changed. The WolfEx **Menus** tab edits `menus.json` in that folder; any other file there is read too (files load in name order).
Mistakes are reported in the game's console (`ERROR ... [Yu-Gi-Oh-RIX] ...`) and only that button is skipped.

```json
{
  "buttons": [
    {
      "key": "mymod.credits",
      "menu": "main",
      "page": "main",
      "label": "Credits",
      "description": "Jump straight to the credits.",
      "look": "helpAndOptions",
      "action": { "goto": "credits" }
    }
  ],
  "edit": [
    { "item": "quitGame", "label": "Exit to Desktop", "description": "Close the game.", "hidden": false }
  ]
}
```

## Buttons

| Field | Meaning |
| --- | --- |
| `key` | A name for you (not used by the game yet). |
| `menu` | `main` (default) or `options`. |
| `page` | Main menu only: `main` (default), `singlePlayer`, `multiplayer`. |
| `label` | The text on the button (required). |
| `description` | Shown while the button is highlighted. |
| `look` | Whose picture the button borrows. Main menu: `singlePlayerMenu soloDuel duelistChallenge multiplayerMenu multiplayerA multiplayerB leaderboard battlePack deckEditor cardShop helpAndOptions tutorials quitGame`. Options: `howToPlay controllerSettings settings videoSettings credits`. |
| `action` | What it does when pressed: one step, or a list of steps. |

Buttons are listed in the order they appear in the files, after the game's own.

## Actions

| Step | Meaning |
| --- | --- |
| `{ "goto": "credits" }` | Open a screen: `title signIn mainMenu loading gameBegin helpAndOptions settings videoSettings credits controllerSettings howToPlay statistics voices pauseMenu duelistChallenge campaignDialog campaignSelectDeck tutorialList deckEditor swapCards matchResult gameResult cardShop battlePack battlePackDraft battlePackEdit playerMatch liveSetting liveSession liveLobby leaderboard inviteLanding safetyZone duelSelect selectRung scoreReview` (or the screen's number). |
| `{ "press": "cardShop" }` | Press one of the game's main menu buttons (same names as `look`). |
| `{ "quit": true }` | Quit the game (what the game's own Quit button does). |
| `{ "call": "funky.toggleTools" }` | Run an action a plugin registered with `RIX_RegisterAction`. Yu-Gi-Oh-Funky registers `funky.toggleTools`. |
| `{ "page": "myPage" }` | Open a page made on the WolfEx **Pages** tab (`Yu-Gi-Oh-Ex\pages\myPage.json`, see [PageDesigner.md](PageDesigner.md)). |

Several steps: `"action": [ { "goto": "credits" }, { "call": "funky.toggleTools" } ]` (the WolfEx tab keeps these but only edits single steps).

## Edits to the game's own main menu buttons

`item` is one of the `look` names. `label` and `description` replace the text; `hidden: true` removes the button from its page.
Everything is applied when the game starts.

## Not supported yet

Placing a button "after" another one, conditions (`visibleIf`), several languages, the pause and live menus, editing the options menu's own buttons.
