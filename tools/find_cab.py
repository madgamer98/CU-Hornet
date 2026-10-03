"""Find which Silksong Addressables bundle contains a given CAB serialized-file name.

Bundle filenames don't match the internal CAB name, so grep the raw bytes.
Usage: python tools/find_cab.py CAB-xxxx [more...]
"""
import os
import sys

AA = r"%SILKSONG_DIR%\Hollow Knight Silksong_Data\StreamingAssets\aa"
CHUNK = 8 << 20


def scan(root, needles):
    found = {n: [] for n in needles}
    hits = 0
    for dirpath, _dirs, files in os.walk(root):
        for fn in files:
            if not fn.endswith(".bundle"):
                continue
            path = os.path.join(dirpath, fn)
            try:
                with open(path, "rb") as f:
                    tail = b""
                    while True:
                        buf = f.read(CHUNK)
                        if not buf:
                            break
                        data = tail + buf
                        for n in needles:
                            if n in data and len(found[n]) < 5:
                                found[n].append(os.path.relpath(path, root))
                        tail = data[-64:]
            except OSError:
                continue
            hits += 1
            if hits % 300 == 0:
                print(f"  ...scanned {hits} bundles", flush=True)
    return found


if __name__ == "__main__":
    needles = [a.encode() for a in sys.argv[1:]]
    res = scan(AA, needles)
    for n in sys.argv[1:]:
        print(n, "->", res[n.encode()])
