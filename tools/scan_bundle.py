"""Inspect a Silksong Addressables bundle with UnityPy.

Prints object type counts and names of interesting objects (Texture2D, Sprite,
AnimationClip, and MonoBehaviours with a readable m_Name / script). Used to find
Hornet's sprite atlases and animation clips without dumping the whole 7.7 GB.

Usage: python tools/scan_bundle.py <bundle> [<bundle> ...]
"""
import sys
from collections import Counter

import UnityPy

# Silksong is Unity 6000.0.50f1. Addressables bundles don't always embed a version
# header UnityPy can read, so give it the fallback explicitly.
UnityPy.config.FALLBACK_UNITY_VERSION = "6000.0.50f1"


def mono_info(obj):
    """Return (name, script_name) for a MonoBehaviour if the typetree allows it."""
    name = None
    script = None
    try:
        tt = obj.read_typetree()
        if isinstance(tt, dict):
            name = tt.get("m_Name")
    except Exception:
        tt = None
    try:
        m = obj.read()
        s = getattr(m, "m_Script", None)
        if s is not None:
            try:
                script = s.read().m_Name
            except Exception:
                script = str(s)
    except Exception:
        pass
    return name, script


def scan(path):
    print("=" * 100)
    print(path)
    env = UnityPy.load(path)
    counts = Counter()
    textures = []
    sprites = []
    clips = []
    monos = []
    for obj in env.objects:
        t = obj.type.name
        counts[t] += 1
        try:
            if t == "Texture2D":
                d = obj.read()
                textures.append((d.m_Name, d.m_Width, d.m_Height))
            elif t == "Sprite":
                d = obj.read()
                sprites.append((d.m_Name,))
            elif t == "AnimationClip":
                d = obj.read()
                clips.append((d.m_Name, getattr(d, "m_Legacy", None)))
            elif t == "MonoBehaviour":
                name, script = mono_info(obj)
                monos.append((name, script))
        except Exception as e:
            monos.append((f"<err {e}>", None))

    print("-- type counts --")
    for k, v in counts.most_common():
        print(f"  {k:24} {v}")
    print(f"-- textures ({len(textures)}) --")
    for n, w, h in textures[:40]:
        print(f"  {n}  {w}x{h}")
    print(f"-- clips ({len(clips)}) --")
    for n, leg in clips[:60]:
        print(f"  {n}")
    print(f"-- MonoBehaviours (first 80; name | script) --")
    for n, s in monos[:80]:
        print(f"  {n} | {s}")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        scan(p)
