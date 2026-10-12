"""Deterministic, authored scenery plans for Soluna.

The same catalog is reusable by other games; routes, districts and clearings below
are deliberately authored for this example. Coordinates are in 32-pixel tiles.
"""
import base64
import json
import math
from pathlib import Path

ASSETS = Path(__file__).resolve().parents[1] / 'games/soluna/assets'
CATALOG = json.loads((ASSETS / 'props/catalog.json').read_text())
GRASS, MOSS, DIRT, COBBLE, WATER, SHALLOW, FLAG, DARK_FLAG = range(128, 136)
SOIL, STRAW, DECK, PLANK, LEAVES, GRAVEL, OLD_FLAG, SAND = range(136, 144)


class Plan:
    def __init__(self, id, name, width, height, seed, spawn):
        self.w, self.h = width, height
        self.blocked = bytearray(width * height)
        self.routes = set()
        self.m = dict(Id=id, Name=name, Width=width, Height=height, Revision=10,
                      Moral=0, Depth=0, Seed=seed, Music='', Links={}, Warps=[], Npcs=[],
                      Landmarks=[], Props=[], Tilesets=['soluna/atlas.png'],
                      Layers=[[GRASS] * (width * height)] + [[-1] * (width * height) for _ in range(4)],
                      FringeFrom=3, SpawnX=spawn[0], SpawnY=spawn[1])

    def inside(self, x, y):
        return 0 <= x < self.w and 0 <= y < self.h

    def tile(self, x, y, tile, layer=0, blocked=None):
        if not self.inside(x, y):
            return
        self.m['Layers'][layer][y*self.w+x] = tile
        if blocked is not None:
            self.blocked[y*self.w+x] = int(blocked)

    def rect(self, x, y, w, h, tile, blocked=None):
        for yy in range(y, y+h):
            for xx in range(x, x+w):
                self.tile(xx, yy, tile, blocked=blocked)

    def oval(self, cx, cy, rx, ry, tile, blocked=None):
        for y in range(math.floor(cy-ry), math.ceil(cy+ry)+1):
            for x in range(math.floor(cx-rx), math.ceil(cx+rx)+1):
                if ((x-cx)/rx)**2 + ((y-cy)/ry)**2 <= 1:
                    self.tile(x, y, tile, blocked=blocked)

    def path(self, points, radius=1.2, tile=DIRT):
        for (ax, ay), (bx, by) in zip(points, points[1:]):
            steps = max(abs(ax-bx), abs(ay-by))*4+1
            for n in range(steps+1):
                cx, cy = ax+(bx-ax)*n/steps, ay+(by-ay)*n/steps
                for y in range(math.floor(cy-radius), math.ceil(cy+radius)+1):
                    for x in range(math.floor(cx-radius), math.ceil(cx+radius)+1):
                        if (x-cx)**2+(y-cy)**2 <= radius*radius and self.inside(x, y):
                            self.tile(x, y, tile, blocked=False)
                            self.routes.add((x, y))

    def prop(self, name, x, y, width=None, height=None, solid=None):
        p = dict(CATALOG[name], X=x, Y=y)
        if width is not None:
            p['Width'] = width
        if height is not None:
            p['Height'] = height
        self.m['Props'].append(p)
        # Footprints belong to the map, independent of sprite transparency.
        if solid:
            dx, dy, w, h = solid
            for yy in range(y+dy, y+dy+h):
                for xx in range(x+dx, x+dx+w):
                    if self.inside(xx, yy):
                        self.blocked[yy*self.w+xx] = 1
        return p

    def tree(self, name, x, y, w=None, h=None):
        p = self.prop(name, x, y, w, h)
        tx, ty = x+p['Width']//2, y+p['Height']-1
        if self.inside(tx, ty):
            self.blocked[ty*self.w+tx] = 1

    def lamp(self, x, y):
        self.prop('lantern', x, y, solid=(0, 1, 1, 1))

    def point(self, kind, name, x, y, tile=None):
        self.m['Landmarks'].append(dict(Kind=kind, Name=name, X=x, Y=y))
        if tile is not None:
            self.tile(x, y, tile, layer=1)
        self.blocked[y*self.w+x] = 3

    def water_edge(self):
        ground = self.m['Layers'][0]
        for y in range(1, self.h-1):
            for x in range(1, self.w-1):
                if ground[y*self.w+x] == WATER and any(ground[(y+dy)*self.w+x+dx] not in (WATER, SHALLOW)
                    for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0))):
                    self.tile(x, y, SHALLOW)

    def rim(self):
        for x in range(self.w):
            self.blocked[x] = self.blocked[(self.h-1)*self.w+x] = 1
        for y in range(self.h):
            self.blocked[y*self.w] = self.blocked[y*self.w+self.w-1] = 1

    def warp(self, xs, y, target, to):
        for x in xs:
            self.blocked[y*self.w+x] = 2
            self.m['Warps'].append(dict(X=x, Y=y, Map=target, ToX=to[0], ToY=to[1]))

    def finish(self):
        self.rim()
        self.m['Attributes'] = base64.b64encode(self.blocked).decode()
        return self.m


