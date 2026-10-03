"""Compare sprite-definition names between the cloakless (408-clip) body and the
cloaked `Hornet Cln` collection, to see if the full moveset can render cloaked."""
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


collections = {}
anim = None
for pid, o in index.items():
    if o.type.name != "MonoBehaviour":
        continue
    try:
        tt = o.read_typetree()
    except Exception:
        continue
    n = tt.get("spriteCollectionName")
    if n:
        collections[n] = tt
    cl = tt.get("clips")
    if isinstance(cl, list) and len(cl) == 408:
        anim = cl

for n in ("Hornet Cln", "Hornet Cloakless Cln"):
    defs = collections.get(n, {}).get("spriteDefinitions") or []
    names = [d.get("name") for d in defs]
    print("== %s: %d defs ==" % (n, len(names)))
    for nm in names[:35]:
        print("   ", nm)

cloaked = set(d.get("name") for d in (collections.get("Hornet Cln", {}).get("spriteDefinitions") or []))
print("== 408-clip Idle/Slash frame defs (cloakless) vs cloaked name match ==")
for c in anim:
    if not isinstance(c, dict) or c.get("name") not in ("Idle", "Slash", "Run", "Airborne"):
        continue
    for fr in (c.get("frames") or [])[:4]:
        cc = resolve(fr.get("spriteCollection", {}))
        try:
            ctt = cc.read_typetree()
        except Exception:
            continue
        nm = (ctt.get("spriteDefinitions") or [])[fr["spriteId"]].get("name")
        alt = nm.replace("_cloakless", "") if nm else nm
        print("   %-38s -> cloaked match %r" % (nm, alt in cloaked))
