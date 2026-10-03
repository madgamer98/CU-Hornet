"""For every tk2dSpriteAnimation in Silksong's hero bundles, print its GameObject
name (which is how the game labels the crest/set) and the sprite collection its
first frame uses."""
import glob
import os

import UnityPy

UnityPy.config.FALLBACK_UNITY_VERSION = "6000.0.50f1"
AA = r"%SILKSONG_DIR%\Hollow Knight Silksong_Data\StreamingAssets\aa\StandaloneWindows64"
PATHS = glob.glob(os.path.join(AA, "herocollections_assets_*.bundle")) + [
    os.path.join(AA, "herodynamic_assets_all.bundle"),
    os.path.join(AA, "herostatic_assets_all.bundle"),
]


def main():
    env = UnityPy.load(*PATHS)
    index = {}
    for f in env.files.values():
        for cname, sf in (getattr(f, "files", {}) or {}).items():
            for pid, o in getattr(sf, "objects", {}).items():
                index[pid] = o

    def go_name(mono_tt):
        p = mono_tt.get("m_GameObject")
        if isinstance(p, dict):
            o = index.get(p.get("m_PathID"))
            if o is not None:
                try:
                    return o.read().m_Name
                except Exception:
                    return "?"
        return "?"

    def coll_name(pptr):
        o = index.get(pptr.get("m_PathID")) if isinstance(pptr, dict) else None
        if o is None:
            return None
        try:
            tt = o.read_typetree()
        except Exception:
            return "?"
        return tt.get("spriteCollectionName") or "?"

    anims = []
    for pid, o in index.items():
        if o.type.name != "MonoBehaviour":
            continue
        try:
            tt = o.read_typetree()
        except Exception:
            continue
        cl = tt.get("clips")
        if not isinstance(cl, list):
            continue
        first = cl[0] if cl and isinstance(cl[0], dict) else {}
        f0 = (first.get("frames") or [{}])[0]
        anims.append((go_name(tt), len(cl), first.get("name"), coll_name(f0.get("spriteCollection", {}))))
    anims.sort(key=lambda a: -a[1])
    print("%-34s %5s %-16s %s" % ("gameobject", "clips", "firstclip", "frame0 collection"))
    for a in anims:
        print("%-34s %5d %-16s %s" % (a[0], a[1], str(a[2]), a[3]))


if __name__ == "__main__":
    main()
