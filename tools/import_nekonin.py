"""
Builds the NEKONIN (cat folk) character art set from Pipoya's Character Sprite 32 Generator, plus NPCs
from the NEKONIN sprite pack, and makes it the active set. Like the human set, it is for local use
only and goes to folders git ignores.

  input   assets/tilesets/private/.../characters/pipoya32x32_nekonin     parts
          assets/tilesets/private/.../characters/pipoya32x32_characters  weapons (they fit cat paws)
          assets/tilesets/private/PIPOYA FREE RPG Character Sprites NEKONIN, ...32x32/Enemy
  output  assets/characters/private/nekonin/   parts/*.png, npcs/*.png, catalog.json, options.json
          assets/characters/private/active     "nekonin"
          data/private/items.json, data/private/npcs.json

Usage: python tools/import_nekonin.py   (python tools/import_pipoya_characters.py switches back to humans)
"""

import json
import os
import pathlib
import re
import shutil
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from import_pipoya_characters import NPC_DEFS, STATS, WEAPON_NAMES, WEAPONS, layer_orders, repair  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parent.parent
# The game whose content this builds: games/<SOLUNA_GAME>, soluna by default.
GAME = ROOT / "games" / os.environ.get("SOLUNA_GAME", "soluna")
PRIVATE = GAME / "assets" / "tilesets" / "private"
SOURCE = next(PRIVATE.glob("**/pipoya32x32_nekonin"), None)
HUMANS = next(PRIVATE.glob("**/pipoya32x32_characters"), None)
NEKONIN_SPRITES = next(PRIVATE.glob("PIPOYA FREE RPG Character Sprites NEKONIN/**/pipo-nekonin001.png"), None)
MONSTERS = next(PRIVATE.glob("PIPOYA FREE RPG Character Sprites 32x32/**/Enemy"), None)
OUT = GAME / "assets" / "characters" / "private" / "nekonin"
ITEMS = GAME / "data" / "private" / "items.json"
NPCS = GAME / "data" / "private" / "npcs.json"

# The 17 bases: 12 cats, then bears and a panda, each with a tail of matching fur.
FURS = ["Creme", "Caramelo", "Branco", "Cinza", "Laranja rajado", "Tigrado", "Cinza e branco", "Tricolor",
        "Prata rajado", "Siamês", "Branco malhado", "Escaminha", "Urso pardo", "Urso escuro", "Urso marrom",
        "Urso polar", "Panda"]
TAILS = {"long": "尻尾", "hook": "カギ", "bob": "ボブ"}

COSTUMES = {1: ("hero", "Roupa de herói", (0, 3)), 2: ("demon_lord", "Armadura do Rei Demônio", (0, 6)),
            3: ("shorts", "Calção", (0, 1))}
HATS = [
    (r"帽子001", "hero_crown", "Coroa de herói", (0, 2)), (r"帽子002", "demon_helm", "Elmo do Rei Demônio", (0, 4)),
    (r"帽子003", "cap", "Boné", (0, 1)), (r"帽子005", "straw_hat", "Chapéu de palha", (0, 0)),
    (r"帽子006", "watermelon", "Melancia", (0, 1)), (r"帽子007", "pumpkin", "Abóbora", (0, 1)),
    (r"帽子008", "wizard_hat", "Chapéu de mago", (0, 0)), (r"帽子009", "laurel", "Coroa de louros", (0, 0)),
    (r"リボン001_右", "ribbon_right", "Laço direito", (0, 0)), (r"リボン001_左", "ribbon_left", "Laço esquerdo", (0, 0)),
    (r"天使", "halo", "Auréola", (0, 0)), (r"悪魔", "devil_horns", "Chifres", (0, 0)),
]
FACE = [(r"メガネ", "glasses", "Óculos"), (r"眼帯001-右", "eyepatch_right", "Tapa-olho direito"),
        (r"眼帯001-左", "eyepatch_left", "Tapa-olho esquerdo")]
NECK = [(r"スカーフ", "scarf", "Lenço"), (r"^ネクタイ", "tie", "Gravata"), (r"吊ネクタイ", "bow_tie", "Gravata-borboleta"),
        (r"メダル", "medal", "Medalha"), (r"首輪001", "collar", "Coleira"), (r"首輪002", "bell_collar", "Coleira com sino")]
BACK = [(r"マント001", "cape", "Capa", (0, 1)), (r"マント002", "demon_cape", "Capa do Rei Demônio", (0, 2)),
        (r"天使.*翼", "angel_wings", "Asas de anjo", (0, 1)), (r"悪魔.*翼", "devil_wings", "Asas de demônio", (0, 1))]

# Humanoid NPCs become cat folk from the NEKONIN pack; the monsters stay.
NEKONIN_NPCS = {1: ("Aldeão", 2), 2: ("Aldeã", 25), 3: ("Guarda", 14), 4: ("Gato", None), 5: ("Cachorro", None),
                6: ("Mercador", 32), 7: ("Bruxa da vila", 13), 8: ("Panda", 18)}


