# Master Duel step functions the game does not have

Made by `Tools/Scripts/md_function_catalogue.py` from duel.dll (build 80460 : 20260629) and `md_rows.json` (Tools/Scripts/md_effect_rows.py).
These are the slot functions of Master Duel's effect rows (docs/EffectSystem.md: 48-byte rows, five slots) that no card the two games share
pairs with a game function: the mechanics Konami added after the game. Each is listed with the post-game cards whose rows use it (the
Cards mod's cards), where (table and slot), its size, and the card ids its own code compares against.

**521 functions: 48 generic** (two or more cards, no card id in its own code - candidates for BOB / BAB parts),
**89 card-specific** (compare card ids: a part only after IDA shows the path for an id they do not list),
**384 single** (one card uses it: that card's own machine).

Heuristic: only the function's own bytes are read (a callee can still branch on ids); "compares" counts cmp / mov immediates that are a
card id Master Duel has. Until each one is traced in IDA, BOB / BAB reach every one of these through as(<card>) - the card plays as the
source under Master Duel's engine (Core_EngineLend), so all of them, card-specific ones included, already run for a custom card.

| function | verdict | cards | size | where | ids its code compares | cards using it (first 6) |
|---|---|---|---|---|---|---|
| `0x18023ef70` | card-specific | 69 | 145 | handGrave condition (slot 2); ignition condition (slot 2); monster condition (slot 2); pendulum condition (slot 2); spellTrap condition (slot 2) | 14480 Gizmek Orochi, the Serpentron Sky Slasher | Crystal Beast Rainbow Dragon 14955, Crimson Resonator 15003, Machina Redeployment 15092, Hellebore the Rikka Fairy 15116, Pride of the Plunder Patroll 15325, Appliancer Kappa Scale 15397 ... |
| `0x180477b50` | generic | 60 | 0 | handGrave prompt / pick (slot 4); ignition prompt / pick (slot 4); monster prompt / pick (slot 4); pendulum prompt / pick (slot 4); spellTrap prompt / pick (slot 4) |  | Soulicer Crystal 5088, D/D Evil 14739, Ghost Mourner & Moonlit Chill 15022, Gatchiri @Ignister 15135, D/D Dog 15181, Dual Avatar Feet - Armored Un-Gyo 15512 ... |
| `0x1801e6eb0` | generic | 19 | 67 | handGrave target (slot 1); ignition target (slot 1); monster target (slot 1); pendulum target (slot 1) |  | Dogmatika Fleurdelis, the Knighted 15242, Protecting Spirit Loagaeth 16214, Heroic Champion - Claivesolish 17155, Worldsea Dragon Zealantis 17805, Ghoti Fury 18041, Baby Spider 18439 ... |
| `0x180402780` | card-specific | 18 | 313 | ignition action (slot 0); monster action (slot 0) | 19977, 20049, 20991, 22532 Fleeting Phantom Mask Master, 22550 Kewl Tune Crackle ... | Dogmatika Theo, the Iron Punch 15240, Arc Rebellion Xyz Dragon 15520, Epsilon The Magnet Warrior 16825, Gold Pride - Star Leon 18458, Combat Wheel 19028, Vagnawa the Moon-Eating Dragon 19498 ... |
| `0x18020eb80` | generic | 17 | 77 | handGrave cost (slot 3); ignition cost (slot 3) |  | Valiant Shark Lancer 15030, Bystial Magnamhut 17762, Bystial Saronir 17763, Bystial Druiswurm 17764, Epurrely Beauty 18005, Epurrely Plump 18006 ... |
| `0x180242510` | generic | 16 | 72 | handGrave condition (slot 2); ignition condition (slot 2); monster condition (slot 2); spellTrap condition (slot 2) |  | Plunder Patroll Booty 15081, Jinzo - Layered 15464, Evil★Twin Present 15634, Primitive Planet Reichphobia 17169, Vaylantz World - Konig Wissen 17359, Libromancer Prevented 17400 ... |
| `0x1802663c0` | card-specific | 16 | 525 | handGrave condition (slot 2); ignition condition (slot 2); monster condition (slot 2); spellTrap condition (slot 2) | 8993 Pot of Duality, 9381 Gishki Noellia, 9968 Ma'at, 10357 Spellbook Library of the Heliosphere, 13267 Akashic Magician ... | Votis 7506, Flourishing Frolic 15058, Adamancipator Seeker 15097, Adamancipator Researcher 15098, Adamancipator Analyzer 15099, Adamancipator Risen - Leonite 15103 ... |
| `0x18040cad0` | card-specific | 16 | 3300 | handGrave action (slot 0); ignition action (slot 0); monster action (slot 0); spellTrap action (slot 0) | 15469 How Did Dai Get Here?, 15516 Infernity Doom Archfiend, 15576 Samsara Dragon, 15588 Dragunity Remus, 15768 Dream Mirror Recap ... | How Did Dai Get Here? 15469, Dream Mirror Recap 15768, Water Enchantress of the Temple 16749, Horned Saurus 19024, Fire King Sanctuary 19400, Fallen of the Tistina 19416 ... |
| `0x18025ca50` | card-specific | 14 | 157 | handGrave condition (slot 2); ignition condition (slot 2); monster condition (slot 2); spellTrap condition (slot 2) | 6634 Assault Lion, 7793 Guivre, 10440 Constellar Sombre, 10441 Evilswarm Kerykeion, 11204 Escalation of the Monarchs ... | Cyberdark Realm 16298, Floowandereeze and the Magnificent Map 16544, Beetrooper Armor Horn 16631, The Weather Forecast 17174, Wheel Synchron 18788, Horned Saurus 19024 ... |
| `0x18023ebc0` | card-specific | 12 | 206 | handGrave condition (slot 2); ignition condition (slot 2); monster condition (slot 2); spellTrap condition (slot 2) | 12289 Blackwing - Decay the Ill Wind | Red Supernova Dragon 14957, Battle of Sleeping Spirits 17627, Primite Drillbeam 20536, Wonko, Noble Knight of the Forest 21063, Cupsy☆Yummy 21365, Artmage Varnish -Alteration- 21469 ... |
| `0x1803e0de0` | card-specific | 11 | 151 | handGrave action (slot 0); ignition action (slot 0); monster action (slot 0); pendulum action (slot 0) | 15045 Stairs of Mail, 17966, 20131, 20648 | Penguin Brave 15518, Digital Bug Registrider 15714, Symphonic Warrior DJJ 17133, Dreaming Reality of Nemleria, Realized 18802, Vanquish Soul Jiaolong 19160, Battleguard Echoes 20058 ... |
| `0x180395fb0` | generic | 9 | 52 | handGrave action (slot 0); ignition action (slot 0); spellTrap action (slot 0) |  | Plunder Patrollship Brann 15077, Plunder Patrollship Moerk 15078, Protecting Spirit Loagaeth 16214, The Most Distant, Deepest Depths 17755, Aromalilith Rosemary 19505, Psychic Blaster Mk-II 21451 ... |
| `0x180213b30` | card-specific | 9 | 1728 | handGrave cost (slot 3); ignition cost (slot 3) | 5099 Soul Exchange, 13686 Lair of Darkness, 15516 Infernity Doom Archfiend, 15624 Live☆Twin Ki-sikil, 16106 Ursarctic Departure ... | Ursarctic Mikpolar 16097, Ursarctic Miktanus 16098, Ursarctic Mikbilis 16099, Ursarctic Megapolar 16100, Ursarctic Megatanus 16101, Ursarctic Megabilis 16102 ... |
| `0x1803f8db0` | generic | 9 | 40 | ignition action (slot 0); monster action (slot 0); spellTrap action (slot 0) |  | Amaze Attraction Rapid Racing 16035, Kuribee 16398, Despian Luluwalilith 18505, Baelgrill de Nouvelles 18744, E Stranger Big Bang 19484, Primite Imperial Dragon 20489 ... |
| `0x1803ffb70` | card-specific | 8 | 2464 | handGrave action (slot 0); ignition action (slot 0); spellTrap action (slot 0) | 4608 Penguin Soldier, 9656 Number 17: Leviathan Dragon, 9875 Kurivolt, 10126 Temtempo the Percussion Djinn, 10726 Overlay Capture ... | Torpedo Takedown 15049, Seventh Eternity 17045, Overlay Network 17254, Tally-ho! Springans 18199, Kashtira Big Bang 18220, Fallen of Argyros 18472 ... |
| `0x1803e4450` | card-specific | 8 | 2341 | handGrave action (slot 0); ignition action (slot 0); monster action (slot 0); spellTrap action (slot 0) | 13448 Dai Dance, 15385 Numeron Calling, 15428, 15516 Infernity Doom Archfiend, 16436 A.I.'s Show ... | Numeron Calling 15385, Hi-Speedroid Clear Wing Rider 16380, A.I.'s Show 16436, Soul of the Supreme King 17261, Supreme King Z-ARC - Synchro Universe 19722, Gem-Knight Master Diamond Dispersion 20931 ... |
| `0x180406570` | card-specific | 8 | 73 | handGrave action (slot 0); ignition action (slot 0); monster action (slot 0); spellTrap action (slot 0) | 19497 Enigmaster Packbit | Psychic Megacyber 15346, Sinful Spoils of Subversion - Snake-Eye 18847, Snake-Eyes Flamberge Dragon 19152, Startling Stare of the Snake-Eyes 19217, Centur-Ion Emeth VI 19374, Snake-Eyes Doomed Dragon 20772 ... |
| `0x180269320` | card-specific | 8 | 533 | ignition condition (slot 2); monster condition (slot 2) | 12993 Unending Nightmare, 14116 T.G. Star Guardian, 15149 Mayakashi Mayhem, 15997 Garden Rose Flora, 16267 Beast King Unleashed ... | Sun God Unification 15336, Lil-la Rap 18321, Gate Guardian of Water and Thunder 18328, Bomber Place 21171, Eternal Sunshine 21208, Chef de Nouvelles 21434 ... |
| `0x1801e4df0` | card-specific | 8 | 1436 | handGrave target (slot 1); ignition target (slot 1); spellTrap target (slot 1) | 16388 Synchro Dilemma, 16867 Clear New World, 17120 Visas Starfrost, 17730 Crystal Aegis, 18475 Mannadium Riumheart ... | Synchro Dilemma 16388, Clear New World 16867, Visas Starfrost 17120, Crystal Aegis 17730, Mannadium Riumheart 18475, Unchained Soul of Sharvara 18806 ... |
| `0x1804170e0` | card-specific | 8 | 970 | handGrave action (slot 0); ignition action (slot 0); spellTrap action (slot 0) | 6649 Armament Reincarnation, 16748 Rite of Aramesir, 16752 Fateful Adventure, 16944 Beetrooper Descent, 17178 Parasomnia Pillow ... | Armament Reincarnation 6649, Rite of Aramesir 16748, Beetrooper Descent 16944, Parasomnia Pillow 17178, Blackwing - Sudri the Phantom Glimmer 17760, Curse of Aramatir 17822 ... |
| `0x18020f820` | card-specific | 8 | 672 | handGrave cost (slot 3); ignition cost (slot 3); spellTrap cost (slot 3) | 12454 Frightfur Sanctuary, 15516 Infernity Doom Archfiend | Mirrorjade the Iceblade Dragon 17069, Brilliant Rose 17423, Ultimate Slayer 17469, Armored Shark 20479, Dark Flare Swordsman 21430, Lunalight Liger Dancer 21441 ... |
| `0x180428c60` | generic | 8 | 861 | handGrave action (slot 0); ignition action (slot 0); monster action (slot 0); spellTrap action (slot 0) |  | Dogmatika Alba Zoa 18176, Vaalmonica Scelta 19386, Vaalmonica Versare 19387, Vaalmonica Intonare 19388, Mimighoul Fork 20719, Hallo, the Spirit of Tricks 20723 ... |
| `0x180287a00` | card-specific | 7 | 327 | ignition condition (slot 2); monster condition (slot 2) | 23311 Angelechy Opening to e4 | Horned Saurus 19024, Multi-Universe 20062, Tenyinfinity 20799, Yummy★Snatchy 21371, Spring 21484, Summer 23013 ... |
| `0x180438d90` | card-specific | 7 | 1664 | handGrave action (slot 0) | 4064 Kuriboh, 4608 Penguin Soldier, 15516 Infernity Doom Archfiend, 20221 Gimmick Puppet Fantasix Machinix, 20422 Dipsea Fiend | Gimmick Puppet Fantasix Machinix 20221, Galaxy-Eyes Tachyon Primal 20277, Dipsea Fiend 20422, Mimighoul Armor 20714, Mimighoul Fairy 20715, Mimighoul Slime 20716 ... |
| `0x1803ef8b0` | card-specific | 6 | 522 | handGrave action (slot 0); monster action (slot 0) | 12240 Trap Hole of Spikes, 15842 Zolga the Prophet, 16238 Dispatchparazzi, 20064 Aiza the Dragoness of Deranged Devotion, 21753 Matador Archfiend | Appliancer Laundry Dragon 15399, Zolga the Prophet 15842, Dispatchparazzi 16238, Wake Up Your Elemental HERO 17247, Aiza the Dragoness of Deranged Devotion 20064, Matador Archfiend 21753 |
| `0x1801b1280` | generic | 6 | 202 | handGrave target (slot 1); ignition target (slot 1); spellTrap target (slot 1) |  | Cyber Energy Shock 15347, Super All In! 15388, D/D/D Deviser King Deus Machinex 16849, Unchained Malevolent Magistrate Kamura 23172, Dark Time Wizard 23374, Evil Lord - Zorc 23486 |
| `0x18046c910` | generic | 6 | 39 | ignition action (slot 0); spellTrap action (slot 0) |  | Darklord Uprising 15309, Shaddoll Schism 15310, Ultimate Fusion 16856, Double Fusion 20949, Artmage Masterwork -Succession- 21467, Hunting Horn 22068 |
| `0x1801e3f60` | card-specific | 6 | 960 | handGrave target (slot 1); ignition target (slot 1) | 15492 Virtual World Kirin - Lili, 19025 Clorless, Chaos King of Dark World, 20259 Three in One | Virtual World Mai-Hime - Lulu 15489, Virtual World Roshi - Laolao 15490, Virtual World Xiezhi - Jiji 15491, Virtual World Kirin - Lili 15492, Clorless, Chaos King of Dark World 19025, Three in One 20259 |
| `0x1802137f0` | generic | 6 | 47 | ignition cost (slot 3) |  | Amaze Attraction Cyclo-Coaster 16032, Amaze Attraction Wonder Wheel 16033, Amaze Attraction Rapid Racing 16035, Amaze Attraction Horror House 16036, Amaze Attraction Viking Vortex 16265, Amaze Attraction Thrill Train 17479 |
| `0x180210400` | card-specific | 6 | 1435 | handGrave cost (slot 3); ignition cost (slot 3); spellTrap cost (slot 3) | 13477 Ojamassimilation, 16636 Astral Kuriboh, 20187 Gimmick Puppet Rouge Doll, 20461, 22133 First Penguin ... | Astral Kuriboh 16636, Seventh Tachyon 20282, Gagaga Ganbara Knight 21413, Junk Meister 22310, Darkness Resonator 22510, Plundered Power Patron Plane - Vidolia 22566 |
| `0x180439410` | card-specific | 6 | 2676 | monster action (slot 0); spellTrap action (slot 0) | 15520 Arc Rebellion Xyz Dragon, 20232 Millennium Ankh, 21432 Kaibaman the Legend, 23004 Prohibitive Power Patron Purview - Vilaea, 23386 Spell Shattering Sword ... | Millennium Ankh 20232, Kaibaman the Legend 21432, Prohibitive Power Patron Purview - Vilaea 23004, Spell Shattering Sword 23386, Zenet Prophet - Nefertari 23532, Zenet Prophet - Amenhotep 23533 |
| `0x1804075d0` | card-specific | 5 | 980 | handGrave action (slot 0); ignition action (slot 0); monster action (slot 0) | 4608 Penguin Soldier, 15377 Malefic Paradigm Dragon, 15636 Drytron Beta Rastaban, 15879 Flint Cragger, 19534 Terrors of the Afterroot | Malefic Paradigm Dragon 15377, Virtual World Kyubi - Shenshen 15515, Rilliona, the Magistus of Verre 15614, Derk, Longing for the Blue Skies 22129, Witchcrafter Pupils 23166 |
| `0x180250470` | generic | 5 | 220 | ignition condition (slot 2); monster condition (slot 2) |  | Raging Storm Dragon - Beaufort IX 15952, Dark Honest 15983, Beetrooper Light Flapper 16941, Doom Kraken 17048, Vulcarrion the Rotting Phoenix 21046 |
| `0x18026bbd0` | card-specific | 5 | 358 | ignition condition (slot 2); monster condition (slot 2) | 12789 Zoodiac Boarbow, 18007 Expurrely Happiness, 18925 Number C79: Battlin' Boxer General Kaiser, 20404 Antidote Nurse | Springans Ship - Exblowrer 15737, Expurrely Happiness 18007, Varudras, the Final Bringer of the End Times 19886, Antidote Nurse 20404, Bazoo the Soul-Devourer 21240 |
| `0x1804897f0` | generic | 5 | 119 | spellTrap prompt / pick (slot 4) |  | Life Shaver 5554, Virtual World Gate - Xuanwu 15766, Forge a New Future 17265, Arbitration of White 19750, Anti-GMX Final Experiment 22938 |
| `0x18017e930` | generic | 5 | 123 | spellTrap target (slot 1) |  | Life Shaver 5554, Virtual World Gate - Xuanwu 15766, Forge a New Future 17265, Arbitration of White 19750, Anti-GMX Final Experiment 22938 |
| `0x1801ed6f0` | generic | 5 | 0 | ignition cost (slot 3) |  | Ogdoabyss, the Ogdoadic Overlord 16079, Baronne de Fleur 16386, First of the Dragonlords 21447, Charmer Quartet in Bloom 22559, Ars Magna "Citrinitas" 23383 |
| `0x1801e3c60` | card-specific | 5 | 757 | handGrave target (slot 1); ignition target (slot 1); monster target (slot 1); pendulum target (slot 1); spellTrap target (slot 1) | 14961 D/D/D Vice King Requiem, 16260 Springans Interluder, 19172 Odd-Eyes Arcray Dragon, 23309 Angelechy Problem | Springans Interluder 16260, Odd-Eyes Arcray Dragon 19172, Angelechy Problem 23309, Angelechy Strategy Score 23524, Angelechy Brilliant Move 23525 |
| `0x180275700` | card-specific | 5 | 178 | monster condition (slot 2) | 18003 Purrely, 18481 Purrelyly | Exosister Elis 16736, Exosister Stella 16737, Exosister Irene 16738, Exosister Sophia 16739, Exosister Martha 17427 |
| `0x180428250` | card-specific | 5 | 360 | handGrave action (slot 0); ignition action (slot 0); monster action (slot 0); spellTrap action (slot 0) | 17142 Reverse Jar, 18034 Destructive Daruma Karma Cannon, 18277, 19086 Demigod of the Tistina, 19087 Crystal God Tistina ... | Reverse Jar 17142, Destructive Daruma Karma Cannon 18034, Demigod of the Tistina 19086, Crystal God Tistina 19087, Zenetnakht Sphinx 23534 |
| `0x180422910` | card-specific | 5 | 997 | monster action (slot 0); spellTrap action (slot 0) | 4608 Penguin Soldier, 15804, 17627 Battle of Sleeping Spirits, 18322 Humongous Hive Hegemon - Zexstagger, 22692 Dark Magical Curtain ... | Battle of Sleeping Spirits 17627, Humongous Hive Hegemon - Zexstagger 18322, Dark Magical Curtain 22692, Underworld Circle 22708, Destined Duel 23531 |
| `0x18041e3f0` | card-specific | 5 | 833 | ignition action (slot 0); spellTrap action (slot 0) | 13282 Burning Bamboo Sword, 18853 Million-Century Ice Prison, 20102, 21688, 22839 | Icejade Gymir Aegirine 18183, Purrely Sleepy Memory 18524, Primite Roar 20535, Primite Howl 20552, Ryzeal Mass Driver 21212 |
| `0x1801e5cc0` | card-specific | 5 | 755 | handGrave target (slot 1); ignition target (slot 1); monster target (slot 1) | 6829 White Knight Lord, 15388 Super All In!, 17400 Libromancer Prevented, 18228 Courageous Crimson Chevalier Bradamante, 18744 Baelgrill de Nouvelles ... | White Knight Lord 6829, Courageous Crimson Chevalier Bradamante 18228, Chef's Special Recipe 18750, Number C79: Battlin' Boxer General Kaiser 18925, Junora the Power Patron of Tuning 22142 |
| `0x180477b80` | generic | 5 | 0 | handGrave prompt / pick (slot 4); ignition prompt / pick (slot 4); spellTrap prompt / pick (slot 4) |  | Susurrus of the Sinful Spoils 20239, Dracotail Horn 21363, Lunalight Perfume Dancer 21442, Artmage Pact -Awakening- 21486, Dragunity Quirinus 22196 |
| `0x18035d320` | card-specific | 5 | 2744 | monster action (slot 0); pendulum action (slot 0) | 3734, 7623 Cup of Ace, 7624 Light Barrier, 11274 Magical Spring, 15802 ... | Arcana Force V - The Hierophant 20743, Arcana Force XIX - The Sun 20744, Tempura of Fortune - EBI 20767, Arcana Force EX - The Chaos Ruler 20771, Arcana Force XII - The Hangman 23143 |
| `0x180413580` | generic | 4 | 51 | monster action (slot 0); spellTrap action (slot 0) |  | Ecole de Zone 16415, Soul Binding Gate 16416, Merciless Scorpion of Serket 21036, Trap of the Poisonous Scorpion 21579 |
| `0x18037da60` | card-specific | 4 | 883 | ignition action (slot 0) | 8999 Barrier Wave, 9536 Chaos Infinity, 14805 Supermagic Sword of Raptinus, 16035 Amaze Attraction Rapid Racing, 17023 ... | Supermagic Sword of Raptinus 14805, Amaze Attraction Rapid Racing 16035, Amaze Attraction Thrill Train 17479, Asset Mountis 19413 |
| `0x1803f4f60` | card-specific | 4 | 5684 | handGrave action (slot 0); ignition action (slot 0); monster action (slot 0); spellTrap action (slot 0) | 9326 Symphonic Warrior Piaano, 14243 Salamangreat Beat Bison, 15008 Ancient Warriors - Deceptive Jia Wen, 15203, 15204 ... | Forbidden Droplet 15299, Predaplant Triantis 17131, Fiendsmith's Desirae 20215, Clown Crew Biancaviso 22524 |
| `0x180212e60` | card-specific | 4 | 1126 | ignition cost (slot 3) | 15512 Dual Avatar Feet - Armored Un-Gyo, 15516 Infernity Doom Archfiend, 15616 Aiwass, the Magistus Spell Spirit, 15624 Live☆Twin Ki-sikil, 16038 S-Force Chase ... | S-Force Rappa Chiyomaru 15702, S-Force Orrafist 15704, S-Force Lapcewell 16821, S-Force Nightcatcher 22989 |
| `0x1804013c0` | card-specific | 4 | 3885 | handGrave action (slot 0); ignition action (slot 0) | 15428, 15429, 15516 Infernity Doom Archfiend, 15576 Samsara Dragon, 15588 Dragunity Remus ... | Lavalval Salamander 15846, Prediction Princess Tarotreith 17793, Reeshaddoll Wendikurhu 22208, Enneacraft Reset 22574 |
| `0x18040ee10` | card-specific | 4 | 1177 | handGrave action (slot 0); monster action (slot 0); spellTrap action (slot 0) | 4608 Penguin Soldier, 18214 Triple Tactics Thrust, 19835 Veidos the Eruption Dragon of Extinction, 23158 Evil Box | Albion the Branded Dragon 15994, Triple Tactics Thrust 18214, Spirit of Yubel 19456, Tenpai Dragon Paidra 19857 |
| `0x180411f70` | generic | 4 | 272 | handGrave action (slot 0); monster action (slot 0); spellTrap action (slot 0) |  | Stardust Illumination 16240, Icejade Manifestation 18198, Jade Dragon Mech 22328, Cyberse Code Magician 22706 |
| `0x18041f3c0` | card-specific | 4 | 298 | handGrave action (slot 0) | 15516 Infernity Doom Archfiend | Labrynth Cooclock 17366, Samsara D Lotus 19458, Eidos the Underworld Monarch 21428, Unchained Ogre Shara 23170 |
| `0x180427f30` | generic | 4 | 169 | ignition action (slot 0) |  | Epurrely Happiness 18004, Epurrely Beauty 18005, Epurrely Plump 18006, Epurrely Noir 18831 |
| `0x180497200` | card-specific | 4 | 940 | ignition prompt / pick (slot 4); monster prompt / pick (slot 4); spellTrap prompt / pick (slot 4) | 15516 Infernity Doom Archfiend, 15576 Samsara Dragon, 15588 Dragunity Remus, 15608, 22983 Melffys' Joyful Surprise | Fire King Sky Burn 19401, Mementomictlan Tecuhtlica - Creation King 20216, Dark Rebellion Xyz Dragon, Four Heavenly Dragons 22730, Melffys' Joyful Surprise 22983 |
| `0x18024e8f0` | card-specific | 3 | 76 | ignition condition (slot 2); monster condition (slot 2) | 15388 Super All In!, 15392 Fire Flint Lady | Feedran, the Winds of Mischief 15072, Scareclaw Sclash 17185, Clear Rage Golem 20394 |
| `0x1801a2990` | generic | 3 | 242 | ignition target (slot 1); spellTrap target (slot 1) |  | Time Stream 15370, Starry Knight Astel 15872, Starry Knight Orbitael 16513 |
| `0x180286840` | generic | 3 | 0 | handGrave condition (slot 2); monster condition (slot 2) |  | Dogmatika Nation 15285, White Knight of Dogmatika 15993, Dogmatika Lawbringer 22086 |
| `0x18040a040` | card-specific | 3 | 425 | handGrave action (slot 0) | 22550 Kewl Tune Crackle | Banquet of Millions 15558, The Bystial Alba Los 17766, Kewl Tune Crackle 22550 |
| `0x1801e1360` | card-specific | 3 | 742 | ignition target (slot 1); monster target (slot 1) | 4469 Electric Snake, 15773 Myutant GB-88 | Myutant M-05 15568, Myutant ST-46 15569, Myutant GB-88 15773 |
| `0x1804100f0` | card-specific | 3 | 1069 | handGrave action (slot 0); ignition action (slot 0); spellTrap action (slot 0) | 16213 Antihuman Intelligence ME-PSY-YA, 16237 GranSolfachord Musecia, 20596 Ryu-Ge Rising, 20746 Speedroid Wing Synchron, 21070 D/D Scale Surveyor ... | Solfachord Elegance 16092, Antihuman Intelligence ME-PSY-YA 16213, GranSolfachord Musecia 16237 |
| `0x1801e4550` | card-specific | 3 | 433 | ignition target (slot 1); spellTrap target (slot 1) | 15839 Ultimate Divine-Beast, 16477 Arcana Triumph Joker, 20281 Tachyon Spiral of Destruction | Ultimate Divine-Beast 15839, Arcana Triumph Joker 16477, Tachyon Spiral of Destruction 20281 |
| `0x1802785f0` | card-specific | 3 | 608 | monster condition (slot 2); spellTrap condition (slot 2) | 13020 Pendulum Switch, 14204 D/D/D Abyss King Gilgamesh, 14982 Pendulum Transfer, 15467 Daidaratant the Ooze Giant, 16092 Solfachord Elegance ... | Pendulum Transfer 14982, D/D/D Deviser King Deus Machinex 16849, Vulmina, Statue of the Sacred Dragon 20420 |
| `0x180411d90` | card-specific | 3 | 477 | monster action (slot 0) | 16232 Gunkan Suship Ikura-class Dreadnought, 16534 Gunkan Suship Uni-class Super-Dreadnought | Gunkan Suship Ikura-class Dreadnought 16232, Gunkan Suship Uni-class Super-Dreadnought 16534, Gunkan Suship Shirauo-class Carrier 16535 |
| `0x18028a080` | generic | 3 | 59 | monster condition (slot 2) |  | Gunkan Suship Ikura-class Dreadnought 16232, Gunkan Suship Uni-class Super-Dreadnought 16534, Gunkan Suship Shirauo-class Carrier 16535 |
| `0x18040b790` | card-specific | 3 | 1502 | ignition action (slot 0); spellTrap action (slot 0) | 4608 Penguin Soldier, 15616 Aiwass, the Magistus Spell Spirit, 15639 Drytron Delta Altais, 18003 Purrely, 18859 The Continuing Epic of Charles ... | Floowandereeze and the Magnificent Map 16544, Purrely 18003, The Continuing Epic of Charles 18859 |
| `0x180285f20` | generic | 3 | 359 | handGrave condition (slot 2); monster condition (slot 2); spellTrap condition (slot 2) |  | Blue-Eyes Jet Dragon 16809, Chimera Fusion 18839, Ashtra's Howl 23396 |
| `0x18041ec20` | generic | 3 | 73 | ignition action (slot 0) |  | Mamonaka the Vaylantz United 17355, Vaylantz World - Konig Wissen 17359, Vaylantz Wave - Master Phase 18861 |
| `0x1801e57a0` | card-specific | 3 | 415 | handGrave target (slot 1); monster target (slot 1) | 17351 Vaylantz Buster Baron, 18668 Pendulum Pendant, 21070 D/D Scale Surveyor | Vaylantz Buster Baron 17351, Pendulum Pendant 18668, D/D Scale Surveyor 21070 |
| `0x1801e5a40` | card-specific | 3 | 636 | ignition target (slot 1); spellTrap target (slot 1) | 9328 Yaksha | Land Flipping 18215, Centur-Ion Phalanx 19379, Angelechy Castellan 23522 |
| `0x180429fa0` | card-specific | 3 | 442 | ignition action (slot 0); spellTrap action (slot 0) | 22712 Malefic Paradigm Shift, 23156 Funny Dark Rabbit | Photon Timestop 18216, Malefic Paradigm Shift 22712, Funny Dark Rabbit 23156 |
| `0x1803f2a00` | card-specific | 3 | 1942 | ignition action (slot 0); monster action (slot 0) | 4608 Penguin Soldier, 14105 Cataclysmic Scorching Sunburner, 15516 Infernity Doom Archfiend, 16115, 16569 ... | Armed Neos 18426, Lycoris Lilyreaper 22069, Number 101: Silent Honor ARK - Soul Asylum 23502 |
| `0x180270e20` | generic | 3 | 0 | ignition condition (slot 2); monster condition (slot 2) |  | Full Armored Dark Knight Lancer 19183, Goblin Biker Big Gabonga 19502, Number S38: Titanic Galaxy 21066 |
| `0x18042cbb0` | generic | 3 | 48 | ignition action (slot 0); monster action (slot 0) |  | Lil-la Rap 18321, Deception of the Sinful Spoils 20531, Black Skull Dragon, the Archfiend of Unity 23348 |
| `0x180215bc0` | card-specific | 3 | 227 | ignition cost (slot 3) | 15516 Infernity Doom Archfiend | Sengenjin Wakes from a Millennium 20180, Golem that Guards the Millennium Treasures 20181, Shield of the Millennium Dynasty 20182 |
| `0x1802700c0` | generic | 3 | 30 | handGrave condition (slot 2) |  | Mulcharmy Purulia 20206, Mulcharmy Fuwalos 20500, Mulcharmy Meowls 20764 |
| `0x180438a80` | generic | 3 | 35 | handGrave action (slot 0) |  | Mulcharmy Purulia 20206, Mulcharmy Fuwalos 20500, Mulcharmy Meowls 20764 |
| `0x180410ff0` | card-specific | 3 | 561 | handGrave action (slot 0) | 4064 Kuriboh, 16197 Albion the Shrouded Dragon | Hecahands Ibtel 21931, Hecahands Yadel 21932, Black Rose Dragon/Assault Mode 22921 |
| `0x180445510` | generic | 3 | 349 | ignition action (slot 0) |  | Elfnote Lucina 22116, Elfnote Tinia 22117, Elfnote Fortuna 22118 |
| `0x180214db0` | card-specific | 3 | 1410 | handGrave cost (slot 3); ignition cost (slot 3) | 15516 Infernity Doom Archfiend, 22533 Fydraulis Harmonia, 23141 Ilios the Black Sun Dragon, 23355 Garbage Golem | Fydraulis Harmonia 22533, Ilios the Black Sun Dragon 23141, Garbage Golem 23355 |
| `0x180450640` | card-specific | 3 | 773 | handGrave action (slot 0) | 4064 Kuriboh, 23560 Mad Tempest Trirealm Rift Ploutonion | Sky Thunder Trirealm Rift Yomi 23558, Burial Summit Trirealm Rift Helheim 23559, Mad Tempest Trirealm Rift Ploutonion 23560 |
| `0x180241ff0` | card-specific | 2 | 141 | monster condition (slot 2); spellTrap condition (slot 2) | 15840 Golden-Eyes Idol, 16241 Majestic Absorption, 18228 Courageous Crimson Chevalier Bradamante, 18863 Mikanko Spiritwalk | Cross Over 14808, Nightmare-Eyes Restrict 22212 |
| `0x180405f10` | generic | 2 | 413 | handGrave action (slot 0); monster action (slot 0) |  | Dogmatika Nation 15285, Dogmatika Lawbringer 22086 |
| `0x1802e5d20` | generic | 2 | 78 | handGrave action (slot 0); ignition action (slot 0) |  | Dracoon Lamp 15269, Sunvine Gardna 15865 |
| `0x180408330` | card-specific | 2 | 702 | monster action (slot 0) | 15499 Awakening of the Possessed - Gagigobyte, 17976, 19271, 21088, 22035 ... | Awakening of the Possessed - Gagigobyte 15499, Number C104: Umbral Horror Masquerade Victory 23365 |
| `0x180277e50` | generic | 2 | 98 | ignition condition (slot 2) |  | Time Wizard of Tomorrow 15462, Diabolantis the Menacing Mantis 18452 |
| `0x180409250` | card-specific | 2 | 592 | spellTrap action (slot 0) | 7624 Light Barrier, 15543 Arcana Reading, 20791 Arcana Spread | Arcana Reading 15543, Arcana Spread 20791 |
| `0x180286fd0` | generic | 2 | 37 | ignition condition (slot 2); monster condition (slot 2) |  | Infernoble Knight Captain Oliver 15517, Disguise, the Copycat Hero 22063 |
| `0x18040b530` | card-specific | 2 | 608 | monster action (slot 0); spellTrap action (slot 0) | 18223 Weighbridge | Evil★Twins Ki-sikil & Lil-la 15628, Weighbridge 18223 |
| `0x18040e700` | card-specific | 2 | 333 | ignition action (slot 0); spellTrap action (slot 0) | 15612 Crowley, the Magistus of Grimoires | Pendulum Encore 15958, Pendulum Evolution 19189 |
| `0x180262150` | generic | 2 | 0 | ignition condition (slot 2); spellTrap condition (slot 2) |  | Sunavalon Bloom 15869, Reincarnation of the Seventh Emperors 17106 |
| `0x1801e4710` | card-specific | 2 | 526 | ignition target (slot 1) | 16032 Amaze Attraction Cyclo-Coaster, 21814 Kerzebs, the Gaoled Glutton | Amaze Attraction Cyclo-Coaster 16032, Kerzebs, the Gaoled Glutton 21814 |
| `0x18040e850` | card-specific | 2 | 509 | ignition action (slot 0) | 15520 Arc Rebellion Xyz Dragon, 15588 Dragunity Remus, 15608 | Amazement Ambassador Bufo 15968, Amazement Attendant Comica 15969 |
| `0x1802625a0` | card-specific | 2 | 132 | ignition condition (slot 2) | 8339 Emergency Assistance, 13527 Packet Link, 15388 Super All In!, 22213 Infernal Strike Fighter | Hidden Springs of the Far East 16027, Infernal Strike Fighter 22213 |
| `0x1802864f0` | generic | 2 | 130 | ignition condition (slot 2) |  | Golden-Eyes Idol 15840, K9-04 Noroi 21791 |
| `0x1804123f0` | card-specific | 2 | 87 | handGrave action (slot 0); monster action (slot 0) | 16247 Gunkan Sushipyard Seaside Supper Spot | Gunkan Sushipyard Seaside Supper Spot 16247, Gunkan Suship Catch-of-the-Day 18529 |
| `0x180414e60` | card-specific | 2 | 98 | ignition action (slot 0); monster action (slot 0) | 16508 Gunkan Suship Uni | Gunkan Suship Uni 16508, Galaxy Photon Dragon 18189 |
| `0x1801e4be0` | card-specific | 2 | 516 | monster target (slot 1); spellTrap target (slot 1) | 16819 Ghostrick Siren, 17551, 17886, 18892, 18995 ... | Ghostrick Siren 16819, Six Strike - Double Assault 20539 |
| `0x180270210` | generic | 2 | 125 | spellTrap condition (slot 2) |  | Imprudent Intrusion 16884, Riryoku Guardian 18331 |
| `0x180416ac0` | card-specific | 2 | 694 | monster action (slot 0); spellTrap action (slot 0) | 15516 Infernity Doom Archfiend, 16652 The Sacred Waters in the Sky | Protector of The Agents - Moon 16651, The Sacred Waters in the Sky 16652 |
| `0x18041c970` | card-specific | 2 | 278 | handGrave action (slot 0); monster action (slot 0) | 15428 | Branded Loss 17168, Angelechy Seneschal 23523 |
| `0x180420430` | card-specific | 2 | 1852 | ignition action (slot 0); spellTrap action (slot 0) | 4608 Penguin Soldier, 15428, 15516 Infernity Doom Archfiend | Branded Expulsion 17472, Albion the Sanctifire Dragon 18498 |
| `0x180418660` | card-specific | 2 | 512 | ignition action (slot 0); monster action (slot 0) | 15516 Infernity Doom Archfiend, 16859 Dogmatikamacabre, 18200 Dogmatikamatrix | Branded in Central Dogmatika 17460, Dogmatikamatrix 18200 |
| `0x18041f110` | generic | 2 | 348 | handGrave action (slot 0); monster action (slot 0) |  | Lovely Labrynth of the Silver Castle 17360, Spell Shattering Sword 23386 |
| `0x18026ca00` | generic | 2 | 124 | handGrave condition (slot 2); monster condition (slot 2) |  | Lovely Labrynth of the Silver Castle 17360, Spell Shattering Sword 23386 |
| `0x180287b50` | generic | 2 | 113 | spellTrap condition (slot 2) |  | Vaylantz World - Shinra Bansho 17358, Vaylantz World - Konig Wissen 17359 |
| `0x1804268c0` | card-specific | 2 | 721 | handGrave action (slot 0); spellTrap action (slot 0) | 15588 Dragunity Remus, 15608 | Spright Double Cross 17829, Goblin Bikers Gone Wild 20488 |
| `0x1801e3810` | card-specific | 2 | 1093 | ignition target (slot 1); spellTrap target (slot 1) | 14804 Gearbreed, 14937 Selene, Queen of the Master Magicians, 16847 Maple Maiden, 17034 Sky Striker Mobilize - Linkage!, 17772 Mitsu the Insect Ninja ... | Sky Striker Mobilize - Linkage! 17034, Monarchic Perfection 21487 |
| `0x1804234e0` | generic | 2 | 129 | spellTrap action (slot 0) |  | Amazoness Hall 17654, Amazoness Hot Spring 17655 |
| `0x18042cda0` | card-specific | 2 | 162 | handGrave action (slot 0); spellTrap action (slot 0) | 18331 Riryoku Guardian, 19045 Earthbound Prison | Riryoku Guardian 18331, Earthbound Prison 19045 |
| `0x180240e50` | generic | 2 | 32 | spellTrap condition (slot 2) |  | A Shattered, Colorless Realm 18537, Sharv Sarga 19214 |
| `0x1801da740` | card-specific | 2 | 826 | monster target (slot 1) | 5177, 11988 D/D/D Wave Oblivion King Caesar Ragnarok, 18390, 18670 Gold Pride - Pin Baller, 19700 ... | Gold Pride - Pin Baller 18670, Maiden in Love 21750 |
| `0x18042d870` | card-specific | 2 | 834 | monster action (slot 0); spellTrap action (slot 0) | 18487 Tsumuha-Kutsunagi the Lord of Swords, 20243 Vesper Girsu | Tsumuha-Kutsunagi the Lord of Swords 18487, Vesper Girsu 20243 |
| `0x1803cecd0` | card-specific | 2 | 146 | spellTrap action (slot 0) | 12023 Grand Horn of Heaven, 15368 Fossil Dragon Skullgar | Million-Century Ice Prison 18853, Sangen Kaiho 20252 |
| `0x18042fc30` | card-specific | 2 | 856 | monster action (slot 0) | 4608 Penguin Soldier, 18842 New World - Amritara, 20191 Knight Armed Dragon, the Armored Knight Dragon, 22923 Gaming Gamer GG | New World - Amritara 18842, Knight Armed Dragon, the Armored Knight Dragon 20191 |
| `0x180430200` | generic | 2 | 40 | handGrave action (slot 0) |  | Battlin' Boxer Promoter 18924, Raidraptor - Noir Lanius 19459 |
| `0x180430c60` | generic | 2 | 858 | handGrave action (slot 0) |  | Camelot, Realm of Noble Knights and Noble Arms 19140, Interdimensional Matter Forwarder 20245 |
| `0x1801e66c0` | card-specific | 2 | 272 | ignition target (slot 1) | 19503 Goblin's Crazy Beast, 20478 Drake Shark | Goblin's Crazy Beast 19503, Drake Shark 20478 |
| `0x180291b60` | card-specific | 2 | 162 | ignition condition (slot 2); spellTrap condition (slot 2) | 19524 Majespecter Wind, 21786 Solfachord Solfegia | Majespecter Wind 19524, Solfachord Solfegia 21786 |
| `0x18023f170` | card-specific | 2 | 161 | handGrave condition (slot 2); ignition condition (slot 2) | 14803 Infernoble Knight - Roland | Raidraptor - Bloom Vulture 19460, Vanquish Soul Hollie Sue 21433 |
| `0x180262ac0` | generic | 2 | 0 | handGrave condition (slot 2); monster condition (slot 2) |  | Life Shaver 5554, Abyss of the Sacred Beasts 23016 |
| `0x180437250` | card-specific | 2 | 535 | handGrave action (slot 0); spellTrap action (slot 0) | 23011 Ragged Records of Rites | Mira Match 19934, Ragged Records of Rites 23011 |
| `0x180489700` | card-specific | 2 | 226 | ignition prompt / pick (slot 4) | 15368 Fossil Dragon Skullgar | Winged Kuriboh LV6 19719, Number 69: Heraldry Crest - Shatter Stream 21182 |
| `0x18043ab30` | card-specific | 2 | 657 | handGrave action (slot 0); ignition action (slot 0) | 10030 Overlay Eater, 15516 Infernity Doom Archfiend, 15588 Dragunity Remus, 15608 | Performage Cup Tricker 20408, LeVirtue Dragon 20518 |
| `0x18020fc40` | card-specific | 2 | 109 | ignition cost (slot 3) | 12749 Zoodiac Ratpier | Gimmick Puppet Cattle Scream 20188, Constellar Castor 22201 |
| `0x18043e4c0` | generic | 2 | 144 | monster action (slot 0) |  | Mimighoul Armor 20714, Mimighoul Fairy 20715 |
| `0x18043eeb0` | card-specific | 2 | 293 | monster action (slot 0) | 15576 Samsara Dragon, 15588 Dragunity Remus, 15608 | Materiactor Meltdown 20796, Materiactor Meltthrough 21200 |
| `0x180281050` | card-specific | 2 | 181 | monster condition (slot 2) | 14356 Time Thief Redoer, 23366 CXyz Hope Chaos Barian Dragon, 23550 Raise Moon Hope Squeeze - Jackpot | Materiactor Meltdown 20796, Materiactor Meltthrough 21200 |
| `0x18026e230` | card-specific | 2 | 389 | handGrave condition (slot 2) | 10522 Tempest, Dragon Ruler of Storms | Spectral, Dragon Ruler of Flickers 21155, Nebulus, Dragon Ruler of Mishaps 21156 |
| `0x180232870` | generic | 2 | 210 | ignition condition (slot 2); monster condition (slot 2) |  | Wizard @Ignister 21138, Red Nova Dragon/Assault Mode 22513 |
| `0x18043fdf0` | generic | 2 | 41 | monster action (slot 0) |  | Wonko, Noble Knight of the Forest 21063, Cupsy☆Yummy 21365 |
| `0x180446030` | card-specific | 2 | 544 | monster action (slot 0) | 9656 Number 17: Leviathan Dragon, 15516 Infernity Doom Archfiend, 22330 Mercurium the Living Quicksilver, 22552 Superdreadnought Rail Cannon Gustav Rocket | Mercurium the Living Quicksilver 22330, Superdreadnought Rail Cannon Gustav Rocket 22552 |
| `0x180288160` | card-specific | 2 | 3379 | handGrave condition (slot 2); monster condition (slot 2) | 4909 Mystical Space Typhoon, 5558 King's Knight, 6341 King of the Skull Servants, 9447 Z-ONE, 10357 Spellbook Library of the Heliosphere ... | Combined Maneuver - Engage Zero! 19933, Martyr of the Sacred Beasts 22958 |
| `0x18044a2d0` | card-specific | 2 | 1544 | handGrave action (slot 0); ignition action (slot 0) | 4608 Penguin Soldier, 15516 Infernity Doom Archfiend, 22933 GMX Suppression Squad, 22934 GMX - COMPREX, 22935 Evolved Daneen | GMX Suppression Squad 22933, GMX 55th Experiment Report 22936 |
| `0x180446c60` | card-specific | 2 | 187 | handGrave action (slot 0); monster action (slot 0) | 22498 Regulus, the Prince of Endymion, 23576 Zombyra | Regulus, the Prince of Endymion 22498, Zombyra 23576 |
| `0x180297220` | generic | 2 | 71 | spellTrap condition (slot 2) |  | Trirealm Rift Gospel 23562, Trirealm Rift Territory - Valvols 23563 |
| `0x180217940` | generic | 2 | 366 | spellTrap cost (slot 3) |  | Trirealm Rift Gospel 23562, Trirealm Rift Territory - Valvols 23563 |
| `0x18044cae0` | single | 1 | 65 | ignition action (slot 0) |  | Mind Scan 23162 |
| `0x180296480` | single | 1 | 112 | ignition condition (slot 2) |  | Mind Scan 23162 |
| `0x180406f20` | single | 1 | 514 | spellTrap action (slot 0) |  | Afterglow 15375 |
| `0x180286be0` | single | 1 | 267 | spellTrap condition (slot 2) |  | Afterglow 15375 |
| `0x180286d30` | single | 1 | 31 | monster condition (slot 2) |  | Appliancer Dryer Drake 15400 |
| `0x18027b550` | single | 1 | 31 | ignition condition (slot 2) |  | Appliancer Vacculephant 15398 |
| `0x180286860` | single | 1 | 24 | handGrave condition (slot 2) |  | Ancient Warriors Saga - East-by-South Winds 15294 |
| `0x1804060b0` | single | 1 | 512 | spellTrap action (slot 0) |  | Diced Dice 15301 |
| `0x180286660` | single | 1 | 113 | ignition condition (slot 2) |  | Dogmatika Maximus 15243 |
| `0x180405600` | single | 1 | 621 | ignition action (slot 0) |  | Dogmatika Maximus 15243 |
| `0x180286740` | single | 1 | 254 | ignition condition (slot 2) | 15388 Super All In! | Dracoon Lamp 15269 |
| `0x180405c70` | single | 1 | 664 | monster action (slot 0) | 15516 Infernity Doom Archfiend, 15576 Samsara Dragon, 15588 Dragunity Remus, 20514 Centur-Ion Primera Primus | Frightfur Cruel Whale 15273 |
| `0x1804062b0` | single | 1 | 91 | spellTrap action (slot 0) |  | Horn of Olifant 15306 |
| `0x180259500` | single | 1 | 0 | ignition condition (slot 2) |  | Junk Sleep 15314 |
| `0x1802869f0` | single | 1 | 374 | spellTrap condition (slot 2) |  | Law of the Cosmos 15348 |
| `0x180406bc0` | single | 1 | 120 | spellTrap action (slot 0) |  | Law of the Cosmos 15348 |
| `0x180406310` | single | 1 | 120 | handGrave action (slot 0) |  | Millennium Revelation 15335 |
| `0x180409bb0` | single | 1 | 1168 | spellTrap action (slot 0) | 15516 Infernity Doom Archfiend | Banquet of Millions 15558 |
| `0x1804087f0` | single | 1 | 515 | handGrave action (slot 0) | 15516 Infernity Doom Archfiend | Conductor of Nephthys 15509 |
| `0x1801a66e0` | single | 1 | 30 | ignition target (slot 1) |  | Divine Arsenal AA-ZEUS - Sky Thunder 15524 |
| `0x180287000` | single | 1 | 208 | ignition condition (slot 2) |  | Geonator Transverser 15529 |
| `0x1804085f0` | single | 1 | 509 | monster action (slot 0) |  | Hinezumi Hanabi 15508 |
| `0x1804094a0` | single | 1 | 379 | spellTrap action (slot 0) |  | Jack-In-The-Hand 15546 |
| `0x18040a1f0` | single | 1 | 1220 | spellTrap action (slot 0) | 8000 Trap Reactor Y FI, 15516 Infernity Doom Archfiend | One or Eight 15559 |
| `0x1804751e0` | single | 1 | 1833 | spellTrap action (slot 0) | 4608 Penguin Soldier, 15428, 15516 Infernity Doom Archfiend | Tri-Brigade Revolt 15549 |
| `0x180408eb0` | single | 1 | 517 | spellTrap action (slot 0) | 15516 Infernity Doom Archfiend, 15520 Arc Rebellion Xyz Dragon | Virtual World City - Kauwloon 15534 |
| `0x18024a8b0` | single | 1 | 197 | spellTrap condition (slot 2) | 7766 Judgment of Thunder | Warning Point 15557 |
| `0x180287780` | single | 1 | 124 | monster condition (slot 2) |  | Drytron Fafnir 15643 |
| `0x180287570` | single | 1 | 102 | monster condition (slot 2) | 16673, 19666, 21838 Apocavities | Evil★Twins Ki-sikil & Lil-la 15628 |
| `0x180279e40` | single | 1 | 42 | monster condition (slot 2) |  | Ancient Warriors - Rebellious Lu Feng 15716 |
| `0x18040e560` | single | 1 | 59 | ignition action (slot 0) |  | Coordius the Triphasic Dealmon 15954 |
| `0x18040d7c0` | single | 1 | 612 | spellTrap action (slot 0) | 15588 Dragunity Remus, 15608, 15620 Trismagistus | Linear Equation Cannon 15771 |
| `0x18040c630` | single | 1 | 256 | monster action (slot 0) | 14718 Dream Mirror of Joy, 14719 Dream Mirror of Terror | Neiroy, the Dream Mirror Disciple 15717 |
| `0x18040c920` | single | 1 | 13 | spellTrap action (slot 0) |  | Springans Blast! 15760 |
| `0x1802879c0` | single | 1 | 59 | spellTrap condition (slot 2) |  | Springans Watch 15745 |
| `0x18040c730` | single | 1 | 46 | spellTrap action (slot 0) |  | Springans Watch 15745 |
| `0x18040da30` | single | 1 | 267 | spellTrap action (slot 0) |  | Terror of Trishula 15835 |
| `0x180287ed0` | single | 1 | 132 | spellTrap condition (slot 2) |  | Terror of Trishula 15835 |
| `0x180287450` | single | 1 | 10 | monster condition (slot 2) |  | Dragunity Arma Gram 15586 |
| `0x1801dc8c0` | single | 1 | 110 | ignition target (slot 1) |  | Lavalval Salamander 15846 |
| `0x18040e010` | single | 1 | 168 | ignition action (slot 0) |  | Time Thief Double Barrel 15856 |
| `0x18040fc70` | single | 1 | 50 | spellTrap action (slot 0) |  | Ogdoadic Calling 16083 |
| `0x180410520` | single | 1 | 223 | spellTrap action (slot 0) |  | Solfachord Elegance 16092 |
| `0x1802898c0` | single | 1 | 15 | ignition condition (slot 2) |  | Solfachord Harmonia 16094 |
| `0x180289b80` | single | 1 | 19 | spellTrap condition (slot 2) |  | Solfachord Musica 16095 |
| `0x180410600` | single | 1 | 1208 | spellTrap action (slot 0) | 7128 "A" Cell Breeding Device, 15516 Infernity Doom Archfiend, 15520 Arc Rebellion Xyz Dragon | Solfachord Scale 16093 |
| `0x180259dc0` | single | 1 | 210 | monster condition (slot 2) | 7079 Ancient City - Rainbow Ruins, 7081 Last Resort, 9232 Malefic World, 10795 Vampire Kingdom, 10806 Vampire Takeover ... | Ursarctic Polari 16103 |
| `0x180410ac0` | single | 1 | 602 | ignition action (slot 0) | 15428 | Ursarctic Quint Charge 16109 |
| `0x18040f540` | single | 1 | 337 | spellTrap action (slot 0) | 16021 A.I. Meet You | A.I. Meet You 16021 |
| `0x18040f8e0` | single | 1 | 69 | ignition action (slot 0) |  | Amaze Attraction Wonder Wheel 16033 |
| `0x18040ea50` | single | 1 | 824 | monster action (slot 0) | 15428 | Bahalutiya, the Grand Radiance 15984 |
| `0x180240970` | single | 1 | 0 | spellTrap condition (slot 2) |  | Boo-Boo Game 16040 |
| `0x180289190` | single | 1 | 0 | monster condition (slot 2) |  | Clock Arc 15991 |
| `0x18040f930` | single | 1 | 117 | spellTrap action (slot 0) |  | Fukubiki 16041 |
| `0x18040f3f0` | single | 1 | 150 | ignition action (slot 0) | 15998 Lavalval Exlord | Lavalval Exlord 15998 |
| `0x180412fc0` | single | 1 | 386 | ignition action (slot 0) | 11013 Number C6: Chronomaly Chaos Atlandis, 14591 Earthbound Greater Linewalker, 16273 Pendransaction | Pendransaction 16273 |
| `0x18040f490` | single | 1 | 167 | handGrave action (slot 0) |  | Star Mine 15999 |
| `0x18028a280` | single | 1 | 77 | spellTrap condition (slot 2) |  | Terrors of the Underroot 16275 |
| `0x18040ed90` | single | 1 | 115 | monster action (slot 0) |  | White Knight of Dogmatika 15993 |
| `0x180289200` | single | 1 | 448 | monster condition (slot 2) |  | You and A.I. 16022 |
| `0x18040db40` | single | 1 | 1217 | ignition action (slot 0) | 4608 Penguin Soldier, 15780 Chaos Grepher | Millennium Seeker 15841 |
| `0x180414430` | single | 1 | 617 | ignition action (slot 0) | 15428 | Imperial Bower 16479 |
| `0x180414970` | single | 1 | 428 | spellTrap action (slot 0) | 15516 Infernity Doom Archfiend, 15520 Arc Rebellion Xyz Dragon | Thunderspeed Summon 16482 |
| `0x180411920` | single | 1 | 1133 | ignition action (slot 0) | 15588 Dragunity Remus, 15608 | Aeropixthree 16220 |
| `0x180410fe0` | single | 1 | 0 | handGrave action (slot 0) |  | Albion the Shrouded Dragon 16197 |
| `0x180411500` | single | 1 | 82 | monster action (slot 0) |  | Doombearer Psychopompos 16217 |
| `0x180411230` | single | 1 | 711 | ignition action (slot 0) | 4608 Penguin Soldier, 15516 Infernity Doom Archfiend | Gunkan Suship Ikura 16201 |
| `0x1804122c0` | single | 1 | 299 | spellTrap action (slot 0) |  | Magikey Maftea 16245 |
| `0x180496ef0` | single | 1 | 32 | ignition prompt / pick (slot 4) |  | Majestic Mirage 16259 |
| `0x1804125b0` | single | 1 | 1128 | spellTrap action (slot 0) | 4608 Penguin Soldier | Synchro Overtake 16256 |
| `0x180413150` | single | 1 | 221 | ignition action (slot 0) |  | Hi-Speedroid Clear Wing Rider 16380 |
| `0x180413230` | single | 1 | 464 | spellTrap action (slot 0) |  | Speedroid Wheel 16381 |
| `0x18023fd60` | single | 1 | 0 | monster condition (slot 2) |  | Borrelcode Dragon 16536 |
| `0x180415b20` | single | 1 | 47 | spellTrap action (slot 0) |  | Branded in High Spirits 16541 |
| `0x1801e5390` | single | 1 | 178 | ignition target (slot 1) | 16502 Floowandereeze & Snowl | Floowandereeze & Snowl 16502 |
| `0x180415aa0` | single | 1 | 32 | monster action (slot 0) |  | Magikey Spirit - Vepartu 16533 |
| `0x1803f9f90` | single | 1 | 443 | handGrave action (slot 0) | 4064 Kuriboh, 14607 Link Apple, 16384 White Steed of the Floral Knights, 16512 Penguin Cleric | Penguin Cleric 16512 |
| `0x180497070` | single | 1 | 400 | spellTrap prompt / pick (slot 4) |  | Reptilianne Ramifications 16546 |
| `0x180415d60` | single | 1 | 314 | spellTrap action (slot 0) |  | Reptilianne Ramifications 16546 |
| `0x180415ea0` | single | 1 | 1376 | spellTrap action (slot 0) | 4608 Penguin Soldier, 15588 Dragunity Remus, 15608, 15616 Aiwass, the Magistus Spell Spirit | Small World 16555 |
| `0x180416400` | single | 1 | 742 | spellTrap action (slot 0) | 15516 Infernity Doom Archfiend, 15520 Arc Rebellion Xyz Dragon | Stained Glass of Light & Dark 16564 |
| `0x180415770` | single | 1 | 804 | monster action (slot 0) | 4608 Penguin Soldier, 15516 Infernity Doom Archfiend | Swordsoul Grandmaster - Chixiao 16527 |
| `0x18028b110` | single | 1 | 116 | monster condition (slot 2) |  | Swordsoul Grandmaster - Chixiao 16527 |
| `0x1801e5450` | single | 1 | 101 | monster target (slot 1) | 10256 Metallizing Parasite - Soltite, 16846 Swordsoul Sinister Sovereign - Qixing Longyuan, 20202 Lord of the Missing Barrows | Swordsoul Sinister Sovereign - Qixing Longyuan 16846 |
| `0x180417eb0` | single | 1 | 163 | ignition action (slot 0) | 16846 Swordsoul Sinister Sovereign - Qixing Longyuan | Swordsoul Sinister Sovereign - Qixing Longyuan 16846 |
| `0x18028b2e0` | single | 1 | 211 | spellTrap condition (slot 2) | 15643 Drytron Fafnir, 16108 Ursarctic Big Dipper | Ursarctic Drytron 16552 |
| `0x180366360` | single | 1 | 312 | spellTrap action (slot 0) | 6539 Clay Charge | Numeron Storm 16426 |
| `0x180413f10` | single | 1 | 539 | monster action (slot 0) | 7124 Crystal Promise, 15576 Samsara Dragon, 15588 Dragunity Remus, 15608, 15624 Live☆Twin Ki-sikil | Doll House 16427 |
| `0x180414220` | single | 1 | 31 | ignition action (slot 0) |  | Double Exposure 16434 |
| `0x1803eb100` | single | 1 | 203 | handGrave action (slot 0) | 16407 Dragonroid | Dragonroid 16407 |
| `0x180413460` | single | 1 | 32 | monster action (slot 0) |  | Penguin Sword 16404 |
| `0x180258570` | single | 1 | 234 | ignition condition (slot 2) | 6772 Neo-Spacian Dark Panther, 7806 Elemental HERO Divine Neos, 11133 CXyz Barian Hope, 12272 Red Supremacy, 16650 Masterflare Hyperion | Starving Venemy Dragon 16428 |
| `0x1804174b0` | single | 1 | 398 | monster action (slot 0) |  | Fateful Adventure 16752 |
| `0x18041a490` | single | 1 | 248 | spellTrap action (slot 0) |  | Zektrike Kou-ou 17032 |
| `0x18028ba20` | single | 1 | 100 | spellTrap condition (slot 2) |  | Zektrike Kou-ou 17032 |
| `0x1804181c0` | single | 1 | 33 | pendulum action (slot 0) |  | D/D/D Deviser King Deus Machinex 16849 |
| `0x180417f60` | single | 1 | 111 | ignition action (slot 0) |  | D/D/D Deviser King Deus Machinex 16849 |
| `0x180417d80` | single | 1 | 304 | ignition action (slot 0) |  | Dinomorphia Stealthbergia 16844 |
| `0x180418cf0` | single | 1 | 601 | spellTrap action (slot 0) | 13490 Masterking Archfiend, 15768 Dream Mirror Recap, 21170 Brain Controller | Ghostrick or Treat 16881 |
| `0x1804179f0` | single | 1 | 901 | ignition action (slot 0) | 4608 Penguin Soldier, 15717 Neiroy, the Dream Mirror Disciple, 16823 Neiroy, the Dream Mirror Traitor | Neiroy, the Dream Mirror Traitor 16823 |
| `0x18041cee0` | single | 1 | 154 | spellTrap action (slot 0) |  | Sol and Luna 17231 |
| `0x18028b840` | single | 1 | 0 | spellTrap condition (slot 2) |  | Top Share 16869 |
| `0x180418f50` | single | 1 | 2720 | spellTrap action (slot 0) | 4608 Penguin Soldier, 8137 Power Tool Dragon, 9381 Gishki Noellia, 15516 Infernity Doom Archfiend | Tribe Drive 16883 |
| `0x180418910` | single | 1 | 345 | handGrave action (slot 0) | 15794 | Uradora of Fate 16870 |
| `0x18041b1f0` | single | 1 | 386 | spellTrap action (slot 0) |  | Branded Retribution 17068 |
| `0x18027d1f0` | single | 1 | 549 | ignition condition (slot 2) | 21061 Two-for-One Repair Job, 22564 Predaprimitive | Majesty Hyperion 16645 |
| `0x18041ca90` | single | 1 | 564 | ignition action (slot 0) | 15516 Infernity Doom Archfiend | Ancient Warriors Saga - Deception and Betrayal 17175 |
| `0x18041c660` | single | 1 | 35 | handGrave action (slot 0) |  | Endless Engine Argyro System 17165 |
| `0x180234170` | single | 1 | 0 | monster condition (slot 2) |  | Light Law Medium 5083 |
| `0x18041c130` | single | 1 | 1328 | monster action (slot 0) | 8192 Exploder Dragonwing, 15428 | Musical Sumo Dice Games 17158 |
| `0x18041cd70` | single | 1 | 274 | spellTrap action (slot 0) |  | Pendulum Scale 17180 |
| `0x1801e55f0` | single | 1 | 235 | handGrave target (slot 1) | 17113 Performapal Gentrude | Performapal Gentrude 17113 |
| `0x18041ccd0` | single | 1 | 152 | spellTrap action (slot 0) |  | Surprise Chain 17179 |
| `0x18041b7f0` | single | 1 | 360 | monster action (slot 0) | 11610 Symph Amplifire | Symphonic Warrior DJJ 17133 |
| `0x18041ce90` | single | 1 | 72 | spellTrap action (slot 0) |  | Therion Cross 17181 |
| `0x18041ad60` | single | 1 | 116 | monster action (slot 0) | 15368 Fossil Dragon Skullgar | Ocean Dragon Lord - Kairyu-Shin 17047 |
| `0x18041aa40` | single | 1 | 792 | spellTrap action (slot 0) | 4608 Penguin Soldier, 15516 Infernity Doom Archfiend | Seventh Ascension 17044 |
| `0x18028ba90` | single | 1 | 90 | spellTrap condition (slot 2) |  | Seventh Ascension 17044 |
| `0x18028c800` | single | 1 | 242 | spellTrap condition (slot 2) |  | Branded Expulsion 17472 |
| `0x18028c7e0` | single | 1 | 0 | monster condition (slot 2) |  | Branded in Central Dogmatika 17460 |
| `0x18028c1f0` | single | 1 | 257 | ignition condition (slot 2) |  | Krawler Soma 17431 |
| `0x18041f8e0` | single | 1 | 211 | ignition action (slot 0) |  | Krawler Soma 17431 |
| `0x18020fcb0` | single | 1 | 150 | ignition cost (slot 3) | 17552 Loris, Lady of Lament | Loris, Lady of Lament 17552 |
| `0x1804201c0` | single | 1 | 620 | ignition action (slot 0) | 15588 Dragunity Remus, 15608, 15624 Live☆Twin Ki-sikil | Melffy Staring Contest 17465 |
| `0x180421d00` | single | 1 | 303 | spellTrap action (slot 0) |  | Morphtronic Converter 17601 |
| `0x1804219c0` | single | 1 | 254 | ignition action (slot 0) |  | Morphtronic Telefon 17596 |
| `0x1804218e0` | single | 1 | 214 | ignition action (slot 0) |  | Morphtronic Telefon 17596 |
| `0x18041fb50` | single | 1 | 518 | spellTrap action (slot 0) | 15520 Arc Rebellion Xyz Dragon | Over Fusion 17455 |
| `0x180421ac0` | single | 1 | 136 | ignition action (slot 0) |  | Power Tool Braver Dragon 17600 |
| `0x18028c300` | single | 1 | 594 | ignition condition (slot 2) |  | Propa Gandake 17437 |
| `0x1801e5940` | single | 1 | 243 | ignition target (slot 1) | 17362 Ariane the Labrynth Servant | Ariane the Labrynth Servant 17362 |
| `0x180202a40` | single | 1 | 41 | spellTrap cost (slot 3) |  | Labrynth Barrage 17371 |
| `0x18041f4f0` | single | 1 | 295 | spellTrap action (slot 0) |  | Runick Smiting Storm 17378 |
| `0x18041e740` | single | 1 | 215 | ignition action (slot 0); monster action (slot 0) | 15802 | Saion the Vaylantz Archer 17348 |
| `0x18041e820` | single | 1 | 27 | monster action (slot 0) |  | Vaylantz Buster Baron 17351 |
| `0x18041eb00` | single | 1 | 274 | monster action (slot 0) |  | Vaylantz Mad Marquess 17353 |
| `0x18041ea10` | single | 1 | 227 | ignition action (slot 0) |  | Vaylantz Mad Marquess 17353 |
| `0x18041d090` | single | 1 | 452 | monster action (slot 0) | 15368 Fossil Dragon Skullgar, 15794, 15802 | Duel Tower 17244 |
| `0x180214b80` | single | 1 | 101 | spellTrap cost (slot 3) |  | Awakening of the Crystal Ultimates 17729 |
| `0x180424e20` | single | 1 | 91 | spellTrap action (slot 0) |  | Awakening of the Crystal Ultimates 17729 |
| `0x18028de50` | single | 1 | 292 | spellTrap condition (slot 2) | 15388 Super All In!, 15392 Fire Flint Lady | Ghoti Cosmos 18040 |
| `0x180428650` | single | 1 | 227 | spellTrap action (slot 0) |  | Ghoti Cosmos 18040 |
| `0x180425220` | single | 1 | 266 | monster action (slot 0) | 22108 Hollowrokket Dragon | Kashtira Ogre 17770 |
| `0x1803a85a0` | single | 1 | 675 | ignition action (slot 0) | 15662, 16183, 17772 Mitsu the Insect Ninja, 19119, 19121 ... | Mitsu the Insect Ninja 17772 |
| `0x180426360` | single | 1 | 867 | spellTrap action (slot 0) | 4608 Penguin Soldier, 15428 | Ninjitsu Art Notebook of Mystery 17816 |
| `0x180425d70` | single | 1 | 175 | handGrave action (slot 0) |  | Psychic Rover 17790 |
| `0x180425670` | single | 1 | 208 | monster action (slot 0) |  | Psychic Rover 17790 |
| `0x180425740` | single | 1 | 732 | monster action (slot 0) | 15428 | Silent Wolf Calupo 17792 |
| `0x180425a20` | single | 1 | 55 | monster action (slot 0) |  | Silent Wolf Calupo 17792 |
| `0x18028ce80` | single | 1 | 119 | spellTrap condition (slot 2) |  | Simul Archfiends 17833 |
| `0x180426ba0` | single | 1 | 3035 | spellTrap action (slot 0) |  | Simul Archfiends 17833 |
| `0x18028cdd0` | single | 1 | 176 | spellTrap condition (slot 2) |  | Spright Double Cross 17829 |
| `0x180424e80` | single | 1 | 451 | handGrave action (slot 0) |  | The Bystial Alba Los 17766 |
| `0x180427780` | single | 1 | 281 | spellTrap action (slot 0) | 4608 Penguin Soldier, 17835 The Great Noodle Inversion | The Great Noodle Inversion 17835 |
| `0x180425e20` | single | 1 | 75 | ignition action (slot 0) |  | Wollow, Founder of the Drudge Dragons 17802 |
| `0x180422d00` | single | 1 | 210 | ignition action (slot 0) |  | Advanced Crystal Beast Cobalt Eagle 17633 |
| `0x1804225f0` | single | 1 | 74 | ignition action (slot 0) |  | Dice Dungeon 17623 |
| `0x18017ea60` | single | 1 | 7000 | handGrave target (slot 1) | 4798 Toon World, 5552 Y-Dragon Head, 5555 Z-Metal Tank, 5975 White Magician Pikeru, 6210 Flint ... | Dragon Nails 4809 |
| `0x180423570` | single | 1 | 206 | handGrave action (slot 0) | 4064 Kuriboh | G Golem Rock Hammer 17656 |
| `0x180422f20` | single | 1 | 291 | ignition action (slot 0) | 17635 Gadget Box, 23391 Spell Power Attainment | Gadget Box 17635 |
| `0x180423220` | single | 1 | 692 | ignition action (slot 0) | 15516 Infernity Doom Archfiend | Performapal Odd-Eyes Seer 17647 |
| `0x180422060` | single | 1 | 1172 | spellTrap action (slot 0) | 15428, 15588 Dragunity Remus, 15608 | Royal Straight 17621 |
| `0x180422500` | single | 1 | 232 | handGrave action (slot 0) | 15802 | Thunder Ball 17622 |
| `0x18028ddd0` | single | 1 | 119 | ignition condition (slot 2) |  | Epurrely Beauty 18005 |
| `0x180427da0` | single | 1 | 169 | ignition action (slot 0) |  | Rescue-ACE Impulse 17991 |
| `0x180269fd0` | single | 1 | 208 | ignition condition (slot 2) |  | Rescue-ACE Impulse 17991 |
| `0x18042a840` | single | 1 | 378 | monster action (slot 0) | 15780 Chaos Grepher | Abyss Actor - Liberty Dramatist 18161 |
| `0x180355cb0` | single | 1 | 251 | handGrave action (slot 0) | 7124 Crystal Promise | Cassimolar 7504 |
| `0x18028dff0` | single | 1 | 269 | handGrave condition (slot 2) |  | Couple of Aces 18172 |
| `0x1804289c0` | single | 1 | 136 | handGrave action (slot 0) |  | Couple of Aces 18172 |
| `0x180429910` | single | 1 | 83 | monster action (slot 0) |  | Galaxy Hundred 18196 |
| `0x180429760` | single | 1 | 420 | monster action (slot 0) | 15516 Infernity Doom Archfiend | Gigantic "Champion" Sargas 18190 |
| `0x18042b100` | single | 1 | 1560 | spellTrap action (slot 0) | 4608 Penguin Soldier | Intimidating Ore - Summonite 18225 |
| `0x18042af50` | single | 1 | 420 | spellTrap action (slot 0) |  | Kashtira Big Bang 18220 |
| `0x180429b50` | single | 1 | 1104 | spellTrap action (slot 0) | 7128 "A" Cell Breeding Device, 15516 Infernity Doom Archfiend | Land Flipping 18215 |
| `0x18028cba0` | single | 1 | 85 | handGrave condition (slot 2) | 22580 King's Resonance | Qardan the Clear-Sighted 17725 |
| `0x180429280` | single | 1 | 36 | handGrave action (slot 0) |  | Rindbrumm the Striking Dragon 18179 |
| `0x180421e30` | single | 1 | 553 | monster action (slot 0) | 18153 Tearlaments Kashtira, 23083, 23234 | Tearlaments Kashtira 18153 |
| `0x18028df80` | single | 1 | 103 | monster condition (slot 2) | 18153 Tearlaments Kashtira, 23234 | Tearlaments Kashtira 18153 |
| `0x180275b40` | single | 1 | 79 | spellTrap condition (slot 2) |  | Terrifying Trap Hole Nightmare 18312 |
| `0x18041dec0` | single | 1 | 621 | monster action (slot 0) | 4608 Penguin Soldier, 15368 Fossil Dragon Skullgar, 15780 Chaos Grepher | Battle Royal Mode - Joining 17260 |
| `0x18028be50` | single | 1 | 73 | monster condition (slot 2) | 16655 Piercing the Darkness | Black Luster Soldier - Legendary Swordsman 17242 |
| `0x18042cd00` | single | 1 | 152 | ignition action (slot 0) | 18323 Labyrinth Heavy Tank | Labyrinth Heavy Tank 18323 |
| `0x18041d860` | single | 1 | 66 | monster action (slot 0) | 15368 Fossil Dragon Skullgar | Performapal Duelist Extraordinaire 17258 |
| `0x18041daa0` | single | 1 | 52 | monster action (slot 0) |  | Performapal Duelist Extraordinaire 17258 |
| `0x18042df10` | single | 1 | 360 | handGrave action (slot 0) |  | Albion the Sanctifire Dragon 18498 |
| `0x18042eb40` | single | 1 | 1276 | spellTrap action (slot 0) | 4608 Penguin Soldier, 15768 Dream Mirror Recap | Hatsugai 18543 |
| `0x18041b380` | single | 1 | 1133 | spellTrap action (slot 0) |  | Numbers Eveil 17105 |
| `0x18042e9e0` | single | 1 | 221 | spellTrap action (slot 0) |  | Pig Iron vs. Pen Peg 18531 |
| `0x18042e2d0` | single | 1 | 318 | spellTrap action (slot 0) |  | Solfachord Symphony 18528 |
| `0x180267410` | single | 1 | 27 | handGrave condition (slot 2) |  | Sweet Dreams, Nemleria 18523 |
| `0x18042e410` | single | 1 | 63 | handGrave action (slot 0) |  | Sweet Dreams, Nemleria 18523 |
| `0x18028f030` | single | 1 | 92 | spellTrap condition (slot 2) |  | Time-Tearing Morganite 18530 |
| `0x18040a6c0` | single | 1 | 45 | handGrave action (slot 0) |  | Votis 7506 |
| `0x180355db0` | single | 1 | 409 | monster action (slot 0) | 15428, 15516 Infernity Doom Archfiend | Votis 7506 |
| `0x18042dbc0` | single | 1 | 497 | handGrave action (slot 0); monster action (slot 0) | 15516 Infernity Doom Archfiend, 18494 Wannabee! | Wannabee! 18494 |
| `0x18042f870` | single | 1 | 105 | handGrave action (slot 0) |  | Chef's Special Recipe 18750 |
| `0x1801e6570` | single | 1 | 162 | ignition target (slot 1) |  | Vanquish Soul Dr. Mad Love 18730 |
| `0x18042f830` | single | 1 | 53 | ignition action (slot 0) |  | Vanquish Soul Dr. Mad Love 18730 |
| `0x18033b2a0` | single | 1 | 160 | spellTrap action (slot 0) | 15516 Infernity Doom Archfiend | Arms Regeneration 6455 |
| `0x180257db0` | single | 1 | 0 | monster condition (slot 2) |  | Battlefield Tragedy 6715 |
| `0x180408280` | single | 1 | 172 | monster action (slot 0) |  | Don't Slip, the Dogs of War 15468 |
| `0x18042d3e0` | single | 1 | 369 | spellTrap action (slot 0) |  | Golden Rule 18431 |
| `0x1801dd180` | single | 1 | 730 | handGrave target (slot 1) | 12228 D/D Orthros, 12913 Time Pendulumgraph, 17109 Performapal Odd-Eyes Butler | Performapal Odd-Eyes Butler 17109 |
| `0x1804081e0` | single | 1 | 155 | monster action (slot 0) |  | Tri-Edge Master 15466 |
| `0x18024b330` | single | 1 | 216 | spellTrap condition (slot 2) | 5199 Fire Sorcerer | Aqua Chorus Round 18854 |
| `0x18024c250` | single | 1 | 0 | spellTrap condition (slot 2) |  | Banishing Trap Hole 18864 |
| `0x18042f8e0` | single | 1 | 51 | handGrave action (slot 0) |  | Doomstar Ulka 18815 |
| `0x18028f6f0` | single | 1 | 790 | monster condition (slot 2) |  | Double-Headed Dino King Rex 18794 |
| `0x18042fa30` | single | 1 | 147 | monster action (slot 0) |  | Greed Jar 18816 |
| `0x1802577a0` | single | 1 | 0 | ignition condition (slot 2) |  | Mighty Dino King Rex 18795 |
| `0x180254b10` | single | 1 | 677 | spellTrap condition (slot 2) | 6558 Gorz the Emissary of Darkness, 18866 Split Mirror of the Underworld | Split Mirror of the Underworld 18866 |
| `0x18041f340` | single | 1 | 120 | handGrave action (slot 0); ignition action (slot 0) | 19159 Arias the Labrynth Butler | Arias the Labrynth Butler 19159 |
| `0x1802914e0` | single | 1 | 116 | ignition condition (slot 2) |  | Asset Mountis 19413 |
| `0x180433020` | single | 1 | 979 | ignition action (slot 0) | 4608 Penguin Soldier, 15368 Fossil Dragon Skullgar, 15780 Chaos Grepher | Card Scanner 19208 |
| `0x1804326c0` | single | 1 | 443 | spellTrap action (slot 0) |  | Concours de Cuisine (Culinary Confrontation) 19206 |
| `0x180431200` | single | 1 | 501 | monster action (slot 0) |  | Hapi, Guidance of Horus 19155 |
| `0x180289090` | single | 1 | 99 | ignition condition (slot 2) |  | I.A.S. -Invasive Alien Species- 19165 |
| `0x180431f60` | single | 1 | 185 | ignition action (slot 0) |  | Origami Goddess 19169 |
| `0x180431640` | single | 1 | 945 | monster action (slot 0) | 15588 Dragunity Remus, 15608 | Poissonniere de Nouvelles 19161 |
| `0x180432510` | single | 1 | 432 | spellTrap action (slot 0) |  | Sinful Spoils of Doom - Rciela 19197 |
| `0x18042c830` | single | 1 | 890 | spellTrap action (slot 0) | 15608 | Switch Point 18317 |
| `0x180433400` | single | 1 | 266 | spellTrap action (slot 0) |  | Vanquish Soul Snow Devil 19220 |
| `0x180431400` | single | 1 | 236 | monster action (slot 0) |  | Wattuna 19157 |
| `0x1804338d0` | single | 1 | 790 | ignition action (slot 0) |  | Centur-Ion Trudea 19373 |
| `0x180291430` | single | 1 | 82 | ignition condition (slot 2) |  | Centur-Ion Trudea 19373 |
| `0x180430a60` | single | 1 | 243 | ignition action (slot 0) |  | Earthbound Servant Geo Gremlin 19043 |
| `0x1804350f0` | single | 1 | 107 | spellTrap action (slot 0) | 19732 Salamandra with Chain | Salamandra with Chain 19732 |
| `0x1804384b0` | single | 1 | 119 | spellTrap action (slot 0) |  | Time Dimension Hole 20034 |
| `0x180434390` | single | 1 | 96 | monster action (slot 0) |  | Goblin Freefall Squad 19485 |
| `0x180434120` | single | 1 | 55 | monster action (slot 0) |  | Keaf, Murk of the Ghoti 19470 |
| `0x180291fe0` | single | 1 | 147 | ignition condition (slot 2) |  | King of the Ashened City 19836 |
| `0x1804343f0` | single | 1 | 264 | ignition action (slot 0) |  | Royal Rhino with Deceitful Dice 19491 |
| `0x180435be0` | single | 1 | 246 | monster action (slot 0) |  | Cooling Embers 19874 |
| `0x180436650` | single | 1 | 748 | spellTrap action (slot 0) | 15516 Infernity Doom Archfiend, 15520 Arc Rebellion Xyz Dragon | Sangen Kaimen 19901 |
| `0x1802921d0` | single | 1 | 10 | spellTrap condition (slot 2) |  | Sinful Spoils Struggle 19898 |
| `0x1804357f0` | single | 1 | 56 | monster action (slot 0) |  | Snake-Eyes Diabellstar 19852 |
| `0x180436060` | single | 1 | 246 | monster action (slot 0) |  | Tantrum Toddler 19887 |
| `0x18042fad0` | single | 1 | 104 | ignition action (slot 0) |  | Way Where There's a Will 19908 |
| `0x18035ba10` | single | 1 | 267 | handGrave action (slot 0); ignition action (slot 0) |  | Wightlord 19866 |
| `0x18043a380` | single | 1 | 0 | monster action (slot 0) |  | Galaxy-Eyes Tachyon Primal 20277 |
| `0x18042f430` | single | 1 | 673 | ignition action (slot 0) | 18722 Transcendosaurus Gigantozowler | Blaze, Supreme Ruler of all Dragons 19542 |
| `0x1804975e0` | single | 1 | 23 | ignition prompt / pick (slot 4) |  | Dragon Master Magia 19718 |
| `0x180291ce0` | single | 1 | 175 | ignition condition (slot 2) |  | Earthbound Resonance 19745 |
| `0x180434580` | single | 1 | 458 | ignition action (slot 0) | 15588 Dragunity Remus, 15608 | Frozen Domain of the Ice Barrier 19622 |
| `0x180434940` | single | 1 | 123 | ignition action (slot 0); monster action (slot 0) |  | Frozen Domain of the Ice Barrier 19622 |
| `0x180291ca0` | single | 1 | 60 | monster condition (slot 2) |  | Gathering Light 19744 |
| `0x180437680` | single | 1 | 870 | monster action (slot 0) | 15428 | Wonky Quartet 19936 |
| `0x180438ce0` | single | 1 | 174 | monster action (slot 0) | 15802 | Bettan Bat 20209 |
| `0x18043a0f0` | single | 1 | 641 | handGrave action (slot 0) | 4064 Kuriboh, 15576 Samsara Dragon, 15588 Dragunity Remus, 15608, 15624 Live☆Twin Ki-sikil | Madolche Dessert 20256 |
| `0x1801b0c40` | single | 1 | 608 | spellTrap target (slot 1) | 13818 Gabrion, the Timelord, 20232 Millennium Ankh | Millennium Ankh 20232 |
| `0x18043b170` | single | 1 | 169 | monster action (slot 0) |  | Mimighoul Archfiend 20431 |
| `0x18043b090` | single | 1 | 221 | monster action (slot 0) | 20430 Mimighoul Cerberus | Mimighoul Cerberus 20430 |
| `0x18043aff0` | single | 1 | 151 | monster action (slot 0) |  | Mimighoul Dragon 20429 |
| `0x180292e80` | single | 1 | 65 | ignition condition (slot 2) |  | Obliterate!!! Blaze 20234 |
| `0x180439e90` | single | 1 | 600 | ignition action (slot 0) | 15588 Dragunity Remus, 15608, 20233 Wedju Temple | Wedju Temple 20233 |
| `0x180497720` | single | 1 | 193 | ignition prompt / pick (slot 4) | 15516 Infernity Doom Archfiend | Goblin Biker Grand Crisis 20551 |
| `0x180215dc0` | single | 1 | 272 | ignition cost (slot 3) | 15516 Infernity Doom Archfiend | Heosvarog the Mechanical Dawn 20511 |
| `0x1801cba40` | single | 1 | 30 | ignition target (slot 1) |  | LeVirtue Dragon 20518 |
| `0x18043b9c0` | single | 1 | 344 | monster action (slot 0) |  | Mermail King - Neptabyss 20522 |
| `0x18043b640` | single | 1 | 884 | monster action (slot 0) | 15516 Infernity Doom Archfiend, 15520 Arc Rebellion Xyz Dragon | Newbee! 20505 |
| `0x1801e6900` | single | 1 | 630 | ignition target (slot 1) | 20525 Reincarnation Unveiling Mail, 22864, 22994 Chaos Mystic Box | Reincarnation Unveiling Mail 20525 |
| `0x180293a70` | single | 1 | 143 | spellTrap condition (slot 2) |  | Summer Schoolwork Successful! 20556 |
| `0x18043c760` | single | 1 | 27 | handGrave action (slot 0) |  | Virtue Stream 20544 |
| `0x18043e5e0` | single | 1 | 1016 | monster action (slot 0) | 4608 Penguin Soldier | Mimighoul Charm 20720 |
| `0x1802631d0` | single | 1 | 407 | ignition condition (slot 2) | 8501 Bending Destiny, 10057 Lightray Gearfried, 10806 Vampire Takeover, 17515, 17976 | Heavy Cavalry of the Indestructible Insects 20706 |
| `0x18043e550` | single | 1 | 144 | monster action (slot 0) |  | Mimighoul Slime 20716 |
| `0x18043e430` | single | 1 | 144 | spellTrap action (slot 0) |  | Release Brainwashing 20710 |
| `0x1802148c0` | single | 1 | 57 | handGrave cost (slot 3) |  | Kyoro Ryu-Ge Kaiva 20592 |
| `0x18043fd10` | single | 1 | 209 | ignition action (slot 0) | 15768 Dream Mirror Recap | Ame no Murakumo no Mitsurugi 21054 |
| `0x18025ca30` | single | 1 | 27 | monster condition (slot 2) |  | Gladiator Beast Claudius 20775 |
| `0x18043eb30` | single | 1 | 309 | ignition action (slot 0) | 15804 | Goddess of the Two Sides 20766 |
| `0x18043ec70` | single | 1 | 178 | ignition action (slot 0) |  | Materiactor Exagard 20782 |
| `0x18043ed30` | single | 1 | 372 | ignition action (slot 0) | 20796 Materiactor Meltdown, 20977, 23226 | Materiactor Meltdown 20796 |
| `0x18043e9e0` | single | 1 | 112 | handGrave action (slot 0) | 20745 Speedroid Colonel Clackers | Speedroid Colonel Clackers 20745 |
| `0x18043fbc0` | single | 1 | 326 | spellTrap action (slot 0) |  | The Toil of the Normal 21053 |
| `0x18043d920` | single | 1 | 984 | spellTrap action (slot 0) | 15516 Infernity Doom Archfiend, 15520 Arc Rebellion Xyz Dragon | Majesty of the White Dragons 20606 |
| `0x1804427a0` | single | 1 | 141 | spellTrap action (slot 0) | 21465 Artmage Academic Arcane Arts Acropolis | Artmage Varnish -Alteration- 21469 |
| `0x180442ad0` | single | 1 | 258 | spellTrap action (slot 0) | 15368 Fossil Dragon Skullgar | Regenesis Commands 21488 |
| `0x180442da0` | single | 1 | 52 | handGrave action (slot 0) |  | Shipping Error 21490 |
| `0x180442830` | single | 1 | 309 | ignition action (slot 0) | 15576 Samsara Dragon, 15588 Dragunity Remus, 15608 | Spring 21484 |
| `0x180440b10` | single | 1 | 570 | spellTrap action (slot 0) | 4064 Kuriboh | A.I. Connect 21191 |
| `0x1802943d0` | single | 1 | 140 | monster condition (slot 2) | 21192 World of Spirits | Ancient Fairy Life Dragon 21176 |
| `0x180440a70` | single | 1 | 61 | monster action (slot 0) |  | Ancient Fairy Life Dragon 21176 |
| `0x180440d50` | single | 1 | 1285 | spellTrap action (slot 0) | 15428, 15576 Samsara Dragon, 15588 Dragunity Remus, 15608 | Gordian Slicer 21203 |
| `0x180441260` | single | 1 | 599 | spellTrap action (slot 0) | 15428, 15780 Chaos Grepher | Let's Go Together 21205 |
| `0x18057c200` | single | 1 | 1044 | ignition action (slot 0) | 15624 Live☆Twin Ki-sikil | Materiactor Meltthrough 21200 |
| `0x180441e60` | single | 1 | 1501 | spellTrap action (slot 0) | 4608 Penguin Soldier, 15428 | Mitsurugi Tempest 21353 |
| `0x18043b220` | single | 1 | 59 | ignition action (slot 0) |  | Alba System Dogmatikalamity 20469 |
| `0x180273ce0` | single | 1 | 274 | monster condition (slot 2) | 18845 EMERGENCY!, 21349 Ame no Habakiri no Mitsurugi | D/D Count Surveyor 21069 |
| `0x18043ff70` | single | 1 | 1590 | monster action (slot 0) | 7120 Magna-Slash Dragon, 7128 "A" Cell Breeding Device, 15576 Samsara Dragon, 15588 Dragunity Remus, 15608 | D/D Count Surveyor 21069 |
| `0x1803c6ea0` | single | 1 | 395 | monster action (slot 0) | 21071 D/D Extra Surveyor | D/D Extra Surveyor 21071 |
| `0x180294300` | single | 1 | 206 | spellTrap condition (slot 2) |  | Magia Magic - Thunder of Judgment 21075 |
| `0x180294220` | single | 1 | 223 | spellTrap condition (slot 2) |  | Magia Magic - Thunder of Judgment 21075 |
| `0x180215fc0` | single | 1 | 571 | spellTrap cost (slot 3) | 15516 Infernity Doom Archfiend | Magia Magic - Thunder of Judgment 21075 |
| `0x18043f0d0` | single | 1 | 1785 | handGrave action (slot 0) | 4064 Kuriboh, 4608 Penguin Soldier, 15588 Dragunity Remus, 15608, 16384 White Steed of the Floral Knights | The Fabled Behilmoth 20923 |
| `0x1802945f0` | single | 1 | 115 | ignition condition (slot 2) |  | Eidos the Underworld Monarch 21428 |
| `0x180442ec0` | single | 1 | 302 | monster action (slot 0) | 15792 | Psychic Omnibuster 21632 |
| `0x1802948b0` | single | 1 | 114 | monster condition (slot 2) |  | Psychic Omnibuster 21632 |
| `0x180442440` | single | 1 | 41 | monster action (slot 0) |  | Cooky☆Yummy 21366 |
| `0x1804425c0` | single | 1 | 319 | monster action (slot 0) | 15516 Infernity Doom Archfiend | Lollipo☆Yummy 21367 |
| `0x180294520` | single | 1 | 207 | spellTrap condition (slot 2) |  | Yummy☆Surprise 21374 |
| `0x180443440` | single | 1 | 48 | spellTrap action (slot 0) |  | Bot Herder 21748 |
| `0x1804436a0` | single | 1 | 272 | monster action (slot 0) |  | Marshmao☆Yummy 21790 |
| `0x18022ccf0` | single | 1 | 40 | monster condition (slot 2) |  | Megalith Anastasis 21832 |
| `0x180444ab0` | single | 1 | 607 | monster action (slot 0) |  | Silk Bomb Moth 22070 |
| `0x180280720` | single | 1 | 74 | spellTrap condition (slot 2) | 12944 Zefraath, 15388 Super All In! | Solfachord Happiness 21831 |
| `0x180274b90` | single | 1 | 1123 | monster condition (slot 2) | 11477 Yosenju Shinchu R, 11679 Performapal Lizardraw, 11784 Wisdom-Eye Magician, 12375 Performapal Parrotrio, 12482 Odd-Eyes Phantom Dragon ... | Enneacraft - Atori.MAR 21948 |
| `0x1804442e0` | single | 1 | 706 | monster action (slot 0) |  | Enneapolis 21950 |
| `0x180444840` | single | 1 | 11 | monster action (slot 0) |  | Enneapolis 21950 |
| `0x18038a630` | single | 1 | 597 | ignition action (slot 0) | 4608 Penguin Soldier, 15516 Infernity Doom Archfiend, 15792 | Hecahands Breus 21935 |
| `0x180443d50` | single | 1 | 1420 | ignition action (slot 0) | 15516 Infernity Doom Archfiend | Hecahands Xeno 21937 |
| `0x180444930` | single | 1 | 168 | handGrave action (slot 0) |  | Kewl Tune Cue 21956 |
| `0x180288070` | single | 1 | 64 | spellTrap condition (slot 2) | 18249, 22354 Spellbook of the Grand Circle | Spellbook of the Grand Circle 22354 |
| `0x180294bf0` | single | 1 | 220 | ignition condition (slot 2) |  | Dracotail Shaulas 22140 |
| `0x180294b30` | single | 1 | 187 | ignition condition (slot 2) |  | Elfnote Tinia 22117 |
| `0x1804459f0` | single | 1 | 111 | monster action (slot 0) |  | Exosister Betrayal 22168 |
| `0x180445b30` | single | 1 | 55 | spellTrap action (slot 0) |  | Furtive Techniques Fur Hire, Fur All Your Ultimate Moves 22177 |
| `0x1804465b0` | single | 1 | 100 | ignition action (slot 0) |  | GMX - VELOX 22341 |
| `0x180446620` | single | 1 | 421 | spellTrap action (slot 0) | 22934 GMX - COMPREX, 22935 Evolved Daneen | GMX Applied Experiment #55 22342 |
| `0x180446250` | single | 1 | 864 | monster action (slot 0) |  | GMX Associate Noma 22338 |
| `0x1801a2380` | single | 1 | 30 | monster target (slot 1) |  | Shichi-Go-San 22182 |
| `0x180445670` | single | 1 | 393 | ignition action (slot 0) | 15516 Infernity Doom Archfiend | Tellusion the Magna Warrior 22110 |
| `0x18028e830` | single | 1 | 557 | handGrave condition (slot 2) | 23434, 23435 | Throne of the Archfiends 22162 |
| `0x180445800` | single | 1 | 35 | ignition action (slot 0) |  | Zalen the Shackled Dragon 22145 |
| `0x180441980` | single | 1 | 357 | spellTrap action (slot 0) |  | Baneshade 21242 |
| `0x180441780` | single | 1 | 77 | ignition action (slot 0) |  | Shateki 21241 |
| `0x180441580` | single | 1 | 35 | handGrave action (slot 0) |  | Sloppy Stasher Squirrel 21239 |
| `0x180446ae0` | single | 1 | 156 | spellTrap action (slot 0) |  | Vortex of Time 22352 |
| `0x180294670` | single | 1 | 180 | spellTrap condition (slot 2) |  | Sky Striker Special Maneuver - Lemnisgate! 21480 |
| `0x18044a8e0` | single | 1 | 128 | ignition action (slot 0) |  | Anti-GMX Final Experiment 22938 |
| `0x1804479f0` | single | 1 | 263 | ignition action (slot 0) |  | Breachborrel Dragon 22558 |
| `0x180447b00` | single | 1 | 161 | spellTrap action (slot 0) | 15516 Infernity Doom Archfiend | Enneacraft Reset 22574 |
| `0x180447c00` | single | 1 | 2768 | spellTrap action (slot 0) | 4608 Penguin Soldier, 8137 Power Tool Dragon, 15516 Infernity Doom Archfiend, 15768 Dream Mirror Recap | Fifty Fifty? 22579 |
| `0x180447900` | single | 1 | 238 | handGrave action (slot 0) |  | Fydraulis Harmonia 22533 |
| `0x18044a280` | single | 1 | 73 | handGrave action (slot 0) |  | Genia of the Ring 22927 |
| `0x180294da0` | single | 1 | 192 | ignition condition (slot 2) |  | GMX Suppression Squad 22933 |
| `0x1804486d0` | single | 1 | 164 | spellTrap action (slot 0) |  | King's Resonance 22580 |
| `0x180295020` | single | 1 | 269 | spellTrap condition (slot 2) | 22580 King's Resonance | King's Resonance 22580 |
| `0x180447870` | single | 1 | 144 | monster action (slot 0) |  | Mimighoul Flower 22527 |
| `0x1801e2a90` | single | 1 | 191 | spellTrap target (slot 1) | 13975 Chaos Emperor, the Dragon of Armageddon, 22588 Solemn Accusation | Solemn Accusation 22588 |
| `0x180448dd0` | single | 1 | 415 | spellTrap action (slot 0) | 15794, 15802 | Subspace Battle 22687 |
| `0x18044a160` | single | 1 | 283 | ignition action (slot 0) | 15804 | VIP Whale 22926 |
| `0x180447000` | single | 1 | 285 | monster action (slot 0) |  | Black Jack the Shadow-Armored Knight 22501 |
| `0x1802594d0` | single | 1 | 0 | spellTrap condition (slot 2) |  | Champion's Pulse 22312 |
| `0x180445c80` | single | 1 | 389 | ignition action (slot 0) | 15516 Infernity Doom Archfiend | Dragunity Divine Wind 22200 |
| `0x1804477b0` | single | 1 | 180 | monster action (slot 0) |  | Early Palm Gets the Win 22503 |
| `0x180446df0` | single | 1 | 69 | monster action (slot 0) |  | Fallin' Cheatah 22500 |
| `0x180294ee0` | single | 1 | 146 | spellTrap condition (slot 2) |  | Pair Bear Scare!! 22502 |
| `0x1804474c0` | single | 1 | 67 | spellTrap action (slot 0) |  | Pair Bear Scare!! 22502 |
| `0x18044c500` | single | 1 | 1489 | ignition action (slot 0) | 9161 Gishki Chain | Invincible Demise Lord 23146 |
| `0x1801e6340` | single | 1 | 560 | ignition target (slot 1) | 17428 Gunkan Suship Shari Red, 18620, 20363, 23018 Blitzclique Alternator | Blitzclique Alternator 23018 |
| `0x180263b90` | single | 1 | 139 | monster condition (slot 2) |  | Celtic Mystic 22952 |
| `0x18044a960` | single | 1 | 356 | monster action (slot 0) |  | Celtic Mystic 22952 |
| `0x18044ab80` | single | 1 | 186 | ignition action (slot 0) |  | Fallen Paradise of the Sacred Beasts 22997 |
| `0x180295ec0` | single | 1 | 153 | ignition condition (slot 2) |  | Fallen Paradise of the Sacred Beasts 22997 |
| `0x180449b30` | single | 1 | 42 | ignition action (slot 0) |  | Possessed Destiny - Wynn 22829 |
| `0x18044ad50` | single | 1 | 1692 | spellTrap action (slot 0) |  | Sacred Spirit Sword Aiwass 23003 |
| `0x1802967c0` | single | 1 | 71 | ignition condition (slot 2) |  | Shamanite Shamanknight 23301 |
| `0x18044aad0` | single | 1 | 62 | handGrave action (slot 0) |  | Sorcerer of the Swamp 22973 |
| `0x18044cfb0` | single | 1 | 451 | monster action (slot 0) | 15516 Infernity Doom Archfiend | Witness of the Ancient 23296 |
| `0x18044ea00` | single | 1 | 360 | ignition action (slot 0) |  | Evil Lord - Zorc 23486 |
| `0x18044eb70` | single | 1 | 291 | spellTrap action (slot 0) | 15516 Infernity Doom Archfiend | Ancient Secrets 23497 |
| `0x1802708e0` | single | 1 | 69 | ignition condition (slot 2) |  | Orichalcos Sword of Sealing 22709 |
| `0x180295690` | single | 1 | 265 | spellTrap condition (slot 2) |  | Underworld Circle 22708 |
| `0x180449710` | single | 1 | 95 | ignition action (slot 0) | 22704 Astrograph Sorcerer, the Starfrost Magician | Astrograph Sorcerer, the Starfrost Magician 22704 |
| `0x18028e100` | single | 1 | 149 | monster condition (slot 2) | 18174 Meteor Rush - Monochroid | Number 39: Utopia, Emissary of Light 22699 |
| `0x1802971c0` | single | 1 | 90 | handGrave condition (slot 2) | 23548 Raise Moon Starter Squeeze | Raise Moon Starter Squeeze 23548 |
| `0x18044ef00` | single | 1 | 32 | spellTrap action (slot 0) |  | Zenet Cataract 23541 |
| `0x18044f0f0` | single | 1 | 124 | ignition action (slot 0) |  | Aroma Strategy 23581 |
| `0x18044f070` | single | 1 | 127 | monster action (slot 0) |  | Gilford the Lightning, the Warrior of Thunderbolts 23572 |
| `0x18044f6d0` | single | 1 | 429 | ignition action (slot 0) |  | Knight of Twin Swords 23584 |
| `0x18044ede0` | single | 1 | 40 | spellTrap action (slot 0) |  | Angelechy Verdict 23528 |
| `0x180296e40` | single | 1 | 31 | spellTrap condition (slot 2) |  | Angelechy Verdict 23528 |
| `0x18044d390` | single | 1 | 306 | monster action (slot 0) | 15428 | Ashtraja Susanoo 23361 |
| `0x18044e8b0` | single | 1 | 87 | handGrave action (slot 0) |  | Bingo! 23402 |
| `0x180296c50` | single | 1 | 130 | ignition condition (slot 2) |  | Garbage Golem 23355 |
| `0x18044df00` | single | 1 | 148 | handGrave action (slot 0); spellTrap action (slot 0) |  | Graceful & Skull Dice 23375 |
| `0x180406390` | single | 1 | 478 | monster action (slot 0) | 15344 Jinzo the Machine Menace, 15516 Infernity Doom Archfiend, 23325 Jinzo - Energy Shocker | Jinzo - Energy Shocker 23325 |
| `0x18044eca0` | single | 1 | 319 | spellTrap action (slot 0) | 15804 | Just Flip a Coin 23515 |
| `0x18044dc10` | single | 1 | 748 | monster action (slot 0) | 15768 Dream Mirror Recap, 15806 | Number 104: Masquerade V 23364 |
| `0x18044e2a0` | single | 1 | 1240 | ignition action (slot 0); monster action (slot 0) | 12547 Number 51: Finisher the Strong Arm, 14442 Servant of Endymion, 14447 Performapal Turn Trooper, 14688 The World Legacy, 17265 Forge a New Future ... | Reversal Box 23393 |
| `0x18044e780` | single | 1 | 300 | ignition action (slot 0) |  | Reversal Box 23393 |
| `0x180208ba0` | single | 1 | 53 | ignition cost (slot 3) |  | The Great Gallant Bandit 23349 |
