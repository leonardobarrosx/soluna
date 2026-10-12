"""Rebuild only the six authored example maps; leave rules and player data alone.

Run configure_soluna_art.py first after changing source-page crop metadata.
The optional --maps-only flag remains accepted for older setup scripts.
"""
import json
from pathlib import Path
from soluna_maps import authored_maps

ROOT = Path(__file__).resolve().parents[1] / 'games/soluna/data/maps'
ROOT.mkdir(parents=True, exist_ok=True)
for m in authored_maps():
    (ROOT / f'{m["Id"]}.json').write_text(json.dumps(m, ensure_ascii=False, separators=(',', ':')))
print('Rebuilt six public maps. Adventure, evolution, items, NPCs and player data untouched.')
