"""Regenerate the extension's MSIX asset set from repo-search-icon-256.png.

Run after changing the icon:

    python assets-source/generate-assets.py

Requires Pillow. Source art lives beside this script; output goes to
src/RepoSearch.Extension/Assets.
"""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "repo-search-icon-256.png")
OUT = os.path.normpath(os.path.join(HERE, "..", "src", "RepoSearch.Extension", "Assets"))

src = Image.open(SRC).convert("RGBA")
print(f"source: {src.size} mode={src.mode}")


def square(size):
    return src.resize((size, size), Image.LANCZOS)


def centred(width, height, coverage=0.62):
    """Tile centred on a transparent canvas, for the wide tile and splash screen -
    a square logo must never be stretched to fill them."""
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    edge = int(min(width, height) * coverage)
    tile = square(edge)
    canvas.paste(tile, ((width - edge) // 2, (height - edge) // 2), tile)
    return canvas


# Standard MSIX logo sizes at scale-100; each also gets a 2x .scale-200 variant.
SQUARE = {
    "StoreLogo": 50,
    "Square44x44Logo": 44,
    "Square150x150Logo": 150,
    "LockScreenLogo": 24,
}
WIDE = {
    "Wide310x150Logo": (310, 150),
    "SplashScreen": (620, 300),
}

os.makedirs(OUT, exist_ok=True)
written = []

for name, size in SQUARE.items():
    for suffix, factor in ((".png", 1), (".scale-200.png", 2)):
        img = square(size * factor)
        img.save(os.path.join(OUT, name + suffix), "PNG")
        written.append((name + suffix, img.size))

for name, (w, h) in WIDE.items():
    for suffix, factor in ((".png", 1), (".scale-200.png", 2)):
        img = centred(w * factor, h * factor)
        img.save(os.path.join(OUT, name + suffix), "PNG")
        written.append((name + suffix, img.size))

# The 24px unplated variant Windows uses for small chrome.
name = "Square44x44Logo.targetsize-24_altform-unplated.png"
square(24).save(os.path.join(OUT, name), "PNG")
written.append((name, (24, 24)))

# Full-resolution copy for the palette itself: Command Palette renders the provider and
# page icon much larger than the 50px StoreLogo, so give it the whole 256px tile.
name = "RepoSearchIcon.png"
src.save(os.path.join(OUT, name), "PNG")
written.append((name, src.size))

for name, size in sorted(written):
    print(f"  {name:<52} {size[0]}x{size[1]}")
print(f"\n{len(written)} files written to {OUT}")
