"""Four furnished, connected rooms. These are example-game data, not engine rules."""
from soluna_maps import Plan, CATALOG

WOOD, PARQUET, STONE = 176, 177, 178
WALL, PLASTER, BEAM = 180, 181, 183


def room(id, name, floor, door, luck=2):
    p = Plan(id, name, 32, 24, 100+id, (16, 19))
    p.m['Luck'] = luck
    p.rect(0, 0, 32, 24, -1, True)
    p.rect(3, 3, 26, 18, WALL, True)
    p.rect(4, 3, 24, 1, BEAM)
    p.rect(4, 4, 24, 1, PLASTER)
    p.rect(4, 5, 24, 15, floor, False)
    p.rect(15, 19, 3, 2, WOOD, False)
    p.warp(range(15, 18), 20, 1, door)
    return p


def furniture(p, name, x, y, width=None, height=None):
    w, h = width or CATALOG[name]['Width'], height or CATALOG[name]['Height']
    solid = None if CATALOG[name].get('Underfoot') else (0, h-1, w, 1)
    return p.prop(name, x, y, w, h, solid=solid)


def inn():
    p = room(3, 'Estalagem · A Lua Velada', WOOD, (8, 17))
    p.rect(22, 5, 6, 15, PARQUET)
    p.rect(21, 5, 1, 5, WALL, True)
    p.rect(21, 12, 1, 5, WALL, True)
    furniture(p, 'teal-rug', 6, 10, 5, 4)
    furniture(p, 'teal-rug', 13, 12, 5, 4)
    furniture(p, 'teal-rug', 23, 8, 4, 3)
    furniture(p, 'tavern-bar', 5, 5, 5, 3)
    furniture(p, 'hearth', 14, 4)
    furniture(p, 'dining-table', 7, 11)
    furniture(p, 'dining-table', 14, 13)
    furniture(p, 'bookcase', 23, 4)
    furniture(p, 'bed', 23, 13)
    furniture(p, 'bed', 26, 13)
    furniture(p, 'stock', 5, 16)
    furniture(p, 'planter', 18, 17)
    p.lamp(19, 5); p.lamp(26, 5)
    p.point(1, 'Provisões da estalagem', 8, 9)
    p.point(2, 'Notícias junto à lareira', 15, 8)
    p.m['Npcs'] = [dict(NpcId=1, X=10, Y=9)]
    return p.finish()


def smithy():
    p = room(4, 'Ferraria · Brasa Pálida', STONE, (10, 30))
    p.rect(4, 5, 12, 8, 191)
    p.rect(20, 5, 8, 15, WOOD)
    furniture(p, 'teal-rug', 22, 11)
    furniture(p, 'coal-forge', 6, 4, 4, 4)
    furniture(p, 'bellows', 11, 6)
    furniture(p, 'anvil', 8, 10)
    furniture(p, 'anvil', 13, 10)
    furniture(p, 'weapon-rack', 22, 4, 4, 3)
    furniture(p, 'tavern-bar', 22, 9, 4, 2)
    furniture(p, 'woodpile', 5, 16, 3, 1)
    furniture(p, 'ore', 10, 16)
    furniture(p, 'stock', 24, 16, 3, 2)
    p.lamp(18, 5); p.lamp(26, 14)
    p.point(11, 'Forja e bancada de ofícios', 10, 12)
    p.point(1, 'Suprimentos da ferraria', 23, 12)
    p.m['Npcs'] = [dict(NpcId=1, X=25, Y=12)]
    return p.finish()


def herbalist():
    p = room(5, 'Botica · Raiz da Lua', 189, (33, 16), 3)
    p.rect(19, 5, 9, 15, WOOD)
    furniture(p, 'teal-rug', 8, 10, 6, 4)
    furniture(p, 'teal-rug', 22, 12, 4, 4)
    furniture(p, 'herb-shelf', 5, 4)
    furniture(p, 'herb-shelf', 10, 4)
    furniture(p, 'bookcase', 24, 4)
    furniture(p, 'alchemy-table', 9, 10, 4, 3)
    furniture(p, 'cauldron', 15, 6)
    furniture(p, 'ritual-altar', 23, 10)
    furniture(p, 'planter', 5, 16)
    furniture(p, 'planter', 10, 17)
    furniture(p, 'herbs', 18, 17)
    furniture(p, 'bookcase', 25, 16)
    p.lamp(18, 5); p.lamp(21, 16)
    p.point(11, 'Mesa de infusões', 11, 14)
    p.point(0, 'Altar das raízes', 24, 13)
    p.point(1, 'Provisões da botica', 7, 8)
    p.m['Npcs'] = [dict(NpcId=2, X=14, Y=13)]
    return p.finish()


def mill():
    p = room(6, 'Moinho · Margem Silenciosa', 188, (38, 27))
    p.rect(4, 5, 12, 9, STONE)
    furniture(p, 'teal-rug', 19, 12, 5, 3)
    furniture(p, 'grindstone', 6, 5, 5, 4)
    furniture(p, 'flour-sacks', 13, 5)
    furniture(p, 'flour-sacks', 23, 5)
    furniture(p, 'fish-rack', 19, 5)
    furniture(p, 'dining-table', 20, 12)
    furniture(p, 'stock', 5, 16, 3, 2)
    furniture(p, 'woodpile', 9, 17)
    furniture(p, 'forge', 11, 11)
    furniture(p, 'flour-sacks', 24, 16)
    p.lamp(17, 5); p.lamp(26, 10)
    p.point(11, 'Bancada do moinho', 12, 14)
    p.point(1, 'Reservas da margem', 20, 10)
    p.m['Npcs'] = [dict(NpcId=1, X=22, Y=10)]
    return p.finish()


def interiors():
    return [inn(), smithy(), herbalist(), mill()]
