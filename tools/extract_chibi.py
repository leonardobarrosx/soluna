"""
Extracts the chibi character layers from the BoundWorlds Character Maker (a Paint.NET file)
into one PNG per layer, plus the catalog the client reads.

Source: "32x32 BoundWorlds Character Maker (Paint.NET)" by IndigoFenix, CC-BY 4.0 / 3.0,
https://opengameart.org/content/32x32-boundworlds-character-maker-paintnet

Every layer is a 96x128 sheet in RPG Maker layout (3 frames x 4 directions, 32 px).
The client recolours layers at runtime, so the catalog only says how each layer is used.

Usage: pip install pypdn, then python tools/extract_chibi.py
"""

import io
import json
import pathlib
import urllib.request
import zipfile

import pypdn
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "assets" / "characters" / "chibi"
SOURCE = OUT / "source" / "npc_builder.pdn"
URL = "https://opengameart.org/sites/default/files/npc_builder_1.zip"

# Paint.NET layer name -> (sheet id, z order). Body-specific pieces share an id with a
# suffix the client resolves: "_f" for the female body, "_m" for the male one.
LAYERS = {
    "body_f": ("body_f", 0),
    "body_m": ("body_m", 0),
    "cheeks": ("cheeks", 1),
    "eyes_1": ("eyes_blue", 2),
    "eyes_2": ("eyes_brown", 2),
    "eyes_3": ("eyes_green", 2),
    "eyes_4": ("eyes_dark", 2),
    "pants_f": ("pants", 10),
    "shirt_sleeveless_f": ("sleeveless", 20),
    "robes_f": ("robe", 21),
    "dress_f": ("dress", 22),
    "bigrobes_f": ("mantle_f", 23),
    "bigrobes_m": ("mantle_m", 23),
    "armor_m": ("armor", 24),
    "armbands_f": ("armbands", 30),
    "pearl_necklace": ("necklace", 31),
    "amulet": ("amulet", 32),
    "hairhero": ("hair_hero", 40),
    "hairpony": ("hair_ponytail", 40),
    "hairfmed": ("hair_medium", 40),
    "hair_straight": ("hair_straight", 40),
    "hair_messy": ("hair_messy", 40),
    "hairchopstics": ("hair_sticks", 41),
}


def main():
    if not SOURCE.exists():
        SOURCE.parent.mkdir(parents=True, exist_ok=True)
        with urllib.request.urlopen(URL, timeout=60) as response:
            archive = zipfile.ZipFile(io.BytesIO(response.read()))
        SOURCE.write_bytes(archive.read("npc_builder.pdn"))

    image = pypdn.read(str(SOURCE))
    sheets = {}
    for layer in image.layers:
        if layer.name not in LAYERS:
            print("skipped layer", layer.name)
            continue
        sheet_id, z = LAYERS[layer.name]
        Image.fromarray(layer.image).convert("RGBA").save(OUT / f"{sheet_id}.png")
        sheets[sheet_id] = {"file": f"{sheet_id}.png", "z": z}
        print(sheet_id)

    catalog = {"frame": 32, "layout": "rpgmaker", "sheets": dict(sorted(sheets.items()))}
    (OUT / "catalog.json").write_text(json.dumps(catalog, indent=1) + "\n", encoding="utf-8", newline="\n")
    (OUT / "CREDITS.md").write_text(
        "# Chibi character layers\n\n"
        "From \"32x32 BoundWorlds Character Maker (Paint.NET)\" by IndigoFenix, licensed CC-BY 4.0 (also CC-BY 3.0):\n"
        "https://opengameart.org/content/32x32-boundworlds-character-maker-paintnet\n\n"
        "The PNGs here are the layers of `source/npc_builder.pdn`, extracted by `tools/extract_chibi.py` "
        "and renamed; Soluna recolours them at runtime.\n",
        encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
