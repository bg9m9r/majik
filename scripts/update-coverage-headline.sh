#!/usr/bin/env bash
# Updates the "Headline numbers" table in MODERN_COVERAGE.md by counting
# factories / templates / JSON cards on disk.
#
# Usage: ./scripts/update-coverage-headline.sh         (writes file in place)
#        ./scripts/update-coverage-headline.sh --check (prints counts, exits non-zero on drift)
set -euo pipefail

cd "$(dirname "$0")/.."

DOC="MODERN_COVERAGE.md"
FACTORY_DIR="Majik.Core/CardData/Factories"
TEMPLATE_DIR="Majik.Core/CardData/SpellTemplates/Templates"
BESPOKE_DIR="${TEMPLATE_DIR}/Bespoke"
JSON_DIR="Majik.Core/CardData"

factories=$(find "${FACTORY_DIR}" -maxdepth 1 -name "*Factory.cs" -type f | wc -l)
bespoke=$(find "${BESPOKE_DIR}" -maxdepth 1 -name "*.cs" -type f | wc -l)
generic=$(find "${TEMPLATE_DIR}" -name "*.cs" -type f -not -path "${BESPOKE_DIR}/*" | wc -l)
# JSON-defined cards: top-level *.json under CardData, excluding test fixtures.
json=$(find "${JSON_DIR}" -maxdepth 2 -name "*.json" -type f -not -name "test-*.json" | wc -l)

if [[ "${1:-}" == "--check" ]]; then
  printf "factories=%d bespoke=%d generic=%d json=%d\n" \
    "$factories" "$bespoke" "$generic" "$json"
  # Drift detection: pull the four numbers currently in the doc and compare.
  doc_factories=$(awk -F'|' '/^\| Named factories / {gsub(/ /,"",$3); print $3}' "$DOC")
  doc_bespoke=$(awk -F'|' '/^\| Bespoke templates / {gsub(/ /,"",$3); print $3}' "$DOC")
  doc_generic=$(awk -F'|' '/^\| Generic templates / {gsub(/ /,"",$3); print $3}' "$DOC")
  doc_json=$(awk -F'|' '/^\| JSON-defined cards / {gsub(/ /,"",$3); print $3}' "$DOC")
  drift=0
  [[ "$factories" != "$doc_factories" ]] && { echo "DRIFT: factories disk=$factories doc=$doc_factories"; drift=1; }
  [[ "$bespoke" != "$doc_bespoke" ]] && { echo "DRIFT: bespoke disk=$bespoke doc=$doc_bespoke"; drift=1; }
  [[ "$generic" != "$doc_generic" ]] && { echo "DRIFT: generic disk=$generic doc=$doc_generic"; drift=1; }
  [[ "$json" != "$doc_json" ]] && { echo "DRIFT: json disk=$json doc=$doc_json"; drift=1; }
  exit "$drift"
fi

tmp=$(mktemp)
awk -v f="$factories" -v b="$bespoke" -v g="$generic" -v j="$json" '
  /^\| Named factories / { printf "| Named factories | %d |\n", f; next }
  /^\| Bespoke templates / { printf "| Bespoke templates | %d |\n", b; next }
  /^\| Generic templates / { printf "| Generic templates | %d |\n", g; next }
  /^\| JSON-defined cards / { printf "| JSON-defined cards | %d |\n", j; next }
  { print }
' "$DOC" > "$tmp"
mv "$tmp" "$DOC"

printf "Updated %s: factories=%d bespoke=%d generic=%d json=%d\n" \
  "$DOC" "$factories" "$bespoke" "$generic" "$json"
