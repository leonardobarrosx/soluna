# Soluna

A 2D top-down MMORPG engine in C#. Client, server, accounts with saved characters, paper-doll equipment and an in-game map editor, dark UI.

Soluna follows the shape of [Crystalshire](https://github.com/RobinPerris/Crystalshire), Robin Perris's VB6 engine from the Eclipse ORPG family: a 32x32 tile grid, five map layers, tile-by-tile movement and editors that live inside the game. It is a rewrite, not a port, and none of the VB6 code is carried over.

![Login](docs/login.png)

![Character select](docs/select.png)

![Character creation](docs/creation.png)

![Night mode](docs/night.png)

![Equipment](docs/equipment.png)

![Map editor](docs/editor.png)

## What works today

- Authoritative UDP server (LiteNetLib). Clients move straight away for responsiveness; the server checks each step against the map and a movement budget, and snaps a client back when it disagrees
- MonoGame client: layered tile renderer, characters sorted by depth, smooth tile-to-tile walking, camera that follows and clamps to the map, zoom
- Accounts: register and log in, passwords stored only as salted PBKDF2-SHA256 hashes, one session per account
- Up to three characters per account, Crystalshire style, on a select screen; create, play or delete
- Characters are saved on logout and every minute: position, look, equipment and inventory survive a server restart
- Character creation: name, body, skin tone, hair style, hair colour and eyes, with a live walking preview
- Chibi paper-doll characters in RPG Maker style (32x32, 3 frames x 4 directions): body, eyes, hair and every equipped item are separate layers, painted in their colour and stacked at runtime, so changing equipment changes how the character looks to everyone on the map
- Equipment panel (I): click an item to wear it or take it off. Items are plain JSON in `data/items.json`, sent by the server at login; new characters get the ones marked `starter`
- Access levels: the first account created on a server is admin and can edit maps (F1) and use `/item <id>`, `/ir <mapa> [x y]`, `/trazer <nome>`, `/novomapa <largura> <altura> [nome]` `/mapa` (nome, pvp, seguro, spawn, musica, link cima|baixo|esquerda|direita <id>) and `/npc <id>|remover`; everyone has `/online`, `/mapas`, `/itens`, `/npcs` and `/status`
- `--all-items` on the server gives new characters the whole wardrobe, for trying art out
- LPC tiles with autotiling for terrain edges and corners; the starter map is a glade with paths, a pond and a pine forest
- Night mode on by default: the world sits under a dark tint with a soft light around your character (F3 toggles it)
- Chat, join and leave messages, player names over heads
- A world of linked maps: walking off an edge with a link carries on into the next map, warp tiles teleport, and each map has a name, a spawn point, PvP or safe, and music. With Pipoya's village, a forest north of it is generated as map 2 and linked to the village's north road
- Game editor (F2, admins): items (name, slot, look picked from the art set with a preview on your own character, colour, attack, defence, price, starter) and NPCs (name, sprite picked from the game's sheets with an animated preview, behaviour, health, attack, defence, experience, range, speeds, respawn). Saving sends them to the server, which validates them, writes the game's files and pushes them to everyone online; NPCs already on the maps take the new values at once
- Tile types as in Crystalshire: blocked, warp, NPC-avoid and heal (heal acts once there are vitals)
- Map cache: clients keep maps in `cache/` and download one again only when its revision changes
- Fixed 20 Hz server tick for everything that happens over time: NPC thinking, respawns, regeneration, autosave
- Combat: HP, MP, level and experience (formulas in `Soluna.Shared/Combat.cs`), attack with Space or Ctrl at whatever is in front, damage from level plus the attack and defence of what you wear, floating numbers, health bars, a lunge on every swing, death and waking up in the village, regeneration out of combat and on heal tiles, PvP only on maps marked for it
- NPCs from `data/npcs.json` (sprite sheet or paper-doll look, friendly, passive or aggressive, hp, attack, defence, experience, range, speed, respawn time) placed per map; the server walks, chases and attacks with them. With Pipoya's sprite pack the importer adds villagers, guards, animals, blobs, goblins, skeletons, ghosts and a lich, and the village and forest get populated on first run
- HUD with health, mana and experience
- In-game map editor (F1) with four modes: Tiles (palette, layers 1 to 9), Attributes (blocked, warp, NPC-avoid, heal; warp destination picked in fields or set to where you stand), NPCs (click to place the chosen NPC, right click to remove; spawns show as faded sprites) and Map (name, PvP, music, links on each edge, start point, going to another map, creating a new one). Ctrl+S or the button saves; the server checks that links, warps and NPCs point at things that exist, and everyone on the map reloads it with its NPCs respawned
- Importing art (admins): drag a PNG onto the game window and a dialog shows it, guesses whether it is a tileset (sides in multiples of 32) or a character sprite (3 frames by 4 directions), and asks for a name and whether its license allows sharing it publicly. The server checks it, saves it in the game (`assets/tilesets/imported` or `assets/characters/sprites`, under a `private` folder unless it may be shared) and tells everyone; it shows up at once in the map editor's palette or the NPC editor's sprite list
- Game files reach players by themselves: at login the server sends the list of the game's files with their hashes, and the client downloads what it lacks or has an older copy of before entering the world (raw packs in `assets/sources` stay on the server)
- Tiled import: `dotnet run --project src/Soluna.Server -- import map.tmx <id> [name]` turns a Tiled map (CSV or base64, embedded or external tilesets under `assets/tilesets`) into a Soluna map, keeping every layer, moving layers named like `*_up`, `roof` or `tree` (or with an `above` property) over the characters, and working out collision from `collision` layers or, failing that, from water, buildings and trees
- With the free [Pipoya RPG Tileset](https://pipoya.itch.io/pipoya-rpg-tileset-32x32) extracted into `assets/tilesets/private/`, the server imports Pipoya's sample village as the start map on first run (into `data/maps/private`, which git ignores, since the pack may not be redistributed)
- Character art sets: creation choices (bodies, races, skins, hair, beards, eyes) come from the set's `options.json`, so a set can be swapped without code changes. With [Pipoya's Character Sprite 32 Generator](https://pipoya.itch.io/pipoya-free-rpg-character-sprites-32x32) extracted into `assets/sources/private/`, `python tools/import_nekonin.py` builds the NEKONIN cat-folk set (17 furs, tail styles, whiskers, hats, glasses, capes, wings, the human set's weapons) with cat-folk villagers, and `python tools/import_pipoya_characters.py` the human set (elf, cat and dog races, beards, hats, weapons, capes); whichever ran last is the active one (`assets/characters/private/active`), and its items and NPCs go to `data/private`. All of it stays out of git
- Falls back to placeholder art painted in code if the asset folders are missing

## Games

The engine holds several games. Each one is a folder under `games/` with its own `data/` (maps, items, NPCs, accounts) and `assets/` (tilesets, characters, fonts); `games/soluna` is the one that ships. Pick one with `--game <name>` on the server and the client, and start a new one from an existing game with `dotnet run --project src/Soluna.Server -- --new-game <name> [--from soluna]`: maps, items, NPCs and the art that may be shared are copied, private art and accounts are not. Below, `data/` and `assets/` mean the game's folders.

## Run it

Needs the .NET 10 SDK.

```bash
dotnet run --project src/Soluna.Server
```

```bash
dotnet run --project src/Soluna.Client -- --name Leo
```

Create an account on the login screen; the first one on a fresh server becomes admin. Open a second client to see two players.

Client options: `--host`, `--port`, `--user` and `--password` (log in on start), `--play` (also enter with the first character, creating the account and a random character if needed). For testing: `--walk --name X` (a local test account that wanders on its own), `--editor` and `--inventory` (open those panels on entry), `--import file.png` (open the import dialog as if the file had been dropped) and `--screenshot file.png` (save a frame after a few seconds, then quit).

## Controls

| Key | Action |
| --- | --- |
| Arrows / WASD | Walk |
| Enter | Chat |
| Space / Ctrl | Attack |
| I | Equipment |
| F1 | Map editor (tiles, attributes, NPCs, map) |
| F2 | Game editor (items, NPCs) |
| F3 | Night on or off |
| + / - or mouse wheel | Zoom |

At character creation: up and down pick a row, left and right change it, R randomises, Enter goes in.

In the editor: 1 to 5 picks the layer (Ground, Mask, Mask2 under characters; Fringe, Fringe2 over them), B switches to painting blocked tiles, Tab and Shift+Tab cycle tilesets, left click paints, right click erases, Ctrl+S saves.

## Art

Characters are the layers of IndigoFenix's BoundWorlds Character Maker (CC-BY 4.0), extracted by `tools/extract_chibi.py`. Tiles are the LPC base tiles (CC-BY-SA 3.0 / GPL 3.0), fetched by `tools/fetch_assets.py`. Both ship in `assets/` with their credits. See [games/soluna/assets/README.md](games/soluna/assets/README.md) for how the paper doll works and how to add art, including packs that cannot be redistributed. RPG Maker's RTP and DLC art is licensed for RPG Maker games only, so it is not used.

## Layout

```
src/Soluna.Shared        protocol, map format, game paths, constants
src/Soluna.Server        console server
src/Soluna.Client        MonoGame client, renderer, UI, editors
games/<name>/data        maps, items.json, npcs.json, accounts (not committed); private/ for content using private art
games/<name>/assets      tilesets, character art and credits; private/ folders for art that may not be shared
tools                    asset and art-set import scripts
cache/<name>             maps the client keeps between sessions
```

## Next

Everything a game creator does is meant to happen inside the engine, in the in-game editors (admin only), never by editing files or running scripts. Done: projects, the editor foundation (widgets, saving content on the server and pushing it live), the item and NPC editors, the map editor with attributes, NPC placement and map properties, and importing tilesets and sprites by dropping them on the window, with the server handing game files to clients. Next, in order: a launcher in the client to create a game or pick one and host it locally; editors for character art and animations. Then, each with its editor from the start: the economy (inventory slots and stacks, potions, drops, gold, shops), encryption for the login (LiteNetLib sends it in the clear, fine on a LAN, not on the internet), items that drop and trade, an autotile brush in the editor, animated tiles, skills and spells, ranged attacks, then the card duel system that the VB6 version started.

## Credits

Architecture inspired by Crystalshire by Robin Perris (MIT). Characters by IndigoFenix, tiles by the LPC contributors, credited in `assets/`. Built with [MonoGame](https://monogame.net), [LiteNetLib](https://github.com/RevenantX/LiteNetLib) and [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp).

## License

Code: MIT, see [LICENSE](LICENSE). Art in `assets/`: the licences listed with it.
