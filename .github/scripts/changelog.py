#!/usr/bin/env python3
"""Reads CHANGELOG.md.

  changelog.py VERSION    prints that version's section (exit 1 if there is none)
  changelog.py --versions prints every version that has a section, one per line
"""
import re
import sys

with open("CHANGELOG.md", encoding="utf-8") as f:
    text = f.read()

sections = {}
current = None
for line in text.splitlines():
    m = re.match(r"^##\s+v?([0-9][0-9A-Za-z.\-+]*)", line)
    if m:
        current = m.group(1)
        sections[current] = []
    elif line.startswith("# "):
        current = None
    elif current is not None:
        sections[current].append(line)

if len(sys.argv) > 1 and sys.argv[1] == "--versions":
    print("\n".join(sections))
    sys.exit(0)

version = sys.argv[1] if len(sys.argv) > 1 else ""
body = "\n".join(sections.get(version, [])).strip()
if not body:
    sys.exit(1)
print(body)
