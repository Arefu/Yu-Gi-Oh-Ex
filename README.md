![YuGiOh Legacy of the Duelist Link Evolution](https://cdn.ygorganization.com/2020/02/Yugioh-Legacy-of-the-Duelist-Link-Evolution-Siliconera-2-800x400-1.jpg)

# Wolf 2 - Electric Boogaloo
Designed and tested with the latest build of [Yu-Gi-Oh Legacy of the Duelist Link Evolution](https://store.steampowered.com/app/1150640/YuGiOh_Legacy_of_the_Duelist__Link_Evolution/) on Steam. These tools have **not** been tested for the Nintendo Switch [version](https://www.nintendo.com/us/store/products/yu-gi-oh-legacy-of-the-duelist-link-evolution-switch/). They likely never will be since all of my effort is purely into the Window's Executable for the game. You might be lucky to transfer the knowledge across but don't expect everything to work, especially not the various plugins used by `Yu-Gi-Oh_Launcher`.




## Getting Started

You should always be using the latest commit that I have, issues are very frequent and there is no real version control of checks to ensure everything is still working. You will need .NET8 and .NET9 and VC++2012 to Latest installed.


* WolfX - Is my attempt at an edit hub where you can interact with the game's proprietary file formats and edit them.
* Yami-Yugi - This is just a simple command line program to parse and extract YGO_DATA.dat.

There is also Yu-Gi-Oh_Launcher which is a pathway to injecting my own code into the game, there are currently a few patches that I have, as well as a speedhack for the game to help make it feel a bit quicker to play.

I would suggest checking the Project's [Wiki Page](https://github.com/Arefu/Yu-Gi-Oh-Ex/wiki), as almost all of what I know about the game is provided there which some of the information will be useful in either using these tools, or creating your own.

## Features 

Hey you, I see you looking at this page wondering what on Earth you're doing being dragged here by no doubt some random guy on a Discord saying "Hey bro, trust me it's good". Well I am sure it is.
This project has a few nifty features, to save time, and brain power remembering them all, here are some importat ones worth noting

1. **WolfX** - This is the main tool that I have been working on, it is a GUI that allows you to interact with the game's proprietary file formats, and edit them. It is still in development, but it is the most feature rich tool that I have created so far.
2. **Yami-Yugi** - This is a simple command line program that can parse and extract the YGO_DATA.dat file that the game uses. It is a very simple tool, but it is very useful for extracting the game's data.
3. **Yu-Gi-Oh_Launcher** - This is a tool that I have created to inject my own code into the game.It has a few patches that I have created, such as a speedhack for the game to help make it feel a bit quicker to play.


## How To Install and Use WolfX (And what is a Yu-Gi-Oh-Ex?)

1. Download the latest release from the GitHub page.
2. You will need to extract the files to a folder on your computer, I'd suggest the same place the game is installed.
3. Launch "Yu-Gi-Oh_Loader.exe" to start the game with my patches, and to see if it works. If not check out the [Wiki](https://github.com/Arefu/Yu-Gi-Oh-Ex/wiki) for what you can do.

Yu-Gi-Oh-Ex is just a little play on Microsoft's convention of putting "Ex" at the end of their functions when wanting to extend their implementation.
I'd be more inclined to call it WolfX however when just talking about the suite of tools that I have created.

If you have a Nintendo Switch, I'm sorry, you're currently out of luck for the majority of things.
If I ever bother to pick up my Swithc more I'll see if anything is possible, but currently you're limited to DAT/TOC modifications.
Even if I can get the core suite of tools compiled for Switch, I am not aware of anyway to inject my own code into a running process for switch like Detours does, so you're out of luck there.