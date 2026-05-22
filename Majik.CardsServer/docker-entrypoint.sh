#!/bin/sh
# Boot-time bootstrap for the cards SQLite seed.
#
# Same logic as Majik.Server/docker-entrypoint.sh — kept as a copy here
# (rather than shared) so the cards service can move on its own deploy
# cadence. The only differences:
#   - this script execs Majik.CardsServer.dll, not Majik.Server.dll
#   - when CARDS_SEED_TAG is unset, the bootstrap is skipped (lets local
#     dev mount a host-side cards.db instead of pulling from GitHub)
#
# Idempotent. Safe if disk is fresh, half-populated, or already on
# the desired tag.

set -eu

SEED_REPO="${CARDS_SEED_REPO:-bg9m9r/majik}"
SEED_TAG="${CARDS_SEED_TAG:-}"
SEED_DIR="${CARDS_SEED_DIR:-/root/.config/Majik}"
SEED_FILE="$SEED_DIR/cards.db"
TAG_FILE="$SEED_DIR/.seed-tag"

if [ -z "$SEED_TAG" ]; then
    echo "[cards-seed] CARDS_SEED_TAG unset — skipping bootstrap." >&2
else
    mkdir -p "$SEED_DIR"
    current=""
    [ -f "$TAG_FILE" ] && current="$(cat "$TAG_FILE")"

    if [ "$current" = "$SEED_TAG" ] && [ -s "$SEED_FILE" ]; then
        echo "[cards-seed] tag=$SEED_TAG already on disk — skipping." >&2
        for f in "$SEED_DIR"/cards*; do
            [ -e "$f" ] || continue
            case "$f" in
                "$SEED_FILE"|"$TAG_FILE") continue ;;
            esac
            size="$(stat -c%s "$f" 2>/dev/null || echo '?')"
            echo "[cards-seed] sweep: removing orphan $f ($size bytes)" >&2
            rm -f "$f"
        done
    else
        url="https://github.com/${SEED_REPO}/releases/download/${SEED_TAG}/cards.db"
        echo "[cards-seed] downloading $url" >&2

        for f in "$SEED_DIR"/cards*; do
            [ -e "$f" ] || continue
            size="$(stat -c%s "$f" 2>/dev/null || echo '?')"
            echo "[cards-seed] sweep: removing $f ($size bytes)" >&2
            rm -f "$f"
        done
        rm -f "$TAG_FILE"

        tmp="$(mktemp -p "$SEED_DIR" cards.db.XXXXXX)"
        if curl -fL --retry 3 --retry-delay 5 "$url" -o "$tmp"; then
            mv "$tmp" "$SEED_FILE"
            printf '%s' "$SEED_TAG" > "$TAG_FILE"
            echo "[cards-seed] installed tag=$SEED_TAG ($(stat -c%s "$SEED_FILE" 2>/dev/null || echo '?') bytes)" >&2
        else
            rm -f "$tmp"
            echo "[cards-seed] download failed AND old seed was already removed — aborting boot." >&2
            exit 1
        fi
    fi
fi

exec dotnet Majik.CardsServer.dll "$@"
