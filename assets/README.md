# Assets

Everything in this folder except this file is ignored by git. Most free art packs allow use in games but not redistribution of the files themselves, and a public repository counts as redistribution.

## Folders

- `tilesets/*.png`: tilesets on a 32x32 grid. The editor lists every PNG here (Tab cycles them) and adds the ones you paint with to the map. Each map stores tileset file names, so every player needs the same files.
- `characters/{n}.png`: character sheets in RPG Maker layout, 3 columns (step, stand, step) by 4 rows (down, left, right, up). Sprite numbers start at 0; the server currently hands out 0 to 5 at random.
- `fonts/*.ttf`: optional UI font. Without one the client uses a system font.

The palette shows up to 8 tiles across (256 px), which fits the packs below.

## Where to get free art

RPG Maker's RTP and its "free" DLC packs are licensed for games made with RPG Maker only. They cannot be used in Soluna, even for a non-commercial game.

These work instead:

- [Pipoya RPG Tileset 32x32](https://pipoya.itch.io/pipoya-rpg-tileset-32x32): RPG Maker look, 32x32, free for commercial and personal use, editing allowed, redistribution of the files not allowed
- [Pipoya Free RPG World Tileset](https://pipoya.itch.io/pipoya-free-rpg-world-tileset-32x32-40x40-48x48): same terms, take the 32x32 version
- [Liberated Pixel Cup (OpenGameArt)](https://opengameart.org/content/liberated-pixel-cup-lpc-base-assets-sprites-map-tiles): CC-BY-SA / GPL, credit the artists
- [Kenney](https://kenney.nl/assets): CC0

Read the licence that ships with each pack and keep its credit file next to the PNGs.
