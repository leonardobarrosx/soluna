"""
Downloads the LPC art Soluna ships with and writes the credits for it.

Characters come from the Universal LPC Spritesheet Character Generator as separate layers
(body, head, hair, clothes) plus colour palettes, so the client can assemble and recolour
them at runtime. Tiles come from the LPC base assets.

Everything fetched here is CC-BY-SA 3.0 / GPL 3.0 (much of it also OGA-BY 3.0), which allows
redistribution with credit; the credits land next to the files.

Usage: python tools/fetch_assets.py   (from anywhere; needs only the standard library)
"""

import io
import json
import pathlib
import urllib.request
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
ULPC = "https://raw.githubusercontent.com/LiberatedPixelCup/Universal-LPC-Spritesheet-Character-Generator/master"
BASE_ASSETS_ZIP = "https://opengameart.org/sites/default/files/lpc_base_assets.zip"

BODIES = ["male", "female"]
ANIMATIONS = ["walk"]
MATERIALS = ["body", "hair", "cloth", "eye", "metal"]

# Sheet definitions to fetch, by path under sheet_definitions/. The file name is the sheet id
# the engine refers to (see Soluna.Shared/CharacterOptions.cs and data/items.json).
SHEETS = [
    "body/body.json",
    "head/heads/human/heads_human_male.json",
    "head/heads/human/heads_human_female.json",
    # Hair styles offered at character creation.
    "hair/short/hair_plain.json",
    "hair/short/hair_messy1.json",
    "hair/short/hair_parted.json",
    "hair/short/hair_bangs.json",
    "hair/short/hair_pixie.json",
    "hair/bob/hair_bob.json",
    "hair/long/hair_long.json",
    "hair/braids/hair_ponytail.json",
    "hair/afro/hair_afro.json",
    "hair/spiky/hair_spiked.json",
    # Equipment.
    "torso/shirts/shortsleeve/torso_clothes_shortsleeve.json",
    "torso/shirts/longsleeve/torso_clothes_longsleeve.json",
    "torso/armour/torso_armour_leather.json",
    "torso/armour/torso_armour_plate.json",
    "legs/pants/legs_pants.json",
    "legs/skirts/legs_skirts_plain.json",
    "feet/boots/feet_boots_basic.json",
    "feet/shoes/feet_shoes_basic.json",
    "headwear/coverings/hoods/hat_hood_cloth.json",
    "headwear/hats/magic/hat_magic_wizard.json",
]


def get(url: str) -> bytes:
    with urllib.request.urlopen(url, timeout=60) as response:
        return response.read()


def get_json(url: str):
    return json.loads(get(url))


def recolor_channels(definition) -> list[str]:
    """Materials of each recolourable channel, in order (color_1, color_2, ...)."""
    recolors = definition.get("recolors")
    if not recolors:
        return []
    if "material" in recolors:
        return [recolors["material"]]
    channels = sorted(k for k in recolors if k.startswith("color_"))
    return [recolors[k]["material"] for k in channels]


def fetch_characters():
    out = ASSETS / "characters" / "lpc"
    catalog = {"animations": ANIMATIONS, "palettes": {}, "sheets": {}}
    credits = {}

    for material in MATERIALS:
        meta = get_json(f"{ULPC}/palette_definitions/{material}/meta_{material}.json")
        variants = get_json(f"{ULPC}/palette_definitions/{material}/{material}_ulpc.json")
        catalog["palettes"][material] = {"base": meta["base"], "variants": variants}

    for sheet_path in SHEETS:
        sheet_id = pathlib.Path(sheet_path).stem
        definition = get_json(f"{ULPC}/sheet_definitions/{sheet_path}")
        layers = []
        for key in sorted(k for k in definition if k.startswith("layer_")):
            layer = definition[key]
            paths = {}
            for body in BODIES:
                folder = layer.get(body)
                if not folder:
                    continue
                paths[body] = folder.rstrip("/")
                for animation in ANIMATIONS:
                    target = out / folder / f"{animation}.png"
                    if not target.exists():
                        target.parent.mkdir(parents=True, exist_ok=True)
                        target.write_bytes(get(f"{ULPC}/spritesheets/{folder}{animation}.png"))
                        print("  ", target.relative_to(ROOT))
            layers.append({"z": layer["zPos"], "paths": paths})

        catalog["sheets"][sheet_id] = {
            "name": definition["name"],
            "type": definition.get("type_name", ""),
            "materials": recolor_channels(definition),
            "layers": layers,
        }

        used = {p for layer in layers for p in layer["paths"].values()}
        for entry in definition.get("credits", []):
            if any(p.startswith(entry["file"].rstrip("/")) or entry["file"].startswith(p) for p in used):
                credits[entry["file"]] = entry
        print(sheet_id)

    (out / "catalog.json").write_text(json.dumps(catalog, indent=1), encoding="utf-8")
    write_credits(out / "CREDITS.md", "LPC character layers", credits.values(),
                  "From the Universal LPC Spritesheet Character Generator: "
                  "https://github.com/LiberatedPixelCup/Universal-LPC-Spritesheet-Character-Generator")


def write_credits(path: pathlib.Path, title: str, entries, source: str):
    lines = [f"# {title}", "", source, ""]
    for entry in sorted(entries, key=lambda e: e["file"]):
        lines.append(f"## {entry['file']}")
        lines.append("")
        lines.append("Authors: " + ", ".join(entry.get("authors", [])))
        lines.append("")
        lines.append("Licenses: " + ", ".join(entry.get("licenses", [])))
        lines.append("")
        for url in entry.get("urls", []):
            lines.append(f"- {url}")
        if entry.get("notes"):
            lines.append("")
            lines.append(entry["notes"])
        lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def fetch_tiles():
    out = ASSETS / "tilesets" / "lpc"
    out.mkdir(parents=True, exist_ok=True)
    archive = zipfile.ZipFile(io.BytesIO(get(BASE_ASSETS_ZIP)))
    for name in archive.namelist():
        parts = pathlib.PurePosixPath(name).parts
        if len(parts) == 3 and parts[1] == "tiles" and name.endswith(".png"):
            (out / parts[2]).write_bytes(archive.read(name))
        elif name.endswith("CREDITS.TXT"):
            (out / "CREDITS.TXT").write_bytes(archive.read(name))
    (out / "LICENSE.md").write_text(
        "# LPC base tiles\n\n"
        "From https://opengameart.org/content/liberated-pixel-cup-lpc-base-assets-sprites-map-tiles\n\n"
        "Licensed CC-BY-SA 3.0 or GPL 3.0; the work of Lanea Zimmerman (Sharm) and "
        "Daniel Armstrong (HughSpectrum) is also available as OGA-BY 3.0. "
        "Per-file authors are in CREDITS.TXT.\n",
        encoding="utf-8")
    print("tiles:", len(list(out.glob("*.png"))), "files")


if __name__ == "__main__":
    fetch_characters()
    fetch_tiles()
