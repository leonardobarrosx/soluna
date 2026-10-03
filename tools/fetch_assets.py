"""
Downloads the LPC base tiles Soluna ships with and writes their licence note.

They are CC-BY-SA 3.0 / GPL 3.0 (much of it also OGA-BY 3.0), which allows redistribution
with credit; CREDITS.TXT from the pack lands next to the files. Characters come from a
different source, see tools/extract_chibi.py.

Usage: python tools/fetch_assets.py   (from anywhere; needs only the standard library)
"""

import io
import pathlib
import urllib.request
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
BASE_ASSETS_ZIP = "https://opengameart.org/sites/default/files/lpc_base_assets.zip"


def get(url: str) -> bytes:
    with urllib.request.urlopen(url, timeout=60) as response:
        return response.read()


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
    fetch_tiles()
