"""Dump the Hornet Cln atlas and the raw stored region of idle0000 to see the tk2d layout."""
import glob
import os

import numpy as np
import UnityPy
from PIL import Image

UnityPy.config.FALLBACK_UNITY_VERSION = "6000.0.50f1"
AA = r"%SILKSONG_DIR%\Hollow Knight Silksong_Data\StreamingAssets\aa\StandaloneWindows64"
PATHS = glob.glob(os.path.join(AA, "herocollections_assets_*.bundle")) + [
    os.path.join(AA, "herodynamic_assets_all.bundle")
]
env = UnityPy.load(*PATHS)
index = {}
for f in env.files.values():
    for _c, sf in (getattr(f, "files", {}) or {}).items():
        for pid, o in getattr(sf, "objects", {}).items():
            index[pid] = o


def resolve(p):
    return index.get(p.get("m_PathID")) if isinstance(p, dict) else None


body = None
for _pid, o in index.items():
    if o.type.name != "MonoBehaviour":
        continue
    try:
        tt = o.read_typetree()
    except Exception:
        continue
    if tt.get("spriteCollectionName") == "Hornet Cln":
        body = tt
        break

tex = resolve(body["textures"][0])
img = tex.read().image.convert("RGBA")
arr = np.array(img)
print("atlas", img.size)
Image.fromarray(arr).resize((img.width // 2, img.height // 2), Image.NEAREST).save(r"shots\atlas_half.png")

d = [x for x in body["spriteDefinitions"] if x.get("name") == "idle0000"][0]
us = [p["x"] for p in d["uvs"]]
vs = [p["y"] for p in d["uvs"]]
u0, u1 = min(us), max(us)
v0, v1 = min(vs), max(vs)
x0, x1 = int(u0 * img.width), int(u1 * img.width)
# top-down PIL coords
yt = int((1 - v1) * img.height)
yb = int((1 - v0) * img.height)
Image.fromarray(arr[yt:yb, x0:x1]).resize(((x1 - x0) * 2, (yb - yt) * 2), Image.NEAREST).save(r"shots\idle0000_topdown.png")
# bottom-up PIL coords
yt2 = int(v0 * img.height)
yb2 = int(v1 * img.height)
Image.fromarray(arr[yt2:yb2, x0:x1]).resize(((x1 - x0) * 2, (yb2 - yt2) * 2), Image.NEAREST).save(r"shots\idle0000_bottomup.png")
print("idle0000 uv", u0, u1, v0, v1, "regions", (x0, x1, yt, yb), (x0, x1, yt2, yb2))
