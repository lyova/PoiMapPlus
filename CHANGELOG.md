# Changelog

Nexus used to number this mod after the game version: 1.1.0 went up there as 3.3, 1.0.0 as 3.2.
From 1.1.1 on the numbers are the same in both places.

## 1.2.0

A marker greyed out with a click on the map stays grey after a restart.
Party members see each other's POIs and their loot state on the map.
A teammate's markers stay after they log off or leave the party.
What the party told you about a POI stays after you leave the party.
New Multiplayer group in the map settings.
Show discoverer puts the name of whoever found a POI first under its marker.
The tooltip says who discovered the POI, or Discovered by you for your own finds.
Only my POIs hides the POIs only your teammates have been to. Off by default.
Party features need the mod on the server, and a server with it requires it from every player.
Without the mod on the server it stays client side, as before.
Fixed nothing being saved when playing as a client on someone else's server.
POI state file bumped to v5. Older versions of the mod still read it.

## 1.1.1

Updated for 7 Days to Die V 3.3. Does not run on 3.2 or older, stay on 1.1.0 there.
Removed the Russian translation.

## 1.1.0

Cleared POIs turn orange again once the game respawns their loot.
Chest badge on the marker: green when left empty and the loot will come back.
Orange chest badge: something was left inside, which blocks the respawn.
Tier printed on the marker, coloured by difficulty.
Settings popup behind a gear on the map: markers, tier label, chest badge, marker size.
Tooltip says what happened to the POI, not just its name.
Badges are dropped once the map is zoomed out far enough that they stop being readable.
poimap respawn fakes a due respawn, for testing the marker.
Fixed the map losing every icon for a frame when a tracked transform was destroyed.
Fixed the marker lagging a step behind after emptying a chest.
Removed TopContainerCount: it only ever watched one container.
POI state file bumped to v4, older files are upgraded in place.

## 1.0.0

First public release. Built and tested against 7 Days to Die V 3.2.0 (b9).
Map markers for every discovered POI, orange while unlooted, green once cleared.
Hover tooltip with POI name and difficulty tier, no permanent labels on the map.
Cleared detection driven by a ranked list of loot containers, configurable in PoiMapPlus.xml.
Containers that vanish when emptied (weapon bags, ammo piles) are tracked when opened.
Quest driven POI resets clear the marker again.
Console commands: poimap reload, rescan, status, navdump.
Localization for every language the game ships with, plus Ukrainian.
