"""Write import metadata for the authored art pages. Reads PNGs; never edits image pixels.

The original generated images remain under assets/atlases. Engine-side frame import
uses these measured rectangles to keep poses aligned despite source-page gutters.
Requires Pillow for reading image dimensions and alpha bounds only.
"""
from pathlib import Path
import json
import shutil
from PIL import Image

ROOT = Path(__file__).resolve().parents[1] / 'games/soluna/assets'


def write(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n')


def rectangles(page, xs, ys):
    """Measure visible bounds within manually reviewed cells; do not change the PNG."""
    with Image.open(ROOT / page) as image:
        alpha = image.convert('RGBA').getchannel('A')
        result = []
        for y0, y1 in zip(ys, ys[1:]):
            for x0, x1 in zip(xs, xs[1:]):
                # Ignore the barely visible outer fringe of generated light effects.
                box = alpha.crop((x0, y0, x1, y1)).point(lambda a: 255 if a > 48 else 0).getbbox()
                if box is None:
                    raise ValueError(f'Empty frame in {page}: {x0}, {y0}')
                l, t, r, b = box
                result.append([x0 + l, y0 + t, r - l, b - t])
        return result


def patch(page, cols, rows, source, target=None, rects=None, inset=2):
    p = {'File': page, 'Columns': cols, 'Rows': rows, 'Source': source,
         'Target': list(range(len(source))) if target is None else target}
    if rects is not None:
        p.update(Rects=[rects[i] for i in source], Fit=True, Inset=inset)
    return p


def sheet(name, page, source, rects, cols=6, rows=8, size=32):
    target = ROOT / f'characters/echoes/{name}.png'
    if not target.exists():
        shutil.copyfile(ROOT / 'characters/echoes/blank.png', target)
    write(Path(str(target) + '.layout.json'), {
        'Columns': 3, 'Rows': 4, 'CellWidth': size, 'CellHeight': size,
        'Patches': [patch(page, cols, rows, source, rects=rects)]})


def block(col, row):
    return [(row + y) * 6 + col + x for y in range(4) for x in range(3)]


creatures = rectangles('atlases/creatures.png', [0, 181, 362, 543, 724, 905, 1086],
                       [0, 184, 359, 534, 720, 912, 1086, 1260, 1448])
sheet('wisp', 'atlases/creatures.png', block(0, 0), creatures)
sheet('thorn', 'atlases/creatures.png', block(3, 0), creatures)
# The art page puts the hound's right-facing row first; map it to the engine's left/right order.
sheet('cinder', 'atlases/creatures.png', [24, 25, 26, 36, 37, 38, 30, 31, 32, 42, 43, 44], creatures)
# Select the reviewed side-facing guard poses; exclude a stray back-facing pose in the side row.
sheet('guardian', 'atlases/creatures.png', [27, 28, 29, 39, 41, 39, 33, 34, 35, 45, 46, 47], creatures, size=48)

forms = rectangles('atlases/forms.png', [40, 223, 372, 548, 729, 870, 1045],
                   [0, 213, 397, 588, 782, 974, 1146, 1310, 1448])
for name, col, row in [('hood', 0, 0), ('robe', 3, 0), ('blade', 0, 4), ('plate', 3, 4)]:
    sheet(name, 'atlases/forms.png', block(col, row), forms)
blank = rectangles('atlases/blank.png', [100, 400, 680, 1000], [70, 404, 727, 1055, 1380])
sheet('blank', 'atlases/blank.png', list(range(12)), blank, cols=3, rows=4)
write(ROOT / 'characters/echoes/forms.json', {
    name: f'characters/echoes/{name}.png' for name in ['blank', 'hood', 'robe', 'blade', 'plate']})

villagers = rectangles('atlases/villagers.png', [0, 256, 512, 768, 1024, 1280, 1536], [0, 280, 523, 766, 1024])
# A few generated third poses face the opposite way. Reuse the correct step instead of flickering directions.
sheet('merchant', 'atlases/villagers.png', [0, 1, 2, 6, 7, 6, 12, 13, 12, 18, 19, 20], villagers, rows=4)
sheet('keeper', 'atlases/villagers.png', [3, 4, 5, 9, 10, 9, 15, 16, 15, 21, 22, 23], villagers, rows=4)

objects = rectangles('atlases/objects.png', [0, 315, 635, 950, 1254], [0, 333, 636, 918, 1254])
write(ROOT / 'tilesets/soluna/atlas.png.layout.json', {
    'Columns': 16, 'Rows': 8, 'CellWidth': 32, 'CellHeight': 32,
    'Patches': [patch('atlases/terrain.png', 4, 4, list(range(16))),
                patch('atlases/objects.png', 4, 4, list(range(16)), list(range(96, 112)), objects, inset=1)],
    'Terrain': {'GrassTile': 0, 'Grass': [0, 1, 8, 9, 10, 11, 12],
                'BlendInto': [2, 3, 4, 13, 14], 'Water': [4]}})
print('Import metadata written: 16 surfaces, 16 objects, 11 character sheets; source PNGs unchanged.')
