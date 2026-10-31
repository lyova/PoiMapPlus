# POI Map Plus

**[Download on Nexus Mods](https://www.nexusmods.com/7daystodie/mods/12271)**

A client-side modlet for **7 Days to Die V 3.3 or newer** that turns the map into a useful planning tool:
every POI you have been to gets a marker, and the marker tells you whether its loot room has
already been emptied.

## What it does

- **Markers for every POI you have been to.** A marker appears the first time you walk into a
  POI, and stays from then on. Nothing is revealed ahead of time - POIs you have never entered
  stay invisible.
- **The skull says whether it is worth going.** Orange means the good loot is still there, green
  means it is gone.
- **The tier is printed on the marker.** `T1`-`T2` white, `T3` orange, `T4`+ red, so a glance at
  the map is enough.
- **A chest badge says whether the loot will come back.** Green chest: you left the container
  empty, so the game will restock it. Orange chest: something is still inside, which blocks the
  respawn for as long as it stays there.
- **Respawned loot turns the marker orange again**, on the day it actually happens - without you
  having to revisit the place. Badge and note go with it, so the POI reads as untouched, which
  by then it effectively is.
- **Hover for details.** The tooltip spells the state out: `cleared, looted` or
  `cleared, chest not empty`.
- **Greyed out markers stay grey.** Clicking a marker on the map greys it out, the same as any
  vanilla marker; the mod remembers that per POI, across map visits and restarts.
- **Party sharing.** With the mod on the server too, party members see each other's POIs, and
  each marker can name the teammate who found it first - see [Multiplayer](#multiplayer).
- **Settings on the map itself.** A gear in the bottom right corner opens a small popup:
  markers on or off, tier label on or off, chest badge on or off, marker size as S / M / L, and
  a Multiplayer group with `Show discoverer` and `Only my POIs`.
  Turning the markers off leaves the tracking running - the map just goes quiet.
- **Quest resets are handled.** Take a quest that resets a POI and its marker goes back to orange.

## How "cleared" is decided

The mod follows a ranked list of loot containers, ordered from the richest down to the most
modest. In every POI it picks the best ranked container that is actually there and watches that
one - lower ranks only matter when nothing better exists in that POI. Anything not on the list is
ignored entirely, so opening a nightstand on the way in marks nothing.

The list itself lives in `PoiMapPlus.xml` under `tracked_loot_lists`, where every entry carries a
`rank` - 1 being the richest. An entry can also carry `max_poi_tier`, which limits it to POIs up
to that tier: a weapons bag is the prize of a tier 1 house, but in a tier 5 dungeon it is noise,
so by default it only counts in tier 1. Look in the file for the current defaults; reordering
them or adding your own containers is a matter of editing it.

If a POI holds nothing from the list at all, it is marked cleared straight away - there is
nothing there worth coming back for.

Weapon bags vanish from the world once emptied. The mod records the rank at the moment such a
container is opened, so they still count. Nothing respawns in their place either, so those POIs
stay green.

## How respawned loot is spotted

The game restocks a container in `TEFeatureStorage.UpdateTick`, under conditions worth knowing:

- **Per container, not per POI, and not on a world-wide schedule.** Each one counts from its own
  `worldTimeTouched`, set when you first open it.
- **Only if it is empty.** Leave a single item inside - even your own junk - and that container
  is never restocked. This is what the chest badge is about.
- **Only while nobody is around.** Until the timer expires, a player within 16 blocks pushes it
  forward, which is why the option is described as applying to "unvisited areas".
- **Only when the chunk is loaded.** The flag actually flips as you come back into range.

So the mod does not scan anything on a timer. At the last scan of a POI it stores one timestamp
and one flag - whether the watched container was left empty - and from those the respawn date is
arithmetic. Opening the map costs two integer comparisons per marker, which is why an explored
map with hundreds of markers does not stutter.

The prediction can be a few in-game hours early if you camped in the POI after looting it, since
the game kept pushing the timer while you were there. Walking back in corrects the record either
way, and the marker is right by the time it matters.

The whole thing needs the world's **Loot Respawn Time** option to be on. With it disabled the
chest badge still works, the marker simply never goes back to orange.

## Requirements

- 7 Days to Die **V 3.3 or newer**. Built against V 3.4.0 (b9).
- Launch **without EasyAntiCheat** - the mod ships a DLL, and EAC blocks those. Start the game
  from `7DaysToDie.exe`, or pick the non-EAC option in the Steam launcher.
- Single player, and multiplayer clients - see below.

## Multiplayer

Play with friends and the map becomes the party's map.

- **See where your party has been.** Every POI a teammate visits shows up on your map, and yours
  on theirs. Nothing to set up - join a party and it just works.
- **Loot state is shared too.** A teammate empties the chest - your marker turns green as well.
- **Markers stay.** A teammate's POIs remain on your map after they log off or leave the party,
  and survive restarts.
- **Who found it.** `Show discoverer` puts the name of the teammate who found a POI first under
  the skull. The tooltip always says who it was - or that it was you.
- **Going solo again.** Everything you learned in the party stays yours. `Only my POIs` hides the
  places only your teammates have been to; untick it and they come back.
- **No free intel.** Only chests you or your party opened count. A stranger emptying a POI does
  not show up on your map - you find out when you open the chest, like everyone else.
- **Greying out stays personal.** A marker you grey out is greyed out for you only.

**Setup.** On its own the mod is client-side: only you install it, and the server and the other
players do not need it. The party features need it on the **server** as well, and once the
server has it, **every player on it needs the mod too** - the game turns away a client without it.

Multiplayer has had less testing than single player - feedback welcome.

### Under the hood

- Party records travel in a network package of the mod's own, which the server forwards to the
  sender's party. That is why the server needs the mod, and why the game's package check at
  connect then requires it from every client.
- The game remembers that a container was opened, not by whom. So in multiplayer the mod keeps
  its own list of containers you opened (`PoiMapPlus.opened.csv`), and only those count; what the
  party tells you is written into your own record and stays after you leave it. In single player
  the game's own flag is used as it is.
- Where you and a teammate both have a record of a POI, knowing it is looted beats not knowing,
  an emptied chest beats one with something left in it, and between equals the latest look wins.
  Out of a party a POI you have a record of is drawn from that record alone.
- Teammates' markers are kept in `PoiMapPlus.party.csv`. Players are told apart by a key each
  player's mod makes up once and keeps in `PoiMapPlus.ui`, not by name or entity id - two players
  may share a name, and a client can come back to a server as a new entity.
- Playing as a client on someone else's server, the mod's files live in the game's `SavesLocal`
  folder for that server.

## Compatibility

- Hooks vanilla methods with Harmony postfixes (`XUiC_MapArea`, including the private
  `onMapPressedLeft` for greyed out markers, `LootManager.LootContainerOpened`,
  `XUiC_LootWindow.OnClose`, `EntityPlayer.onNewPrefabEntered`,
  `PrefabInstance.ResetBlocksAndRebuild`) and appends one nav object class through XPath, so no
  vanilla config file is overwritten.
- Both marker sprites are referenced by name out of the game's shared `UIAtlas`, so the mod ships
  no artwork and cannot drift out of sync with a UI overhaul that restyles that atlas.
- UI overhauls that only restyle the map window are fine. A mod replacing the map controller with
  its own class would leave the markers unhooked.
- Containers from other mods are supported: add their loot lists to `tracked_loot_lists`.
- Large overhauls such as Darkness Falls or Undead Legacy rework loot entirely, so the default
  ranking needs adjusting in `PoiMapPlus.xml`.

## Installation

1. Extract the archive into `<game folder>\Mods\` so you end up with
   `<game folder>\Mods\PoiMapPlus\ModInfo.xml`.
2. Start the game without EAC.

Vortex and the Mod Launcher handle the archive as a normal modlet.

## Configuration

`PoiMapPlus.xml` next to `ModInfo.xml` holds every setting, with comments. The interesting ones:

| setting | default | meaning |
|---------|---------|---------|
| `MinTier` | `1` | lowest POI difficulty tier that gets a marker; `0` shows sheds too |
| `OnlyDiscovered` | `true` | `false` reveals every POI in the world from the start |
| `MaxMarkers` | `600` | cap on simultaneous markers, nearest to the player win |
| `IconScale` | `1` | starting marker size; the popup's S/M/L writes over it |
| `IconScaleSmall/Medium/Large` | `1` / `1.4` / `2` | what S, M and L mean |
| `BadgeMaxZoom` | `1.4` | zoom past which only the skull is drawn |
| `ChestSprite` | `ui_game_symbol_treasure` | any sprite name from the game's `UIAtlas` |
| `TierMid` | `3` | tier that turns orange; below it white, above it red |
| `Verbose` | `false` | log every container opened inside a POI, for troubleshooting |
| `tracked_loot_lists` | see above | which containers count, and in what order |

Container names are LootLists from `Data/Config/loot.xml`, not block names.

The badge layout - `ChestScale`, `ChestOffsetX/Y`, `TierScale`, `TierOffsetX/Y`, and
`FinderScale`, `FinderOffsetY` for the teammate name under the skull - is in fractions
of the current icon size, measured from the centre of the skull with X right and Y up, so it
holds at every zoom level. All of it is re-read by `poimap reload`, so it can be dialled in with
the map open.

Sizes here are UI units rather than screen pixels: `UIRoot` scales the interface to your
resolution on top of them. `BadgeMaxZoom` is a zoom level rather than a size, so the cutoff it
works out to follows whichever size preset is active - switching S/M/L does not quietly move it.
`poimap status` prints both the cutoff and the size the map last drew.

What the popup changes is remembered per save, in `PoiMapPlus.ui` next to the POI state, and it
overrides the starting values from this file. In single player and on the host that is the world
save folder; a client on someone else's server keeps them in the game's `SavesLocal` folder for
that server.

The config is re-read on every world load, so a trip through the main menu is enough. Or use the
console:

```
poimap reload    re-read the config and refresh markers
poimap rescan    reload, forget learned loot state, rescan from scratch
poimap status    show tracked lists, POI counts and the party sharing state
poimap here      dump containers and stored state for the POI you are standing in
poimap respawn   backdate the emptied chest here so its respawn is already due, for
                 testing the marker without waiting; "poimap respawn all" does the lot
poimap verbose   toggle the per-container logging
poimap navdump   list nav objects by class and tracking type
```

## Localization

`Config/Localization.csv` covers every language the game ships with except Russian: English, German, Spanish,
French, Italian, Japanese, Korean, Polish, Brazilian Portuguese, Turkish, Simplified and
Traditional Chinese, plus a Ukrainian column. Adding or fixing a translation is one cell in that
file - no rebuild needed.

## Building

Needs the .NET SDK 8.0 or newer. Game assemblies are referenced straight from the install
directory; adjust `GameDir` in `Directory.Build.props` if yours differs.

```powershell
.\build.ps1
```

The build deploys the mod into the game's `Mods` folder and packs an archive into `dist\`.

## License

MIT - see [LICENSE](LICENSE). Free to fork, modify and bundle into mod packs; credit is
appreciated.
