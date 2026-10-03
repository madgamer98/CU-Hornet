"""Repair the bake.json produced before the manifest comma bug was fixed.

Removes leading/double commas and replaces non-finite floats. Usage:
  python tools/repair_bake.py <path-to-bake.json>
"""
import json
import sys

p = sys.argv[1]
s = open(p, encoding="utf-8").read()
s = s.replace('"frames":[,', '"frames":[')
s = s.replace("[,", "[")
s = s.replace(",,", ",")
for bad in ("NaN", "Infinity", "-Infinity"):
    s = s.replace(bad, "0")
open(p, "w", encoding="utf-8").write(s)

d = json.load(open(p, encoding="utf-8"))
print("clips", len(d["frames"]))
for k in sorted(d["frames"]):
    print(" ", k, len(d["frames"][k]["frames"]))
