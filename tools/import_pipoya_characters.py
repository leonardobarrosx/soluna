"""
Builds a character art set from Pipoya's Character Sprite 32 Generator (CharacterManaJ parts),
for local use: the parts may be used in games but not redistributed, so everything this writes
goes to folders git ignores.

  input   assets/tilesets/private/Pipoya Character Sprite 32 Generator for itch/.../pipoya32x32_characters
  output  assets/characters/private/pipoya/   parts/*.png, catalog.json, options.json, npcs/*.png
          data/private/items.json             equipment that uses those parts
          data/private/npcs.json              NPCs and monsters from the ready-made sprite pack

The zip ships Shift-JIS file names; extracted on Windows they come out as cp850 mojibake,
some of it lossy, so names are repaired first and then matched by pattern.

Usage: python tools/import_pipoya_characters.py   (makes this the active set; tools/import_nekonin.py is the other)
"""

import json
import os
import pathlib
import re
import shutil
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
# The game whose content this builds: games/<SOLUNA_GAME>, soluna by default.
GAME = ROOT / "games" / os.environ.get("SOLUNA_GAME", "soluna")
# Raw packs live in assets/sources (never sent to players); older setups kept them in tilesets/private.
SOURCE_DIRS = [GAME / "assets" / "sources", GAME / "assets" / "tilesets" / "private"]


def find(pattern):
    """The first path matching a glob pattern in any of the source folders."""
    for folder in SOURCE_DIRS:
        if folder.exists():
            match = next(folder.glob(pattern), None)
            if match:
                return match
    return None


SOURCE = find("**/pipoya32x32_characters")
SPRITES = find("**/PIPOYA FREE RPG Character Sprites 32x32/**/Enemy")
OUT = GAME / "assets" / "characters" / "private" / "pipoya"
ITEMS = GAME / "data" / "private" / "items.json"
NPCS = GAME / "data" / "private" / "npcs.json"

LOSSY = {"Åù": "女", "Æj": "男", "ïñùp": "共用", "û+": "目", "ò×": "服", "ö»": "髪"}


def repair(name: str) -> str:
    for encoding in ("cp850", "cp437"):
        try:
            return name.encode(encoding).decode("cp932")
        except (UnicodeEncodeError, UnicodeDecodeError):
            pass
    for broken, fixed in LOSSY.items():
        name = name.replace(broken, fixed)
    return name


def gender(mark: str) -> str:
    return {"女": "f", "男": "m"}.get(mark, "u")


# Costumes: (gender, number) -> item key. Variants (-01, -02...) become key_1, key_2...
COSTUMES = {
    ("f", 1): "villager", ("m", 1): "villager",
    ("f", 2): "farmer", ("m", 2): "farmer",
    ("m", 3): "knight", ("m", 4): "priest", ("m", 5): "minister", ("m", 6): "king",
    ("m", 7): "hero", ("f", 5): "hero",
    ("m", 8): "warrior", ("f", 6): "warrior",
    ("m", 9): "mage", ("f", 7): "mage",
    ("m", 10): "cleric", ("f", 8): "cleric",
    ("m", 11): "thief", ("f", 9): "thief",
    ("m", 12): "casual", ("f", 10): "casual",
    ("f", 3): "maid", ("f", 4): "princess",
}
HATS = {
    1: "helmet", 2: "crown", 3: "tiara", 5: "maid_band", 6: "wizard_hat", 7: "witch_hat",
    8: "cleric_cap", 9: "cleric_hood", 10: "headband", 11: "hairband", 12: "eggshell",
    13: "pumpkin", 14: "top_hat", 15: "ribbon_a", 16: "ribbon_b", 17: "ribbon_c",
}
WEAPONS = {
    "ナイフ001": "knife", "ナイフ002": "dagger", "ロッド001": "rod", "剣だけ001": "sword",
    "剣と剣001": "twin_swords", "剣と盾001": "sword_shield", "盾だけ001": "shield",
    "斧001": "axe", "槍001": "spear", "祇001": "bow",
}

