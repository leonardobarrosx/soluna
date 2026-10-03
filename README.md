# Soluna

A 2D top-down MMORPG engine in C#. Client, server and in-game map editor, dark UI.

Soluna follows the shape of [Crystalshire](https://github.com/RobinPerris/Crystalshire), Robin Perris's VB6 engine from the Eclipse ORPG family: a 32x32 tile grid, five map layers, tile-by-tile movement and editors that live inside the game. It is a rewrite, not a port, and none of the VB6 code is carried over.

![Night mode](docs/night.png)

![Map editor](docs/editor.png)

## What works today

- Authoritative UDP server (LiteNetLib). Clients move straight away for responsiveness; the server checks each step against the map and a movement budget, and snaps a client back when it disagrees
- MonoGame client: layered tile renderer, characters sorted by depth, smooth tile-to-tile walking, camera that follows and clamps to the map, zoom
- Night mode on by default: the world sits under a dark tint with a soft light around your character (F3 toggles it)
- Chat, join and leave messages, player names over heads
- In-game map editor (F1): tile palette, five layers, blocked tiles, Ctrl+S saves to the server and every other player on the map receives the change
- Runs with no art files at all: the client paints a placeholder tileset and characters in code

## Run it

Needs the .NET 10 SDK.

```bash
dotnet run --project src/Soluna.Server
```

```bash
dotnet run --project src/Soluna.Client -- --name Leo
```

Open a second client with another name to see two players. Client options: `--host`, `--port`, `--name`, plus `--walk` (wander on its own), `--editor` (open the editor on entry) and `--screenshot file.png` (save a frame after a few seconds, then quit), which are there for testing.

## Controls

| Key | Action |
| --- | --- |
| Arrows / WASD | Walk |
| Enter | Chat |
| F1 | Map editor |
| F3 | Night on or off |
| + / - or mouse wheel | Zoom |

In the editor: 1 to 5 picks the layer (Ground, Mask, Mask2 under characters; Fringe, Fringe2 over them), B switches to painting blocked tiles, Tab cycles tilesets, left click paints, right click erases, Ctrl+S saves.

## Art

See [assets/README.md](assets/README.md). Short version: drop 32x32 tilesets into `assets/tilesets` and RPG Maker style 3x4 character sheets into `assets/characters`. RPG Maker's own RTP and DLC art is licensed for RPG Maker games only, so it cannot ship with this engine; the README there lists free packs that can.

## Layout

```
src/Soluna.Shared   protocol, map format, constants
src/Soluna.Server   console server, map storage (data/maps/*.json)
src/Soluna.Client   MonoGame client, renderer, UI, editor
data/maps           maps, edited in game
assets              your art (not committed)
```

## Next

Accounts and saved characters, map warps and several maps, NPCs and combat, items and inventory, then the card duel system that the VB6 version started.

## Credits

Architecture inspired by Crystalshire by Robin Perris (MIT). Built with [MonoGame](https://monogame.net), [LiteNetLib](https://github.com/RevenantX/LiteNetLib) and [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp).

## License

MIT, see [LICENSE](LICENSE).