def village():
    p = Plan(1, 'Vigília de Soluna', 44, 36, 83, (22, 21))
    # Connected districts: memorial approach, market square, herb garden, mill bank.
    for x, y, rx, ry in [(6, 7, 8, 5), (36, 7, 8, 5), (6, 31, 8, 5), (29, 33, 8, 3)]:
        p.oval(x, y, rx, ry, MOSS)
    p.oval(22, 20, 7.6, 6.2, COBBLE)
    p.oval(22, 20, 6.3, 5.2, FLAG)
    p.path([(22, 20), (22, 13), (24, 10), (23, 7), (22, 4)], 1.6, OLD_FLAG)
    p.path([(8, 21), (13, 21), (17, 22), (22, 22)], 1.4, COBBLE)
    p.path([(22, 22), (28, 23), (31, 25), (34, 27)], 1.3, COBBLE)
    p.path([(25, 20), (29, 19), (34, 19), (34, 16)], 1.15, GRAVEL)
    p.path([(20, 25), (20, 28), (23, 31), (22, 34)], 1.3, DIRT)
    p.path([(21, 28), (15, 30), (10, 30), (10, 28)], 1.1, GRAVEL)
    p.path([(23, 31), (29, 32)], .9, GRAVEL)
    p.rect(7, 23, 11, 6, GRAVEL)
    p.rect(7, 17, 8, 6, COBBLE)
    p.path([(9, 20), (9, 16)], 1.1, COBBLE)
    # Lay the square last, so feeder streets stop at its border instead of cutting across it.
    p.oval(22, 20, 6.6, 5.6, COBBLE)
    p.oval(22, 20, 5.5, 4.6, FLAG)
    p.oval(22, 20, 2.1, 1.7, OLD_FLAG)

    # The water belongs to the mill, with shallow banks and a reachable wooden landing.
    p.oval(39, 30, 4.8, 4.7, SAND, False)
    p.oval(39.5, 30.5, 4, 3.6, WATER, True)
    p.water_edge()
    p.rect(33, 26, 3, 4, DECK, False)
    p.prop('pier', 33, 27, 3, 4)

    # Each building has an entrance path and a distinct yard.
    p.prop('inn', 5, 11, solid=(1, 3, 5, 2))
    p.prop('smithy', 7, 24, solid=(1, 3, 5, 2))
    p.prop('herbalist', 31, 10, solid=(1, 3, 5, 2))
    p.prop('mill', 36, 21, solid=(1, 3, 4, 2))
    p.m['Props'].append(dict(Sprite='props/cottage.png', X=26, Y=28, Width=5, Height=4))
    for y in (30, 31):
        for x in range(27, 30):
            p.blocked[y*p.w+x] = 1
    p.prop('market', 12, 18, solid=(0, 2, 3, 1))
    p.prop('stock', 9, 20, solid=(0, 1, 2, 1))
    p.prop('forge', 16, 21, solid=(0, 1, 2, 1))
    p.prop('stock', 14, 26, solid=(0, 1, 2, 1))
    p.prop('woodpile', 6, 28)
    p.prop('well', 10, 7, solid=(0, 1, 2, 1))
    p.path([(9, 16), (9, 10), (11, 9)], .75, DIRT)

    # A crescent fountain is the square's focal point. Its front stays open for interaction.
    p.prop('fountain', 21, 13, solid=(0, 2, 3, 1))
    p.prop('notice', 25, 24, solid=(0, 1, 2, 1))
    p.prop('benches', 18, 15)
    p.prop('benches', 26, 17)
    for x, y in [(17, 18), (26, 13), (17, 25), (27, 26), (12, 15), (32, 23)]:
        p.lamp(x, y)
    for x, y in [(18, 13), (24, 13), (18, 26), (25, 27), (6, 16), (10, 16)]:
        p.prop('planter', x, y)
    for x, y in [(15, 15), (16, 14), (28, 16), (28, 25), (15, 28), (32, 26)]:
        p.prop('herbs', x, y)
    for x, y in [(17, 11), (19, 10), (26, 10), (28, 11), (30, 22), (32, 22)]:
        p.prop('hedge', x, y, solid=(0, 0, 2, 1))
    for x in (30, 32, 36, 38):
        p.prop('fence-h', x, 21, solid=(0, 0, 2, 1))
    for y in (16, 18):
        p.prop('fence-v', 39, y, solid=(0, 0, 1, 2))
    for x in (31, 33, 36, 38):
        p.rect(x, 17, 1, 3, SOIL)
        for y in (17, 19):
            p.prop('herbs' if x < 35 else 'fern', x, y)
    p.path([(34, 19), (34, 23), (31, 24)], .65, GRAVEL)

    # Memorial garden: surviving masonry and a quiet loop around the northern route.
    for x, y in [(17, 7), (19, 6), (26, 6), (28, 7)]:
        p.prop('wall-h', x, y, solid=(0, 0, 2, 1))
    p.prop('broken-arch', 17, 4)
    p.prop('rubble', 28, 9)
    p.prop('herbs', 18, 9)
    p.prop('herbs', 27, 8)
    p.prop('planter', 27, 32)
    p.prop('fence-h', 25, 33, solid=(0, 0, 2, 1))
    p.prop('fence-h', 29, 33, solid=(0, 0, 2, 1))

    # Deliberately staggered groves frame the village instead of scattered solitary trees.
    trees = [('oak', 1, 3), ('fir', 5, 2), ('oak', 8, 1), ('fir', 13, 2), ('oak', 30, 1),
             ('fir', 35, 2), ('oak', 38, 3), ('oak', 1, 9), ('fir', 1, 14), ('oak', 1, 19),
             ('fir', 2, 25), ('oak', 1, 29), ('oak', 5, 31), ('fir', 11, 31), ('oak', 15, 31),
             ('oak', 33, 31), ('fir', 40, 11), ('oak', 39, 15), ('willow', 37, 27),
             ('oak', 14, 7), ('fir', 28, 4), ('oak', 29, 26)]
    trees += [('fir', 2, 1), ('oak', 10, 2), ('fir', 1, 6), ('oak', 32, 3), ('fir', 37, 1),
              ('oak', 39, 7), ('fir', 1, 23), ('oak', 3, 30), ('fir', 8, 31), ('oak', 37, 31),
              ('oak', 13, 11), ('oak', 27, 7), ('fir', 39, 18), ('fir', 5, 28)]
    for name, x, y in trees:
        p.tree(name, x, y)
    for x, y in [(4, 7), (3, 17), (4, 23), (13, 10), (30, 8), (39, 9), (14, 32), (32, 30)]:
        p.prop('fern', x, y)
    for x, y in [(36, 31), (38, 33), (41, 29), (36, 29)]:
        p.prop('reeds', x, y)

    p.point(6, 'Águas silenciosas', 34, 27)
    p.point(0, 'Santuário das memórias', 22, 17)
    p.point(1, 'Mercador de cinzas', 15, 22)
    p.point(11, 'Bancada de ofícios', 17, 23)
    p.point(2, 'O sino da vigília', 24, 25)
    p.m['Npcs'] = [dict(NpcId=1, X=14, Y=22), dict(NpcId=2, X=23, Y=17)]
    p.warp(range(20, 25), 4, 2, (24, 41))
    return p.finish()


