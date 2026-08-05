#!/usr/bin/env python3
"""
AL inventory + collision detector + renumberer.

Walks an AL test corpus root, parses every .al file for top-level object
declarations, and produces:

  inventory       — list of (kind, id, name, file, line) tuples
  collisions      — (kind, id) and (kind, name) pairs that appear in 2+ files

Optional --renumber rewrites object IDs in-place: each kind gets a
sequential block starting at --start (default 50000), assigned in
file-path-sorted order for stability. ID-based references inside AL
(`Codeunit.Run(<int>)`, `Page.Run(<int>, ...)`, `Report.Run(<int>, ...)`,
`Database::<int>`) are also rewritten when the int matches an ID we
renumbered for that kind.

Names are renamed only when they collide within a kind (rare); the
second+ occurrence gets `_<orig-id>` appended.

Usage:
  scripts/al-inventory.py <root>                       # report only
  scripts/al-inventory.py <root> --renumber [--start 50000]
  scripts/al-inventory.py <root> --json out.json       # dump inventory

Object kinds covered:
  codeunit, table, tableextension, page, pageextension, report,
  reportextension, enum, enumextension, query, queryextension,
  xmlport, interface
"""
import argparse
import json
import os
import re
import sys
from pathlib import Path
from collections import defaultdict

KINDS = [
    "codeunit", "table", "tableextension", "page", "pageextension",
    "report", "reportextension", "enum", "enumextension", "query",
    "queryextension", "xmlport", "interface",
]

# Match object decl line. interface has no ID. Quoted name is mandatory
# for reliable parsing; unquoted single-word names also accepted.
KIND_ALT = "|".join(KINDS)
RE_DECL = re.compile(
    r'^(?P<indent>\s*)(?P<kind>' + KIND_ALT + r')\s+'
    r'(?:(?P<id>\d+)\s+)?'
    r'(?P<qname>"(?P<name_q>[^"]+)"|(?P<name_u>[A-Za-z_][A-Za-z0-9_]*))'
    r'(?P<rest>.*)$',
    re.IGNORECASE,
)

# ID-based references inside AL bodies that we should rewrite.
RE_REF_CODEUNIT_RUN = re.compile(r'\bCodeunit\.Run\s*\(\s*(\d+)\s*([,\)])', re.IGNORECASE)
RE_REF_PAGE_RUN     = re.compile(r'\bPage\.(Run|RunModal)\s*\(\s*(\d+)\s*([,\)])', re.IGNORECASE)
RE_REF_REPORT_RUN   = re.compile(r'\bReport\.(Run|RunModal|Execute)\s*\(\s*(\d+)\s*([,\)])', re.IGNORECASE)
RE_REF_DATABASE     = re.compile(r'\bDatabase::(\d+)\b')


def walk_al(root: Path):
    for p in sorted(root.rglob("*.al")):
        # Skip excluded/quarantined dirs.
        parts = set(p.parts)
        if "excluded" in parts or "node_modules" in parts:
            continue
        yield p


def parse_file(path: Path):
    """Yield decl dicts for each object decl line in a file."""
    objs = []
    try:
        text = path.read_text(encoding="utf-8")
    except Exception:
        return objs
    for lineno, line in enumerate(text.splitlines(), start=1):
        m = RE_DECL.match(line)
        if not m:
            continue
        kind = m.group("kind").lower()
        if kind not in KINDS:
            continue
        idstr = m.group("id")
        # interface has no ID. Other kinds without an ID are syntax errors;
        # skip them.
        if kind != "interface" and idstr is None:
            continue
        name = m.group("name_q") or m.group("name_u")
        objs.append({
            "kind": kind,
            "id": int(idstr) if idstr else None,
            "name": name,
            "file": str(path),
            "line": lineno,
            "raw": line,
        })
    return objs


def build_inventory(root: Path):
    inv = []
    for f in walk_al(root):
        inv.extend(parse_file(f))
    return inv


def detect_collisions(inv):
    by_kind_id = defaultdict(list)
    by_kind_name = defaultdict(list)
    for o in inv:
        if o["id"] is not None:
            by_kind_id[(o["kind"], o["id"])].append(o)
        by_kind_name[(o["kind"], o["name"].lower())].append(o)
    id_coll = {k: v for k, v in by_kind_id.items() if len(v) > 1}
    name_coll = {k: v for k, v in by_kind_name.items() if len(v) > 1}
    return id_coll, name_coll


def print_collision_report(inv):
    id_coll, name_coll = detect_collisions(inv)
    print(f"=== AL inventory: {len(inv)} objects ===")
    by_kind = defaultdict(int)
    for o in inv:
        by_kind[o["kind"]] += 1
    for k in KINDS:
        if by_kind[k]:
            print(f"  {k}: {by_kind[k]}")
    print()
    print(f"=== ID collisions ({len(id_coll)}) ===")
    for (kind, oid), os_ in sorted(id_coll.items()):
        print(f"  {kind} {oid} — {len(os_)} occurrences:")
        for o in os_:
            print(f"    {o['file']}:{o['line']}  '{o['name']}'")
    print()
    print(f"=== Name collisions ({len(name_coll)}) ===")
    for (kind, name), os_ in sorted(name_coll.items()):
        print(f"  {kind} '{name}' — {len(os_)} occurrences:")
        for o in os_:
            print(f"    {o['file']}:{o['line']}  id={o['id']}")
    return id_coll, name_coll


