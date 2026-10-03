"""List a Hornet animation set's clips and the collections it references.

Usage: python tools/list_clips.py [collection-substring]
Default: base Hornet set (frame0 collection == 'Hornet Cln').
"""
import glob
import os
import sys
from collections import Counter

import UnityPy

UnityPy.config.FALLBACK_UNITY_VERSION = "6000.0.50f1"
AA = r"%SILKSONG_DIR%\Hollow Knight Silksong_Data\StreamingAssets\aa\StandaloneWindows64"
PATHS = glob.glob(os.path.join(AA, "herocollections_assets_*.bundle")) + [
    os.path.join(AA, "herodynamic_assets_all.bundle")
]


def main():
    want = sys.argv[1] if len(sys.argv) > 1 else "Hornet Cln"
    env = UnityPy.load(*PATHS)
    index = {}
    for f in env.files.values():
        for cname, sf in (getattr(f, "files", {}) or {}).items():
            for pid, o in getattr(sf, "objects", {}).items():
                index[pid] = o

    def coll_name(pptr):
        o = index.get(pptr.get("m_PathID")) if isinstance(pptr, dict) else None
        if o is None:
            return None
        try:
            return o.read_typetree().get("spriteCollectionName")
        except Exception:
            return "?"

    anims = []
    for pid, o in index.items():
        if o.type.name != "MonoBehaviour":
            continue
        try:
            tt = o.read_typetree()
        except Exception:
            continue
        cl = tt.get("clips")
        if isinstance(cl, list) and cl:
            first = cl[0] if isinstance(cl[0], dict) else {}
            f0 = (first.get("frames") or [{}])[0]
            anims.append((cl, coll_name(f0.get("spriteCollection", {}))))

    for cl, cn in anims:
        if cn != want:
            continue
        print("### animation set -> %s (%d clips)" % (cn, len(cl)))
        colls = Counter()
        for c in cl:
            if not isinstance(c, dict):
                continue
            print("%-40s fps=%-8.2f frames=%-3d" % (c.get("name"), c.get("fps") or 0, len(c.get("frames") or [])))
            for fr in (c.get("frames") or []):
                colls[coll_name(fr.get("spriteCollection", {}))] += 1
        print("collections used:", dict(colls))


if __name__ == "__main__":
    main()
