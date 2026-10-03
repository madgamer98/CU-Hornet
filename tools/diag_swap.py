"""Try transposed vs normal atlas sampling for idle0000 and save both."""
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
arr = np.array(tex.read().image.convert("RGBA"))
H, W = arr.shape[0], arr.shape[1]
d = [x for x in body["spriteDefinitions"] if x.get("name") == "idle0000"][0]
uvs = [(p["x"], p["y"]) for p in d["uvs"]]
pos = [(p["x"], p["y"]) for p in d["positions"]]
minx = min(p[0] for p in pos); maxx = max(p[0] for p in pos)
miny = min(p[1] for p in pos); maxy = max(p[1] for p in pos)
ow, oh = int(round((maxx - minx) * 64)), int(round((maxy - miny) * 64))


def sample(swap):
    s = (np.arange(ow)[None, :] + 0.5) / ow
    u = 1.0 - (np.arange(oh)[:, None] + 0.5) / oh
    w00 = (1 - s) * (1 - u); w10 = s * (1 - u); w01 = (1 - s) * u; w11 = s * u
    uu = (w00 * uvs[0][0] + w10 * uvs[1][0] + w01 * uvs[2][0] + w11 * uvs[3][0])
    vv = (w00 * uvs[0][1] + w10 * uvs[1][1] + w01 * uvs[2][1] + w11 * uvs[3][1])
    ax = np.clip(np.round(uu * (W - 1)).astype(np.int32), 0, W - 1)
    ay = np.clip(np.round(vv * (H - 1)).astype(np.int32), 0, H - 1)
    out = arr[ax, ay] if swap else arr[ay, ax]
    return Image.fromarray(out, "RGBA")


a = sample(False)
b = sample(True)
sheet = Image.new("RGBA", (a.width + b.width + 24, max(a.height, b.height) + 24), (20, 20, 30, 255))
sheet.alpha_composite(a, (8, 16))
sheet.alpha_composite(b, (a.width + 16, 16))
sheet = sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST)
sheet.convert("RGB").save(r"shots\swap_test.png")
print("normal left, transposed right", sheet.size)
