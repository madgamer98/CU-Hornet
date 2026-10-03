"""Print raw positions/uvs for the first Idle frames of Hornet Cln."""
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
for _pid, o in index.items():
    if o.type.name != "MonoBehaviour":
        continue
    try:
        tt = o.read_typetree()
    except Exception:
        continue
    if tt.get("spriteCollectionName") == "Hornet Cln":
        body = tt
    cl = tt.get("clips")
    if isinstance(cl, list) and cl:
        f0 = (cl[0].get("frames") or [{}])[0]
        cc = resolve(f0.get("spriteCollection", {}))
        try:
            if cc.read_typetree().get("spriteCollectionName") == "Hornet Cln":
                anim = cl
        except Exception:
            pass

defs = body["spriteDefinitions"]
idle = [c for c in anim if c.get("name") == "Idle"][0]
for i, fr in enumerate(idle["frames"][:3]):
    d = defs[fr["spriteId"]]
    print("frame", i, "name", d.get("name"), "flipped", d.get("flipped"))
    print("  pos ", [(round(p["x"], 3), round(p["y"], 3)) for p in d["positions"]])
    print("  uvs ", [(round(p["x"], 4), round(p["y"], 4)) for p in d["uvs"]])
