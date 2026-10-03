"""Search Hornet's 408-clip library and the body collection for cloak/hunter entries."""
import glob
import os
from collections import Counter

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


def coll_of(p):
    o = resolve(p)
    try:
        return o.read_typetree().get("spriteCollectionName")
    except Exception:
        return None


anim = None
body = None
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

print("== clip names containing cloak/hunter/weapon ==")
for c in anim:
    n = c.get("name") if isinstance(c, dict) else None
    if n and any(k in n.lower() for k in ("cloak", "hunter", "weapon", "crest")):
        print("  ", repr(n))

print("== distinct collections referenced across the 408 clips ==")
cnt = Counter()
for c in anim:
    for fr in (c.get("frames") or []):
        cnt[coll_of(fr.get("spriteCollection", {}))] += 1
for k, v in cnt.most_common():
    print("   %-30s %d" % (k, v))

print("== body defs containing cloak/hunter ==")
for d in (body.get("spriteDefinitions") or []):
    n = d.get("name") or ""
    if "cloak" in n.lower() or "hunter" in n.lower():
        print("  ", n)
print("== collection names with cloak/hunter across hero bundles ==")
for pid, o in index.items():
    if o.type.name != "MonoBehaviour":
        continue
    try:
        tt = o.read_typetree()
    except Exception:
        continue
    n = tt.get("spriteCollectionName")
    if n and ("cloak" in n.lower() or "hunter" in n.lower()):
        print("  ", n, "defs", len(tt.get("spriteDefinitions") or []))