def files(source, folder):
    path = source / folder
    return [(repair(f.stem), f) for f in sorted(path.glob("*.png"))] if path.exists() else []


def variant(name):
    m = re.search(r"-(\d+)$", name)
    return int(m[1]) if m else 0


def main():
    if SOURCE is None:
        raise SystemExit("NEKONIN generator parts not found under assets/tilesets/private.")
    orders = layer_orders(SOURCE)
    human_orders = layer_orders(HUMANS) if HUMANS else {}
    if OUT.exists():
        shutil.rmtree(OUT)
    (OUT / "parts").mkdir(parents=True)
    sheets: dict[str, list[dict]] = {}

    def add(sheet_id, folder, file, z):
        target = f"parts/{sheet_id}{'_back' if folder.endswith('$') else ''}.png"
        shutil.copyfile(file, OUT / target)
        sheets.setdefault(sheet_id, []).append({"file": target, "z": z})

    def add_both(sheet_id, folder, name_pattern, source=SOURCE, order_map=None):
        """Front and back layers of a part whose file names match a pattern."""
        order_map = order_map or orders
        found = False
        for layer_folder in (folder, folder + "$"):
            for name, f in files(source, layer_folder):
                if re.search(name_pattern, name):
                    add(sheet_id, layer_folder, f, order_map[layer_folder])
                    found = True
        return found

    bases = [(name, f) for name, f in files(SOURCE, "00Skin") if name.startswith("ベース")]
    for i, (name, f) in enumerate(bases, start=1):
        add(f"body_c_{i}", "00Skin", f, orders["00Skin"])
        # Every tail style exists for every fur: cats have their own, bears all wear the bear tail.
        for style, prefix in TAILS.items():
            tail = f"{prefix}{i:03d}" if i <= 12 else f"クマ{i - 12:03d}"
            add_both(f"tail_{style}_{i}", "11Tail", f"^{tail}$")

    for name, f in files(SOURCE, "02Eye"):
        if m := re.fullmatch(r"目(\d+)(?:-(\d+))?", name):
            add(f"eyes_{int(m[1])}_{int(m[2] or 0)}", "02Eye", f, orders["02Eye"])
    add_both("whiskers", "09Beard", r"^ヒゲ001")

    items = []
    next_id = {"Torso": 100, "Head": 200, "Weapon": 300, "Back": 400, "Neck": 500, "Face": 600}

    def item(slot, name, sheet, stats=(0, 0), starter=False):
        entry = {"id": next_id[slot], "name": name, "slot": slot, "sheet": sheet, "colors": []}
        if stats[0]:
            entry["attack"] = stats[0]
        if stats[1]:
            entry["defense"] = stats[1]
        if starter:
            entry["starter"] = True
        items.append(entry)
        next_id[slot] += 1

    for name, f in files(SOURCE, "01Costume"):
        m = re.fullmatch(r"服(\d+).*", name)
        if not m or int(m[1]) not in COSTUMES:
            continue
        key, label, stats = COSTUMES[int(m[1])]
        v = variant(name)
        sheet = f"costume_{key}" + (f"_{v}" if v else "")
        add(sheet, "01Costume", f, orders["01Costume"])
        item("Torso", label + (f" {v}" if v > 1 else ""), sheet, stats, starter=(key == "shorts" and v == 1))

    for pattern, key, label, stats in HATS:
        names = sorted({n for n, _ in files(SOURCE, "05Hat") if re.match(pattern, n)})
        for i, n in enumerate(names, start=1):
            sheet = f"hat_{key}" + (f"_{i}" if len(names) > 1 else "")
            if add_both(sheet, "05Hat", "^" + re.escape(n) + "$"):
                item("Head", label + (f" {i}" if i > 1 else ""), sheet, stats)

    for slot, folder, table in (("Face", "06Glasses", FACE), ("Neck", "07Cloak", NECK)):
        for pattern, key, label in table:
            names = sorted({n for n, _ in files(SOURCE, folder) if re.search(pattern, n)})
            # Numbered one by one: several models can share a pattern (three kinds of glasses).
            for i, n in enumerate(names, start=1):
                sheet = key + (f"_{i}" if len(names) > 1 else "")
                if add_both(sheet, folder, "^" + re.escape(n) + "$"):
                    item(slot, label + (f" {i}" if i > 1 else ""), sheet, starter=(key == "scarf" and i == 1))

    for pattern, key, label, stats in BACK:
        names = sorted({n for n, _ in files(SOURCE, "07Cloak") if re.search(pattern, n)})
        for i, n in enumerate(names, start=1):
            sheet = key + (f"_{i}" if len(names) > 1 else "")
            if add_both(sheet, "07Cloak", "^" + re.escape(n) + "$"):
                item("Back", label + (f" {i}" if i > 1 else ""), sheet, stats)

    # Weapons from the human set: cat paws hold them just as well.
    if HUMANS:
        for name, f in files(HUMANS, "12Item"):
            m = re.fullmatch(r"(.+?\d{3})-(\d+)", name)
            if not m or m[1] not in WEAPONS:
                continue
            key = f"weapon_{WEAPONS[m[1]]}_{int(m[2])}"
            add_both(key, "12Item", "^" + re.escape(name) + "$", source=HUMANS, order_map=human_orders)
            base = WEAPONS[m[1]]
            label = WEAPON_NAMES.get(base, base) + ("" if m[2] == "01" else f" {int(m[2])}")
            item("Weapon", label, key, STATS.get(f"weapon_{base}", (0, 0)), starter=(key == "weapon_knife_1"))

    catalog = {"frame": 32, "layout": "rpgmaker", "sheets": {k: {"layers": v} for k, v in sorted(sheets.items())}}
    (OUT / "catalog.json").write_text(json.dumps(catalog, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")

    eye_ids = sorted({k for k in sheets if k.startswith("eyes_")}, key=lambda k: tuple(int(p) for p in k.split("_")[1:]))
    options = {
        "body": "body_{body}_{skin}",
        "bodies": [{"id": "c", "label": "Nekonin"}],
        "skins": [{"id": str(i), "label": fur} for i, fur in enumerate(FURS[:len(bases)], start=1)],
        "races": [
            {"id": "long", "label": "Cauda longa", "parts": ["tail_long_{skin}"]},
            {"id": "hook", "label": "Cauda curva", "parts": ["tail_hook_{skin}"]},
            {"id": "bob", "label": "Cauda curta", "parts": ["tail_bob_{skin}"]},
        ],
        "hair": [],
        "hairColors": [],
        "eyes": [{"id": k, "label": f"Olhos {i + 1}"} for i, k in enumerate(eye_ids)],
        "beards": [{"id": "", "label": "Sem bigodes"}, {"id": "whiskers", "label": "Bigodes"}],
        "labels": {"skins": "Pelagem", "races": "Cauda", "beards": "Bigodes"},
        "randomSkins": len(bases),
        "randomEyes": 20,
    }
    (OUT / "options.json").write_text(json.dumps(options, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")

    ITEMS.parent.mkdir(parents=True, exist_ok=True)
    ITEMS.write_text("[\n" + ",\n".join("  " + json.dumps(i, ensure_ascii=False) for i in items) + "\n]\n", encoding="utf-8")
    write_npcs()
    (OUT.parent / "active").write_text("nekonin\n", encoding="utf-8")
    print(f"{len(sheets)} sheets, {len(items)} items -> {OUT.relative_to(ROOT)} (now the active set)")


def write_npcs():
    out = OUT / "npcs"
    out.mkdir(parents=True, exist_ok=True)
    npcs = []
    for npc_id, name, file, behaviour, hp, attack, defense, exp, extra in NPC_DEFS:
        if npc_id in NEKONIN_NPCS:
            name, number = NEKONIN_NPCS[npc_id]
            source = NEKONIN_SPRITES.parent / f"pipo-nekonin{number:03d}.png" if number and NEKONIN_SPRITES else None
            if source is None and MONSTERS:
                source = MONSTERS.parent / file  # the pet cat and dog keep their animal sprites
        else:
            source = MONSTERS.parent / file if MONSTERS else None
        if source is None or not source.exists():
            continue
        target = out / f"{npc_id}.png"
        shutil.copyfile(source, target)
        entry = {"id": npc_id, "name": name, "sprite": target.relative_to(GAME / "assets").as_posix(),
                 "behaviour": behaviour, "hp": hp, "attack": attack, "defense": defense, "exp": exp}
        entry.update(extra)
        npcs.append(entry)
    # Villagers only the NEKONIN pack has.
    for npc_id in (6, 7, 8):
        name, number = NEKONIN_NPCS[npc_id]
        source = NEKONIN_SPRITES.parent / f"pipo-nekonin{number:03d}.png" if NEKONIN_SPRITES else None
        if source is None or not source.exists():
            continue
        target = out / f"{npc_id}.png"
        shutil.copyfile(source, target)
        npcs.append({"id": npc_id, "name": name, "sprite": target.relative_to(GAME / "assets").as_posix(),
                     "behaviour": "Friendly", "hp": 30, "attack": 0, "defense": 0, "exp": 0})
    npcs.sort(key=lambda n: n["id"])
    NPCS.write_text("[\n" + ",\n".join("  " + json.dumps(n, ensure_ascii=False) for n in npcs) + "\n]\n", encoding="utf-8")
    print(f"{len(npcs)} NPCs -> {NPCS.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