def forest():
    p = Plan(2, 'Bosque dos Esquecidos', 48, 48, 192, (24, 41))
    p.rect(0, 0, 48, 48, MOSS)
    # Clearings have irregular, overlapping outlines; their connecting routes form a loop.
    for x, y, rx, ry in [(24, 39, 6, 6), (19, 34, 8, 5), (28, 31, 7, 5), (23, 25, 6, 6),
                         (25, 18, 6, 5), (14, 22, 5, 4), (24, 10, 8, 5)]:
        p.oval(x, y, rx, ry, GRASS)
    p.oval(9, 33, 5.2, 5, SAND, False)
    p.oval(8, 33, 3.8, 4.2, WATER, True)
    p.path([(24, 43), (24, 39), (23, 36), (25, 32), (24, 29), (21, 26),
            (21, 23), (24, 20), (24, 17), (25, 14), (24, 10), (24, 9)], 1.25)
    p.path([(23, 35), (18, 35), (12, 34), (13, 30), (17, 29), (21, 26)], 1.0)
    p.path([(24, 35), (29, 34), (33, 31), (33, 27), (28, 22), (24, 20)], 1.0)
    p.path([(21, 23), (18, 23), (15, 21)], .95, OLD_FLAG)
    p.path([(18, 35), (19, 37)], .8)
    p.path([(17, 29), (18, 31)], .85)
    p.water_edge()
    p.rect(11, 33, 2, 3, DECK, False)
    p.prop('pier', 10, 33, 3, 3)

    # Northern ruin courtyard grows more architectural as the player approaches it.
    p.oval(24, 10, 6.8, 3.6, COBBLE)
    p.oval(24, 10, 5.4, 2.8, OLD_FLAG)
    p.path([(24, 9), (24, 14), (24, 16)], 1.1, OLD_FLAG)
    p.m['Props'].append(dict(Sprite='props/ruin.png', X=22, Y=4, Width=5, Height=5))
    for x in range(22, 27):
        p.blocked[8*p.w+x] = 1
    for x, y in [(17, 8), (19, 6), (27, 6), (30, 8), (18, 12), (28, 12)]:
        p.prop('wall-h', x, y, solid=(0, 0, 2, 1))
    for x, y in [(19, 9), (28, 9), (20, 14), (28, 14)]:
        p.prop('rubble', x, y)
    for x, y in [(21, 10), (27, 10)]:
        p.tile(x, y, 109, layer=1)
    p.prop('broken-arch', 14, 17, solid=(0, 2, 1, 1))
    p.prop('rubble', 12, 21)
    p.prop('mushrooms', 14, 23)
    p.prop('crystal', 28, 20, solid=(0, 1, 2, 1))
    p.prop('boulder', 30, 20, solid=(1, 1, 2, 1))
    p.prop('mushrooms', 30, 23)

    # Material sites are recognizable mini-scenes, with a clear approach to each activity.
    p.oval(31, 33, 4, 3, GRAVEL)
    p.path([(29, 34), (31, 34), (33, 31)], .9, GRAVEL)
    for x, y in [(32, 31), (34, 32), (31, 35)]:
        p.prop('boulder', x, y, solid=(1, 1, 2, 1))
    p.prop('ore', 29, 32, solid=(0, 1, 2, 1))
    p.oval(18, 31, 2.5, 2.2, STRAW)
    p.prop('woodpile', 17, 30)
    p.prop('fallen-log', 15, 31)
    p.prop('fallen-log', 18, 28)
    p.prop('herbs', 18, 37)
    p.prop('herbs', 20, 38)
    p.prop('fern', 18, 39)
    p.prop('camp', 27, 38, solid=(0, 1, 1, 1))
    p.prop('stock', 30, 39, 1, 1)
    p.prop('notice', 21, 40, 1, 1)
    for x, y in [(6, 29), (9, 29), (11, 31), (11, 37), (6, 37)]:
        p.prop('reeds', x, y)
    p.tree('willow', 3, 28)
    p.tree('willow', 6, 35)

    # Canopies follow authored groves. Route reservations prevent trunks closing paths.
    groves = [(2, 2, 14, 13), (32, 2, 43, 14), (2, 16, 9, 24),
              (34, 17, 44, 26), (2, 40, 16, 43), (34, 37, 44, 43),
              (2, 25, 5, 26), (14, 5, 17, 10), (36, 28, 44, 34)]
    for gx, gy, right, bottom in groves:
        for row, y in enumerate(range(gy, bottom+1, 3)):
            for col, x in enumerate(range(gx+(row%2), right+1, 3)):
                name = ('oak', 'fir', 'oak')[(row+col)%3]
                w, h = (4, 4) if name == 'oak' else (3, 4)
                if x+w >= p.w or y+h >= p.h:
                    continue
                tx, ty = x+w//2, y+h-1
                if any((tx+dx, ty+dy) in p.routes for dx in range(-1, 2) for dy in range(-1, 2)):
                    continue
                if p.m['Layers'][0][ty*p.w+tx] in (WATER, SHALLOW, DECK):
                    continue
                p.tree(name, x, y, w, h)
    for name, x, y in [('oak', 15, 24), ('fir', 17, 17), ('oak', 27, 15),
                       ('dead-tree', 30, 10), ('oak', 27, 27), ('fir', 19, 29),
                       ('oak', 14, 37), ('oak', 29, 36), ('fir', 10, 25)]:
        p.tree(name, x, y)
    for x, y in [(13, 28), (16, 26), (20, 19), (27, 18), (33, 25), (31, 29), (21, 38),
                 (26, 40), (12, 39), (35, 36), (17, 15), (31, 15), (13, 18)]:
        p.prop('fern', x, y)
    for x, y in [(13, 24), (18, 18), (29, 17), (33, 23), (16, 39), (30, 11)]:
        p.prop('mushrooms', x, y)
    for x, y in [(17, 20), (31, 17), (34, 37)]:
        p.prop('fallen-log', x, y)

    p.point(6, 'Lago dos esquecidos', 12, 34)
    p.point(7, 'Ervas amargas', 19, 37)
    p.point(8, 'Veio de ferro', 29, 34)
    p.point(9, 'Madeira antiga', 18, 31)
    p.point(10, 'Cristais de eco', 28, 22)
    p.point(3, 'Relicário do bosque', 15, 21, 97)
    p.point(4, 'Limiar do Eclipse', 24, 9, 102)
    for i, (x, y) in enumerate([(24, 32), (25, 27), (21, 24), (28, 18), (22, 14), (32, 21), (14, 26), (30, 30)]):
        p.m['Npcs'].append(dict(NpcId=[10, 11, 12][i%3], X=x, Y=y))
        p.blocked[y*p.w+x] = 0
    p.warp(range(22, 27), 43, 1, (22, 6))
    return p.finish()


def authored_maps():
    return [village(), forest()]
