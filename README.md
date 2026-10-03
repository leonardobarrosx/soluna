# Soluna

A 2D top-down MMORPG engine in C#. Client, server, character creation, paper-doll equipment and an in-game map editor, dark UI.

Soluna follows the shape of [Crystalshire](https://github.com/RobinPerris/Crystalshire), Robin Perris's VB6 engine from the Eclipse ORPG family: a 32x32 tile grid, five map layers, tile-by-tile movement and editors that live inside the game. It is a rewrite, not a port, and none of the VB6 code is carried over.

![Character creation](docs/creation.png)

![Night mode](docs/night.png)

![Equipment](docs/equipment.png)

![Map editor](docs/editor.png)

## What works today

- Authoritative UDP server (LiteNetLib). Clients move straight away for responsiveness; the server checks each step against the map and a movement budget, and snaps a client back when it disagrees
- MonoGame client: layered tile renderer, characters sorted by depth, smooth tile-to-tile walking, camera that follows and clamps to the map, zoom
- Character creation: name, body, skin tone, hair style, hair colour and eyes, with a live walking preview
- Paper-doll characters: body, head, hair and every equipped item are separate LPC layers, recoloured by palette and stacked at runtime, so changing equipment changes how the character looks to everyone on the map
- Equipment panel (I): click an item to wear it or take it off; items are plain JSON in `data/items.json`
- LPC tiles with autotiling for terrain edges and corners; the starter map is a glade with paths, a pond and a pine forest
- Night mode on by default: the world sits under a dark tint with a soft light around your character (F3 toggles it)
- Chat, join and leave messages, player names over heads
- In-game map editor (F1): tile palette, five layers, blocked tiles, Ctrl+S saves to the server and every other player on the map receives the change
- Falls back to placeholder art painted in code if the asset folders are missing

## Run it

Needs the .NET 10 SDK.

```bash
dotnet run --project src/Soluna.Server
```

```bash
dotnet run --project src/Soluna.Client -- --name Leo
```

Open a second client to see two players. Client options: `--host`, `--port`, `--name`, plus a few for testing: `--walk` (random look, wanders on its own), `--skip-creation`, `--editor` and `--inventory` (open those panels on entry) and `--screenshot file.png` (save a frame after a few seconds, then quit).

## Controls

| Key | Action |
| --- | --- |
| Arrows / WASD | Walk |
| Enter | Chat |
| I | Equipment |
| F1 | Map editor |
| F3 | Night on or off |
| + / - or mouse wheel | Zoom |

At character creation: up and down pick a row, left and right change it, R randomises, Enter goes in.

In the editor: 1 to 5 picks the layer (Ground, Mask, Mask2 under characters; Fringe, Fringe2 over them), B switches to painting blocked tiles, Tab and Shift+Tab cycle tilesets, left click paints, right click erases, Ctrl+S saves.

## Art

Characters and tiles come from the Liberated Pixel Cup projects (CC-BY-SA 3.0 / GPL 3.0, much of it also OGA-BY 3.0) and ship in `assets/` with their credits. `tools/fetch_assets.py` downloads them again from the sources. See [assets/README.md](assets/README.md) for how the paper doll works and how to add art, including packs that cannot be redistributed. RPG Maker's RTP and DLC art is licensed for RPG Maker games only, so it is not used.

## Layout

```
src/Soluna.Shared   protocol, map format, constants
src/Soluna.Server   console server, map storage (data/maps/*.json)
src/Soluna.Client   MonoGame client, renderer, UI, editor
data/maps           maps, edited in game
data/items.json     items and the layers they wear
assets              LPC art and credits
tools               asset download script
```

## Next

Accounts and saved characters, items that drop and trade instead of a starter wardrobe, an autotile brush in the editor, map warps and several maps, NPCs and combat (the LPC slash and spellcast animations are ready to fetch), then the card duel system that the VB6 version started.

## Credits

Architecture inspired by Crystalshire by Robin Perris (MIT). Art by the LPC contributors, credited per file in `assets/`. Built with [MonoGame](https://monogame.net), [LiteNetLib](https://github.com/RevenantX/LiteNetLib) and [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp).

## License

Code: MIT, see [LICENSE](LICENSE). Art in `assets/`: the licences listed with it.
