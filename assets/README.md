# Assets

Soluna ships with art from the Liberated Pixel Cup (LPC) family, which is licensed CC-BY-SA 3.0 / GPL 3.0 (much of it also OGA-BY 3.0). Those licences allow redistribution with credit, so the files live in this repository with their credits beside them.

`python tools/fetch_assets.py` downloads everything again from the original sources and rewrites the credits. Run it after changing the lists at the top of that script.

## What is here

- `characters/lpc/`: layers from the [Universal LPC Spritesheet Character Generator](https://github.com/LiberatedPixelCup/Universal-LPC-Spritesheet-Character-Generator): bodies, heads, hair, clothes and armour, one `walk.png` per layer (9 frames x 4 directions, 64 px). `catalog.json` holds each layer's z order, colour channels and the palettes; `CREDITS.md` lists the authors of every file used.
- `tilesets/lpc/`: the [LPC base tiles](https://opengameart.org/content/liberated-pixel-cup-lpc-base-assets-sprites-map-tiles) on a 32x32 grid. `CREDITS.TXT` names the author of each file.

## How characters are drawn

The client builds each character at runtime, paper-doll style: body, head and hair from what was picked at creation, then one layer per equipped item. Every layer is recoloured by swapping its base palette for the chosen variant (skin tone, hair colour, cloth or metal colour), then the layers are stacked by z order. Items in `data/items.json` name the layer (`sheet`) and its colours, so a new item is one line of JSON as long as its layer is in the catalog.

## Adding art

- More LPC layers: add their sheet definition paths to `SHEETS` in `tools/fetch_assets.py` and run it.
- Your own tilesets: any PNG on a 32x32 grid under `tilesets/` shows up in the editor (Tab cycles them).
- Art you may use but not redistribute (most itch.io packs, for example [Pipoya](https://pipoya.itch.io/pipoya-rpg-tileset-32x32)): put it under a `private/` folder, such as `tilesets/private/`. Those folders are ignored by git. Every player needs the same files for maps that use them.

RPG Maker's RTP and DLC art is licensed for RPG Maker games only and cannot be used here.