# Layer folder -> z order, read from character.xml (back layers end in $).
def layer_orders(source: pathlib.Path) -> dict[str, int]:
    orders = {}
    for element in ET.parse(source / "character.xml").getroot().iter():
        if element.tag.split("}")[-1] != "layer":
            continue
        fields = {child.tag.split("}")[-1]: (child.text or "").strip() for child in element}
        if fields.get("dir") and fields.get("order"):
            orders[fields["dir"]] = int(fields["order"])
    return orders


def main():
    if SOURCE is None:
        raise SystemExit("Pipoya's generator not found under assets/tilesets/private.")
    orders = layer_orders(SOURCE)
    if OUT.exists():
        shutil.rmtree(OUT)
    (OUT / "parts").mkdir(parents=True)

    sheets: dict[str, list[dict]] = {}

    def add(sheet_id: str, folder: str, file: pathlib.Path):
        target = f"parts/{sheet_id}{'_back' if folder.endswith('$') else ''}.png"
        shutil.copyfile(file, OUT / target)
        sheets.setdefault(sheet_id, []).append({"file": target, "z": orders[folder]})

    def files(folder: str):
        path = SOURCE / folder
        return [(repair(f.stem), f) for f in sorted(path.glob("*.png"))] if path.exists() else []

    for folder in ("00Skin",):
        for name, f in files(folder):
            if m := re.fullmatch(r"(Male|Female)001-0([1-4])", name):
                add(f"body_{m[1][0].lower()}_{m[2]}", folder, f)

    for name, f in files("02Eye"):
        if m := re.fullmatch(r"(女|男|共用)_目(\d+)", name):
            add(f"eyes_{gender(m[1])}{int(m[2]):02d}", "02Eye", f)

    for folder, suffix in (("03Hair", ""), ("03Hair$", ""), ("03HairHat", "_hat"), ("03HairHat$", "_hat")):
        for name, f in files(folder):
            if m := re.fullmatch(r"(女|男)_髪(\d+)-01", name):
                add(f"hair_{gender(m[1])}{int(m[2]):02d}{suffix}", folder, f)

    costume_ids = {}
    for name, f in files("01Costume"):
        # Variant numbers end the name ("-01"); some names carry a lossy suffix with its own hyphen.
        m = re.fullmatch(r"(女|男)_服(\d+)(?:_.*?)?(?:-(\d+))?", name)
        if not m or (gender(m[1]), int(m[2])) not in COSTUMES:
            continue
        key = COSTUMES[(gender(m[1]), int(m[2]))] + (f"_{int(m[3])}" if m[3] else "")
        add(f"costume_{key}_{gender(m[1])}", "01Costume", f)
        costume_ids.setdefault(key, set()).add(gender(m[1]))

    for folder in ("05Hat", "05Hat$"):
        for name, f in files(folder):
            m = re.fullmatch(r"帽子(\d+)_[^-\d]*?(?:-?(\d+))?", name)
            if m and int(m[1]) in HATS:
                add(f"hat_{HATS[int(m[1])]}" + (f"_{int(m[2])}" if m[2] else ""), folder, f)

    for folder in ("12Item", "12Item$"):
        for name, f in files(folder):
            m = re.fullmatch(r"(.+?\d{3})-(\d+)", name)
            if m and m[1] in WEAPONS:
                add(f"weapon_{WEAPONS[m[1]]}_{int(m[2])}", folder, f)

    for folder in ("07Cloak", "07Cloak$"):
        for name, f in files(folder):
            if m := re.fullmatch(r"マント(\d+).*", name):
                add(f"cloak_{int(m[1])}", folder, f)
            elif name.startswith("矢筒"):
                add("quiver", folder, f)

    for name, f in files("09Beard"):
        if m := re.fullmatch(r"ヒゲ(\d+)-01", name):
            add(f"beard_{int(m[1])}", "09Beard", f)

    for folder in ("10Ear", "10Ear$"):
        for name, f in files(folder):
            if m := re.fullmatch(r"耳00([12])-0([1-4])", name):
                add(f"ears_{'elf' if m[1] == '1' else 'elf_long'}_{m[2]}", folder, f)
            elif name.startswith("耳004"):
                add("ears_cat", folder, f)
            elif name.startswith("耳005"):
                add("ears_dog", folder, f)

    for folder in ("11Tail", "11Tail$"):
        for name, f in files(folder):
            if name.startswith("尻尾002"):
                add("tail_cat", folder, f)
            elif name.startswith("尻尾003"):
                add("tail_dog", folder, f)

    catalog = {"frame": 32, "layout": "rpgmaker", "sheets": {k: {"layers": v} for k, v in sorted(sheets.items())}}
    (OUT / "catalog.json").write_text(json.dumps(catalog, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")

    def numbered(prefix, label):
        ids = sorted({k for k in sheets if re.fullmatch(prefix + r"\d+", k)})
        return [{"id": k, "label": f"{label} {i + 1}"} for i, k in enumerate(ids)]

    options = {
        "body": "body_{body}_{skin}",
        "bodies": [{"id": "m", "label": "Masculino"}, {"id": "f", "label": "Feminino"}],
        "skins": [{"id": "1", "label": "Clara"}, {"id": "2", "label": "Pálida"},
                  {"id": "3", "label": "Morena"}, {"id": "4", "label": "Negra"}],
        "races": [
            {"id": "human", "label": "Humano", "parts": []},
            {"id": "elf", "label": "Elfo", "parts": ["ears_elf_{skin}"]},
            {"id": "high_elf", "label": "Alto elfo", "parts": ["ears_elf_long_{skin}"]},
            {"id": "cat", "label": "Felino", "parts": ["ears_cat", "tail_cat"]},
            {"id": "dog", "label": "Canino", "parts": ["ears_dog", "tail_dog"]},
        ],
        "hair": numbered("hair_f", "Feminino") + numbered("hair_m", "Masculino"),
        "hairColors": [
            {"id": "#3a2c26", "label": "Castanho escuro"}, {"id": "#6b4a32", "label": "Castanho"},
            {"id": "#1f1c22", "label": "Preto"}, {"id": "#a8763f", "label": "Mel"},
            {"id": "#e0bf62", "label": "Loiro"}, {"id": "#b04a2e", "label": "Ruivo"},
            {"id": "#d9d4c6", "label": "Platinado"}, {"id": "#9a9aa6", "label": "Grisalho"},
            {"id": "#5a3a2a", "label": "Chocolate"},
            {"id": "#d27c9b", "label": "Rosa"}, {"id": "#7a5ccc", "label": "Lilás"},
            {"id": "#4f82cf", "label": "Azul"}, {"id": "#5aa36e", "label": "Verde"},
        ],
        # Shared eyes are the special ones (bandage, blindfold, eyepatch...), kept last and out of random picks.
        "eyes": numbered("eyes_f", "Feminino") + numbered("eyes_m", "Masculino") + numbered("eyes_u", "Especial"),
        "randomEyes": len(numbered("eyes_f", "")) + len(numbered("eyes_m", "")),
        "beards": [{"id": "", "label": "Nenhuma"}] + [{"id": f"beard_{i}", "label": f"Barba {i}"} for i in range(1, 5) if f"beard_{i}" in sheets],
        "defaults": {"Torso": "costume_villager"},
        "randomSkins": 4,
        "randomHairColors": 9,
        "credit": "Pipoya Character Sprite 32 Generator (private, not redistributed)",
    }
    (OUT / "options.json").write_text(json.dumps(options, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")

    write_items(costume_ids, sheets)
    write_npcs()
    (OUT.parent / "active").write_text("pipoya\n", encoding="utf-8")
    print(f"{len(sheets)} sheets, {sum(len(v) for v in sheets.values())} layers -> {OUT.relative_to(ROOT)}")


COSTUME_NAMES = {
    "villager": "Roupa de aldeão", "farmer": "Roupa de camponês", "knight_1": "Armadura de cavaleiro",
    "knight_2": "Armadura de prata", "knight_3": "Armadura azul", "priest": "Veste de sacerdote",
    "minister": "Traje de conselheiro", "king": "Traje real", "hero": "Roupa de herói",
    "warrior": "Armadura de guerreiro", "mage": "Manto de mago", "cleric": "Hábito de clérigo",
    "thief": "Traje de ladino", "casual_1": "Roupa de viagem", "casual_2": "Roupa de viagem verde",
    "casual_3": "Roupa de viagem escura", "maid": "Uniforme de criada", "princess": "Vestido real",
}
HAT_NAMES = {
    "helmet_1": "Elmo de ferro", "helmet_2": "Elmo de prata", "helmet_3": "Elmo azul", "crown": "Coroa",
    "tiara": "Tiara", "maid_band": "Touca de criada", "wizard_hat": "Chapéu de mago", "witch_hat": "Chapéu de bruxa",
    "cleric_cap": "Gorro de clérigo", "cleric_hood": "Capuz de clérigo", "headband": "Faixa vermelha",
    "hairband": "Arco de cabelo", "eggshell": "Casca de ovo", "pumpkin": "Cabeça de abóbora", "top_hat": "Cartola",
    "ribbon_a_1": "Laço branco", "ribbon_a_2": "Laço escuro", "ribbon_a_3": "Laço vermelho",
    "ribbon_b_1": "Laço lateral branco", "ribbon_b_2": "Laço lateral escuro", "ribbon_b_3": "Laço lateral vermelho",
    "ribbon_c_1": "Laço duplo branco", "ribbon_c_2": "Laço duplo escuro", "ribbon_c_3": "Laço duplo vermelho",
}
WEAPON_NAMES = {
    "knife": "Faca", "dagger": "Adaga", "rod": "Cajado", "sword": "Espada", "twin_swords": "Espadas gêmeas",
    "sword_shield": "Espada e escudo", "shield": "Escudo", "axe": "Machado", "spear": "Lança", "bow": "Arco",
}
CLOAK_NAMES = {1: "Capa azul", 2: "Capa branca", 3: "Manto branco", 4: "Capa cinza", 5: "Manto cinza",
               6: "Cachecol", 7: "Lenço", 8: "Capa de viajante"}


# Attack and defence by sheet (or sheet family, without the variant number).
STATS = {
    "weapon_knife": (2, 0), "weapon_dagger": (3, 0), "weapon_rod": (2, 0), "weapon_sword": (5, 0),
    "weapon_twin_swords": (7, 0), "weapon_sword_shield": (5, 2), "weapon_shield": (0, 4), "weapon_axe": (6, 0),
    "weapon_spear": (6, 0), "weapon_bow": (4, 0),
    "costume_knight_1": (0, 5), "costume_knight_2": (0, 6), "costume_knight_3": (0, 6), "costume_warrior": (0, 4),
    "costume_hero": (0, 3), "costume_king": (0, 2), "costume_thief": (0, 2), "costume_cleric": (0, 2),
    "costume_mage": (0, 1), "costume_priest": (0, 1), "costume_villager": (0, 1), "costume_farmer": (0, 1),
    "hat_helmet_1": (0, 2), "hat_helmet_2": (0, 3), "hat_helmet_3": (0, 3), "hat_crown": (0, 1),
    "cloak_1": (0, 1), "cloak_2": (0, 1), "cloak_3": (0, 1), "cloak_4": (0, 1), "cloak_5": (0, 1),
}

# NPCs: id, name, sprite file under the pack, behaviour, hp, attack, defence, exp, extra fields.
NPC_DEFS = [
    (1, "Aldeão", "Male/Male 01-1.png", "Friendly", 30, 0, 0, 0, {}),
    (2, "Aldeã", "Female/Female 01-1.png", "Friendly", 30, 0, 0, 0, {}),
    (3, "Guarda", "Soldier/Soldier 01-1.png", "Friendly", 200, 0, 0, 0, {"moveMs": 1400}),
    (4, "Gato", "Animal/Cat 01-1.png", "Friendly", 10, 0, 0, 0, {"moveMs": 500}),
    (5, "Cachorro", "Animal/Dog 01-1.png", "Friendly", 10, 0, 0, 0, {"moveMs": 450}),
    (10, "Fuligem", "Enemy/Enemy 16-1.png", "Passive", 18, 6, 0, 6, {"moveMs": 800}),
    (11, "Fuligem rubra", "Enemy/Enemy 16-2.png", "Aggressive", 26, 8, 1, 10, {"range": 4}),
    (12, "Goblin lanceiro", "Enemy/Enemy 18.png", "Aggressive", 42, 11, 3, 20, {"range": 5}),
    (13, "Goblin arqueiro", "Enemy/Enemy 19.png", "Aggressive", 34, 10, 2, 18, {"range": 6}),
    (14, "Goblin espadachim", "Enemy/Enemy 20.png", "Aggressive", 50, 13, 4, 25, {"range": 5}),
    (15, "Goblin xamã", "Enemy/Enemy 21.png", "Aggressive", 38, 12, 2, 24, {"range": 6}),
    (16, "Chefe goblin", "Enemy/Enemy 22.png", "Aggressive", 140, 18, 7, 110, {"range": 5, "respawnSeconds": 120}),
    (20, "Esqueleto", "Enemy/Enemy 04-1.png", "Aggressive", 60, 15, 5, 32, {"range": 5}),
    (21, "Fantasma", "Enemy/Enemy 09-1.png", "Passive", 32, 11, 1, 18, {"moveMs": 700}),
    (22, "Abóbora", "Enemy/Enemy 03-1.png", "Passive", 45, 9, 3, 15, {}),
    (23, "Espectro", "Enemy/Enemy 15-1.png", "Aggressive", 55, 16, 3, 35, {"range": 6}),
    (30, "Lich", "Boss/Boss 01.png", "Aggressive", 450, 28, 12, 600, {"range": 7, "respawnSeconds": 600, "moveMs": 900}),
]


def write_npcs():
    if SPRITES is None:
        print("ready-made sprite pack not found, no NPCs")
        return
    pack = SPRITES.parent
    out = OUT / "npcs"
    out.mkdir(parents=True, exist_ok=True)
    npcs = []
    for npc_id, name, file, behaviour, hp, attack, defense, exp, extra in NPC_DEFS:
        source = pack / file
        if not source.exists():
            print("missing", file)
            continue
        target = out / f"{npc_id}.png"
        shutil.copyfile(source, target)
        entry = {"id": npc_id, "name": name, "sprite": target.relative_to(GAME / "assets").as_posix(),
                 "behaviour": behaviour, "hp": hp, "attack": attack, "defense": defense, "exp": exp}
        entry.update(extra)
        npcs.append(entry)
    NPCS.write_text("[\n" + ",\n".join("  " + json.dumps(n, ensure_ascii=False) for n in npcs) + "\n]\n", encoding="utf-8")
    print(f"{len(npcs)} NPCs -> {NPCS.relative_to(ROOT)}")


def write_items(costume_ids, sheets):
    items, next_id = [], {"Torso": 100, "Head": 200, "Weapon": 300, "Back": 400}

    def item(slot, name, sheet, starter=False):
        entry = {"id": next_id[slot], "name": name, "slot": slot, "sheet": sheet, "colors": []}
        attack, defense = STATS.get(sheet, STATS.get(sheet.rsplit("_", 1)[0], (0, 0)))
        if attack:
            entry["attack"] = attack
        if defense:
            entry["defense"] = defense
        if starter:
            entry["starter"] = True
        items.append(entry)
        next_id[slot] += 1

    for key in sorted(costume_ids, key=lambda k: list(COSTUME_NAMES).index(k) if k in COSTUME_NAMES else 99):
        item("Torso", COSTUME_NAMES.get(key, key), f"costume_{key}", starter=key in ("villager", "farmer"))
    for key in sorted(k[4:] for k in sheets if k.startswith("hat_")):
        item("Head", HAT_NAMES.get(key, key.replace("_", " ").capitalize()), f"hat_{key}")
    seen = set()
    for key in sorted(k for k in sheets if k.startswith("weapon_")):
        base, variant = key[7:].rsplit("_", 1)
        label = WEAPON_NAMES.get(base, base) + ("" if variant == "1" else f" {variant}")
        item("Weapon", label, key, starter=(base == "knife" and base not in seen))
        seen.add(base)
    for i, name in CLOAK_NAMES.items():
        if f"cloak_{i}" in sheets:
            item("Back", name, f"cloak_{i}")
    if "quiver" in sheets:
        item("Back", "Aljava", "quiver")

    ITEMS.parent.mkdir(parents=True, exist_ok=True)
    ITEMS.write_text("[\n" + ",\n".join("  " + json.dumps(i, ensure_ascii=False) for i in items) + "\n]\n", encoding="utf-8")
    print(f"{len(items)} items -> {ITEMS.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
