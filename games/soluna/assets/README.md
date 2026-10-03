# Assets

Everything here may be redistributed with credit, so it lives in the repository with the credits beside it.

## What is here

- `characters/chibi/`: the layers of the [32x32 BoundWorlds Character Maker](https://opengameart.org/content/32x32-boundworlds-character-maker-paintnet) by IndigoFenix (CC-BY 4.0), one PNG per layer in RPG Maker layout (3 frames x 4 directions, 32 px): two bodies, eyes, hairstyles, clothes and accessories. `source/npc_builder.pdn` is the original Paint.NET file; `tools/extract_chibi.py` turns it into the PNGs and `catalog.json` (file and z order of each layer).
- `tilesets/lpc/`: the [LPC base tiles](https://opengameart.org/content/liberated-pixel-cup-lpc-base-assets-sprites-map-tiles) on a 32x32 grid (CC-BY-SA 3.0 / GPL 3.0). `CREDITS.TXT` names the author of each file; `tools/fetch_assets.py` downloads them again.

## How characters are drawn

The client builds each character at runtime, paper-doll style: body, eyes and hair from what was picked at creation, then one layer per equipped item. Layers that get a colour are colorized: the layer's typical brightness becomes the chosen colour, darker pixels keep their shading and outlines, lighter ones move part of the way to white. Pieces that differ between bodies exist as `<id>_m` and `<id>_f`; everything else fits both. Items in `data/items.json` name the layer (`sheet`) and its colour, so a new item is one line of JSON.

## Adding art

- New character pieces: draw a 96x128 layer on the same grid (it helps to start from `source/npc_builder.pdn`), add it to `catalog.json` with a z order, and give it an item.
- Your own tilesets: any PNG on a 32x32 grid under `tilesets/` shows up in the editor (Tab cycles them).
- Art you may use but not redistribute (most itch.io packs, for example [Pipoya](https://pipoya.itch.io/pipoya-rpg-tileset-32x32)): put it under a `private/` folder, such as `tilesets/private/`. Those folders are ignored by git. Every player needs the same files for maps that use them.

RPG Maker's RTP and DLC art is licensed for RPG Maker games only and cannot be used here.
