"""Extract Hornet's animation frames from the user's own Silksong install.

Reads a tk2d sprite collection and a tk2d animation library from Silksong's
Addressables bundles and writes cropped frame PNGs plus a manifest the
Casualties: Unknown plugin loads at runtime.

tk2d packs sprites rotated and/or flipped in the atlas, so each frame is rebuilt
by sampling the sprite's UV quad, not by a plain rectangular crop.

Nothing from Silksong is redistributed: this runs on the user's own install.

Usage:
  python tools/import_hornet.py --out <dir> --collection "Hornet Cln" \
      --clips Idle,Run,Jump,Fall,...
"""
import argparse
import glob
import json
import os

import numpy as np
import UnityPy
from PIL import Image

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


def sample_quad(atlas, uvs, positions, ppu):
    """Rebuild one sprite by sampling the atlas across its UV quad.

    positions are world-space corners in order [BL, BR, TL, TR]; uvs are the
    matching texture corners. Returns (RGBA image, pivot_x, pivot_y).
    """
    xs = [p["x"] for p in positions]
    ys = [p["y"] for p in positions]
    minx, maxx, miny, maxy = min(xs), max(xs), min(ys), max(ys)
    w = max(maxx - minx, 1e-6)
    h = max(maxy - miny, 1e-6)
    ow = max(1, int(round(w * ppu)))
    oh = max(1, int(round(h * ppu)))
    tex_h, tex_w = atlas.shape[0], atlas.shape[1]

    # (s, u) -> uv via bilinear over the four corners.
    s = (np.arange(ow)[None, :] + 0.5) / ow          # 1 x ow
    u = 1.0 - (np.arange(oh)[:, None] + 0.5) / oh    # oh x 1 (top row = max y)
    w00 = (1 - s) * (1 - u)
    w10 = s * (1 - u)
    w01 = (1 - s) * u
    w11 = s * u
    uu = (w00 * uvs[0]["x"] + w10 * uvs[1]["x"] + w01 * uvs[2]["x"] + w11 * uvs[3]["x"])
    vv = (w00 * uvs[0]["y"] + w10 * uvs[1]["y"] + w01 * uvs[2]["y"] + w11 * uvs[3]["y"])
    tx = np.clip(np.round(uu * (tex_w - 1)).astype(np.int32), 0, tex_w - 1)
    # UnityPy returns the atlas with row 0 = Unity v 0, so map v straight to the row.
    ty = np.clip(np.round(vv * (tex_h - 1)).astype(np.int32), 0, tex_h - 1)
    out = atlas[ty, tx]
    pivot_x = (0.0 - minx) / w
    pivot_y = (0.0 - miny) / h
    img = Image.fromarray(out, "RGBA").transpose(Image.ROTATE_90)
    # tk2d stores these sprites rotated 90 degrees; rotate the frame and pivot back.
    pivot_x, pivot_y = 1.0 - pivot_y, pivot_x
    return img, pivot_x, pivot_y


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--collection", default=DEFAULT_COLLECTION)
    ap.add_argument("--clips", default=",".join(DEFAULT_CLIPS))
    ap.add_argument("--anim-count", type=int, default=0, help="0 = match by collection")
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
            first = cl[0] if isinstance(cl[0], dict) else {}
            f0 = (first.get("frames") or [{}])[0]
            ctt0 = coll_of(f0.get("spriteCollection", {}))
            if ctt0 is not None and ctt0.get("spriteCollectionName") == args.collection:
                if args.anim_count <= 0 or len(cl) == args.anim_count:
                    anim = cl

    if body is None:
        raise SystemExit("collection not found: " + args.collection)
    if anim is None:
        raise SystemExit("animation library for %r not found" % args.collection)

    defs = body.get("spriteDefinitions") or []
    textures = body.get("textures") or []
    print("collection %r: %d defs, %d textures" % (args.collection, len(defs), len(textures)))

    tex_cache = {}

    def atlas_for(material_id):
        if material_id not in tex_cache:
            o = resolve(textures[material_id])
            if o is None:
                raise SystemExit("texture %d not resolvable" % material_id)
            img = o.read().image.convert("RGBA")
            tex_cache[material_id] = np.array(img)
        return tex_cache[material_id]

    out_dir = args.out
    frames_dir = os.path.join(out_dir, "frames")
    os.makedirs(frames_dir, exist_ok=True)

    manifest = {"ppu": None, "clips": {}}
    by_name = {}
    for c in anim:
        if isinstance(c, dict) and c.get("name"):
            by_name[c["name"]] = c

    for name in wanted:
        c = by_name.get(name)
        if c is None:
            print("  skip (no clip):", name)
            continue
        out_frames = []
        ok = True
        for i, fr in enumerate(c.get("frames") or []):
            ctt = coll_of(fr.get("spriteCollection", {}))
            if ctt is None or ctt.get("spriteCollectionName") != args.collection:
                ok = False
                break
            sid = fr.get("spriteId")
            if sid is None or sid >= len(ctt.get("spriteDefinitions") or []):
                ok = False
                break
            d = ctt["spriteDefinitions"][sid]
            texel = (d.get("texelSize") or {}).get("x") or 0.015625
            ppu = int(round(1.0 / texel))
            atlas = atlas_for(d.get("materialId", 0))
            img, px_, py_ = sample_quad(atlas, d.get("uvs") or [], d.get("positions") or [], ppu)
            fname = "%s_%02d.png" % (name.replace(" ", "_"), i)
            img.save(os.path.join(frames_dir, fname))
            out_frames.append({"file": "frames/" + fname, "pivotX": px_, "pivotY": py_,
                               "w": img.width, "h": img.height})
            manifest["ppu"] = ppu
        if not ok or not out_frames:
            print("  skip (mixed/empty):", name)
            continue
        manifest["clips"][name] = {
            "fps": float(c.get("fps") or 12.0),
            "loop": c.get("wrapMode") == 0,
            "frames": out_frames,
        }
        print("  %-18s %2d frames @ %.1f fps" % (name, len(out_frames), float(c.get("fps") or 12)))

    with open(os.path.join(out_dir, "hornet.json"), "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=1)
    print("wrote", os.path.join(out_dir, "hornet.json"), "ppu", manifest["ppu"],
          "clips", len(manifest["clips"]))


if __name__ == "__main__":
    main()
