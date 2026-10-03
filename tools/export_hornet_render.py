"""Export Hornet's atlas + per-frame render quads from the user's Silksong install.

Instead of reconstructing pixels, this writes the atlas texture(s) and, for every
frame, the four geometry positions and matching atlas UVs exactly as tk2d stores
them. The CU plugin draws each frame as a quad mesh, so rotated/flipped atlas
packing is reproduced correctly and works for every crest.

Usage:
  python tools/export_hornet_render.py --out <dir> --collection "Hornet Cln" \
      --clips Idle,Run,Jump,Fall,...
"""
import argparse
import glob
import json
import os

import UnityPy

UnityPy.config.FALLBACK_UNITY_VERSION = "6000.0.50f1"

AA = r"%SILKSONG_DIR%\Hollow Knight Silksong_Data\StreamingAssets\aa\StandaloneWindows64"
PATHS = glob.glob(os.path.join(AA, "herocollections_assets_*.bundle")) + [
    os.path.join(AA, "herodynamic_assets_all.bundle")
]

DEFAULT_COLLECTION = "Hornet Cln"
DEFAULT_CLIPS = [
    "Idle", "Turn", "Run", "Jump", "Jump Full", "Fall", "Soft Land",
    "Evade", "Throw Side", "Harpoon Side",
]


def build_index(env):
    index = {}
    for f in env.files.values():
        for _cname, sf in (getattr(f, "files", {}) or {}).items():
            for pid, o in getattr(sf, "objects", {}).items():
                index[pid] = o
    return index


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--collection", default=DEFAULT_COLLECTION)
    ap.add_argument("--clips", default=",".join(DEFAULT_CLIPS))
    args = ap.parse_args()

    wanted = [c.strip() for c in args.clips.split(",") if c.strip()]
    env = UnityPy.load(*PATHS)
    index = build_index(env)

    def resolve(pptr):
        return index.get(pptr.get("m_PathID")) if isinstance(pptr, dict) else None

    def coll_of(pptr):
        o = resolve(pptr)
        if o is None:
            return None
        try:
            return o.read_typetree()
        except Exception:
            return None

    body = None
    anim = None
    for _pid, o in index.items():
        if o.type.name != "MonoBehaviour":
            continue
        try:
            tt = o.read_typetree()
        except Exception:
            continue
        if tt.get("spriteCollectionName") == args.collection:
            body = tt
        cl = tt.get("clips")
        if isinstance(cl, list) and cl:
            f0 = (cl[0].get("frames") or [{}])[0]
            ctt0 = coll_of(f0.get("spriteCollection", {}))
            if ctt0 is not None and ctt0.get("spriteCollectionName") == args.collection:
                anim = cl

    if body is None or anim is None:
        raise SystemExit("collection/animation not found: " + args.collection)

    defs = body.get("spriteDefinitions") or []
    textures = body.get("textures") or []
    out_dir = args.out
    os.makedirs(out_dir, exist_ok=True)

    materials = []
    mat_to_index = {}

    def material_index(material_id):
        if material_id in mat_to_index:
            return mat_to_index[material_id]
        o = resolve(textures[material_id])
        if o is None:
            raise SystemExit("texture %d not resolvable" % material_id)
        t = o.read()
        tex = t.image.convert("RGBA")
        fname = "atlas_%d.png" % material_id
        tex.save(os.path.join(out_dir, fname))
        idx = len(materials)
        materials.append({"file": fname, "w": tex.width, "h": tex.height})
        mat_to_index[material_id] = idx
        return idx

    by_name = {}
    for c in anim:
        if isinstance(c, dict) and c.get("name"):
            by_name[c["name"]] = c

    manifest = {"materials": materials, "clips": {}}
    for name in wanted:
        c = by_name.get(name)
        if c is None:
            print("  skip (no clip):", name)
            continue
        frames = []
        ok = True
        for fr in c.get("frames") or []:
            ctt = coll_of(fr.get("spriteCollection", {}))
            if ctt is None or ctt.get("spriteCollectionName") != args.collection:
                ok = False
                break
            sid = fr.get("spriteId")
            if sid is None or sid >= len(ctt.get("spriteDefinitions") or []):
                ok = False
                break
            d = ctt["spriteDefinitions"][sid]
            mi = material_index(d.get("materialId", 0))
            pos = []
            for p in d.get("positions") or []:
                pos.extend([round(p["x"], 5), round(p["y"], 5)])
            uv = []
            for p in d.get("uvs") or []:
                uv.extend([round(p["x"], 6), round(p["y"], 6)])
            if len(pos) != 8 or len(uv) != 8:
                ok = False
                break
            frames.append({"mat": mi, "pos": pos, "uv": uv})
        if not ok or not frames:
            print("  skip (mixed/empty):", name)
            continue
        manifest["clips"][name] = {
            "fps": float(c.get("fps") or 12.0),
            "loop": c.get("wrapMode") == 0,
            "frames": frames,
        }
        print("  %-18s %2d frames @ %.1f fps" % (name, len(frames), float(c.get("fps") or 12)))

    with open(os.path.join(out_dir, "render.json"), "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=1)
    print("wrote", os.path.join(out_dir, "render.json"), "materials", len(materials),
          "clips", len(manifest["clips"]))


if __name__ == "__main__":
    main()
