"""Debug: print the first Idle frame's sprite-definition fields and atlas size."""
import glob
import os

import UnityPy

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


body = anim = None
for pid, o in index.items():
    if o.type.name != "MonoBehaviour":
        continue
    try:
        tt = o.read_typetree()
    except Exception:
        continue
    if tt.get("spriteCollectionName") == "Hornet Cloakless Cln":
        body = tt
    cl = tt.get("clips")
    if isinstance(cl, list) and len(cl) == 408:
        anim = cl

defs = body["spriteDefinitions"]
textures = body["textures"]
print("defs", len(defs), "textures", len(textures))
idle = [c for c in anim if isinstance(c, dict) and c.get("name") == "Idle"][0]
for i, fr in enumerate(idle["frames"][:3]):
    d = defs[fr["spriteId"]]
    print("frame", i, "spriteId", fr["spriteId"], "name", d.get("name"))
    for k in ("regionX", "regionY", "regionW", "regionH", "materialId", "flipped", "extractRegion", "texelSize"):
        print("   ", k, d.get(k))
    tex = resolve(textures[d.get("materialId", 0)])
    img = tex.read().image
    print("    texture", img.size, img.mode)
    print("    positions", d.get("positions"))