def renumber(root: Path, inv, start: int):
    """Rewrite IDs in-place. Assigns sequential start..start+N-1 per kind,
    sorted by (file, line) for stability. Rewrites the decl line and any
    ID-based intra-AL reference (Codeunit.Run/Page.Run/Report.Run/Database::N)
    that matches a renumbered ID for that kind."""
    # Plan: kind → (old_id → new_id)
    plan = defaultdict(dict)
    name_plan = defaultdict(dict)  # (kind, lower-name) → new-name
    # Collisions for names: keep first occurrence's name; subsequent get _<old_id>
    seen_names = defaultdict(set)
    # Sort objects within each kind for stable assignment.
    by_kind_sorted = defaultdict(list)
    for o in inv:
        by_kind_sorted[o["kind"]].append(o)
    for kind, lst in by_kind_sorted.items():
        lst.sort(key=lambda o: (o["file"], o["line"]))
        next_id = start
        for o in lst:
            if kind == "interface":
                # No ID renumber. Still handle name collisions.
                key = (kind, o["name"].lower())
                if o["name"].lower() in seen_names[kind]:
                    name_plan[(o["file"], o["line"])] = o["name"] + f"_{o.get('id') or 'x'}"
                else:
                    seen_names[kind].add(o["name"].lower())
                continue
            old = o["id"]
            new = next_id
            next_id += 1
            plan[kind][old] = new
            # Track name collisions
            lname = o["name"].lower()
            if lname in seen_names[kind]:
                name_plan[(o["file"], o["line"])] = o["name"] + f"_{old}"
            else:
                seen_names[kind].add(lname)

    # Build a per-file edit list: decl-line edits + reference rewrites in body.
    files = defaultdict(list)
    for o in inv:
        files[o["file"]].append(o)

    rewrites = 0
    for fpath, objs in files.items():
        path = Path(fpath)
        text = path.read_text(encoding="utf-8")
        lines = text.splitlines(keepends=False)
        decl_lines = {o["line"]: o for o in objs}

        # 1) Rewrite decl lines
        for lineno, o in decl_lines.items():
            line = lines[lineno - 1]
            m = RE_DECL.match(line)
            if not m:
                continue
            kind = o["kind"]
            new_id = None
            if kind != "interface":
                new_id = plan[kind].get(o["id"], o["id"])
            new_name = name_plan.get((fpath, lineno), o["name"])
            # Rebuild line: <indent><kind> <id?> "<name>"<rest>
            indent = m.group("indent")
            rest = m.group("rest")
            qname = f'"{new_name}"' if (m.group("qname") or "").startswith('"') else new_name
            if kind == "interface":
                lines[lineno - 1] = f"{indent}{m.group('kind')} {qname}{rest}"
            else:
                lines[lineno - 1] = f"{indent}{m.group('kind')} {new_id} {qname}{rest}"

        # 2) Rewrite ID refs in body. Apply per-kind plan.
        new_text = "\n".join(lines) + ("\n" if text.endswith("\n") else "")

        def sub_codeunit(mm):
            old = int(mm.group(1)); sep = mm.group(2)
            new = plan["codeunit"].get(old, old)
            return f"Codeunit.Run({new}{sep}"

        def sub_page(mm):
            verb = mm.group(1); old = int(mm.group(2)); sep = mm.group(3)
            new = plan["page"].get(old, old)
            return f"Page.{verb}({new}{sep}"

        def sub_report(mm):
            verb = mm.group(1); old = int(mm.group(2)); sep = mm.group(3)
            new = plan["report"].get(old, old)
            return f"Report.{verb}({new}{sep}"

        def sub_db(mm):
            old = int(mm.group(1))
            new = plan["table"].get(old, old)
            return f"Database::{new}"

        new_text2 = RE_REF_CODEUNIT_RUN.sub(sub_codeunit, new_text)
        new_text2 = RE_REF_PAGE_RUN.sub(sub_page, new_text2)
        new_text2 = RE_REF_REPORT_RUN.sub(sub_report, new_text2)
        new_text2 = RE_REF_DATABASE.sub(sub_db, new_text2)

        if new_text2 != text:
            path.write_text(new_text2, encoding="utf-8")
            rewrites += 1
    print(f"renumbered: {rewrites} files modified")
    # Print plan summary
    for kind in KINDS:
        if plan[kind]:
            mn = min(plan[kind].values()); mx = max(plan[kind].values())
            print(f"  {kind}: {len(plan[kind])} objects → IDs {mn}..{mx}")
    if name_plan:
        print(f"  name renames: {len(name_plan)}")
        for (f, l), nn in sorted(name_plan.items()):
            print(f"    {f}:{l}  → '{nn}'")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root")
    ap.add_argument("--renumber", action="store_true")
    ap.add_argument("--start", type=int, default=50000)
    ap.add_argument("--json", default=None)
    args = ap.parse_args()
    root = Path(args.root).resolve()
    inv = build_inventory(root)
    if args.json:
        Path(args.json).write_text(json.dumps(inv, indent=2))
        print(f"wrote {args.json}")
    print_collision_report(inv)
    if args.renumber:
        renumber(root, inv, args.start)


if __name__ == "__main__":
    main()
