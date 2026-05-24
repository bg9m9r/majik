#!/usr/bin/env bash
# Refresh docs/meta-modern-snapshot.json from MTGGoldfish staples.
#
# Scrapes https://www.mtggoldfish.com/format-staples/modern (HTML), extracts
# the (card name, play-rate%) pairs, and merges them with the in-repo curated
# Modern-staples list (lands, removal, sideboard pieces). Scraped values win
# when both lists name the same card.
#
# Run quarterly. Commit the resulting snapshot. Then re-run:
#   dotnet run --project Majik.Console -- coverage --modern --weighted \
#     --md-out docs/COVERAGE_MODERN.md --json-out docs/coverage-modern.json
# to refresh the auto-generated headline numbers.
#
# Dependencies: curl, python3.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SNAPSHOT="${REPO_ROOT}/docs/meta-modern-snapshot.json"
GOLDFISH_URL="https://www.mtggoldfish.com/format-staples/modern"
TMP_HTML="$(mktemp -t goldfish.XXXXXX.html)"
trap 'rm -f "${TMP_HTML}"' EXIT

echo "Fetching ${GOLDFISH_URL} ..."
curl -fsSL --max-time 30 \
    -A "Mozilla/5.0 (X11; Linux x86_64) refresh-meta-snapshot.sh" \
    "${GOLDFISH_URL}" -o "${TMP_HTML}"

echo "Parsing scraped rows + merging with curated supplement ..."
python3 "${REPO_ROOT}/scripts/_merge_meta_snapshot.py" \
    --html "${TMP_HTML}" \
    --out  "${SNAPSHOT}"

echo "Wrote ${SNAPSHOT}"
echo "Now run:"
echo "  dotnet run --project Majik.Console -- coverage --modern --weighted \\"
echo "    --md-out docs/COVERAGE_MODERN.md --json-out docs/coverage-modern.json"
