"""Probe Silksong hero bundles: list tk2d sprite collections and animation sets,
and which collection each animation's first frame points at.

Usage: python tools/probe_hero.py
"""
import glob
import os

import UnityPy

UnityPy.config.FALLBACK_UNITY_VERSION = "6000.0.50f1"

AA = r"%SILKSONG_DIR%\Hollow Knight Silksong_Data\StreamingAssets\aa\StandaloneWindows64"
PATHS = glob.glob(os.path.join(AA, "herocollections_assets_*.bundle")) + [
    os.path.join(AA, "herodynamic_assets_all.bundle")
]


def build_index(env):
    index = {}
    for fname, f in env.files.items():
        for cname, sf in (getattr(f, "files", {}) or {}).items():
            for pid, o in getattr(sf, "objects", {}).items():
                index[pid] = o
    return index


def main():
    env = UnityPy.load(*PATHS)
    index = build_index(env)

    def resolve(pptr):
        return index.get(pptr.get("m_PathID")) if isinstance(pptr, dict) else None

    def coll_name(o):
        try:
            tt = o.read_typetree()
        except Exception:
            return "?"
        return tt.get("spriteCollectionName") or tt.get("assetName") or tt.get("m_Name") or "?"

    print("== tk2d sprite collections ==")
    for pid, o in index.items():
        if o.type.name != "MonoBehaviour":
            continue
        try:
            tt = o.read_typetree()
        except Exception:
            continue
        if "spriteDefinitions" in tt:
            defs = tt.get("spriteDefinitions") or []
            print("  defs=%-5d name=%r asset=%r" % (len(defs), tt.get("spriteCollectionName"), tt.get("assetName")))

    print("== tk2d animations (>=20 clips) ==")
    for pid, o in index.items():
        if o.type.name != "MonoBehaviour":
            continue
        try:
            tt = o.read_typetree()
        except Exception:
            continue
        cl = tt.get("clips")
        if not (isinstance(cl, list) and len(cl) >= 20):
            continue
        first = cl[0] if isinstance(cl[0], dict) else {}
        f0 = (first.get("frames") or [{}])[0]
        coll = resolve(f0.get("spriteCollection", {}))
        print("  clips=%-4d firstclip=%r frame0_coll=%s" % (len(cl), first.get("name"), coll_name(coll) if coll else None))


if __name__ == "__main__":
    main()
