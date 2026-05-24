#!/usr/bin/env python3
"""
Retrofit [CardName("...")] attributes onto each named-card factory class.

Source of truth: the historical (pre-source-gen) NamedCardFactory.cs
switch arms, captured as a mapping file produced by:

    grep -nP '"[^"]+"\\s*=>\\s*\\w+Factory\\.Create\\(owner\\)' \\
        Majik.Core/CardData/NamedCardFactory.cs \\
        | sed -E 's/^[0-9]+:\\s*"([^"]+)"\\s*=>\\s*(\\w+Factory)\\.Create\\(owner\\).*/\\1|\\2/' \\
        > /tmp/card-mappings.txt

The script is idempotent: if the target factory file already declares
the attribute we want, we leave it alone. Multiple [CardName] attributes
per class are supported (AllowMultiple = true) — needed for any future
factory that handles a functional reprint pair.

Usage:
    python3 scripts/retrofit-card-name-attributes.py \\
        --mapping /tmp/card-mappings.txt \\
        --factories Majik.Core/CardData/Factories
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys


CLASS_DECL_RE = re.compile(
    r"^(?P<indent>\s*)public\s+static\s+(partial\s+)?class\s+(?P<name>\w+Factory)\b",
    re.MULTILINE,
)


def insert_attribute(source: str, factory_name: str, card_name: str) -> str:
    escaped = card_name.replace("\\", "\\\\").replace('"', '\\"')
    attr_line = f'[CardName("{escaped}")]'

    # Idempotency check.
    if attr_line in source:
        return source

    match = None
    for m in CLASS_DECL_RE.finditer(source):
        if m.group("name") == factory_name:
            match = m
            break
    if match is None:
        raise RuntimeError(f"Could not find class declaration for {factory_name}")

    indent = match.group("indent")
    insertion = f"{indent}{attr_line}\n"

    # Walk backwards from the class line over preceding attribute lines
    # at the same indent so existing [CardName] / other attributes stay
    # grouped above the class.
    lines = source.split("\n")
    line_start = source.count("\n", 0, match.start())
    insert_at = line_start
    while insert_at > 0:
        prev = lines[insert_at - 1].rstrip()
        if prev.startswith(indent + "[") and prev.endswith("]"):
            insert_at -= 1
            continue
        break

    lines.insert(insert_at, indent + attr_line)
    return "\n".join(lines)


def ensure_using(source: str) -> str:
    if "using Majik.Core.CardData.Factories;" in source:
        return source
    # Factory files already live in this namespace, so the attribute is
    # in-scope without any using directive. Nothing to do.
    return source


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mapping", required=True, type=pathlib.Path)
    parser.add_argument("--factories", required=True, type=pathlib.Path)
    args = parser.parse_args(argv)

    if not args.mapping.exists():
        print(f"mapping file not found: {args.mapping}", file=sys.stderr)
        return 2
    if not args.factories.is_dir():
        print(f"factories dir not found: {args.factories}", file=sys.stderr)
        return 2

    edited = 0
    skipped = 0
    seen: set[tuple[str, str]] = set()
    for raw in args.mapping.read_text().splitlines():
        raw = raw.strip()
        if not raw or raw.startswith("#"):
            continue
        try:
            name, factory = raw.split("|", 1)
        except ValueError:
            print(f"bad mapping line: {raw}", file=sys.stderr)
            return 2
        if (factory, name) in seen:
            continue
        seen.add((factory, name))

        path = args.factories / f"{factory}.cs"
        if not path.exists():
            print(f"MISSING factory file: {path}", file=sys.stderr)
            return 2

        src = path.read_text()
        out = ensure_using(insert_attribute(src, factory, name))
        if out != src:
            path.write_text(out)
            edited += 1
        else:
            skipped += 1

    print(f"edited={edited} skipped(idempotent)={skipped} total={len(seen)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
