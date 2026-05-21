#!/bin/sh
# Boot-time bootstrap for the cards SQLite seed.
#
# Pulls /root/.config/Majik/cards.db from a GitHub Release on
# bg9m9r/majik, but only when the pinned tag differs from what's
# already on disk. The pinned tag lives in CARDS_SEED_TAG (env). To
# refresh the seed in prod: cut a new release on GitHub, bump
# CARDS_SEED_TAG on majik-api, save → Render redeploys → this script
# downloads on next boot.
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
    else
        url="https://github.com/${SEED_REPO}/releases/download/${SEED_TAG}/cards.db"
        echo "[cards-seed] downloading $url" >&2
        tmp="$(mktemp -p "$SEED_DIR" cards.db.XXXXXX)"
        if curl -fL --retry 3 --retry-delay 5 "$url" -o "$tmp"; then
            mv "$tmp" "$SEED_FILE"
            printf '%s' "$SEED_TAG" > "$TAG_FILE"
            echo "[cards-seed] installed tag=$SEED_TAG ($(stat -c%s "$SEED_FILE" 2>/dev/null || echo '?') bytes)" >&2
        else
            rm -f "$tmp"
            echo "[cards-seed] download failed — leaving existing seed in place." >&2
            # Don't kill boot if download fails and we already have *some* seed.
            # Card endpoints will just serve stale data until the next deploy.
            [ -s "$SEED_FILE" ] || { echo "[cards-seed] no usable seed; aborting." >&2; exit 1; }
        fi
    fi
fi

exec dotnet Majik.Server.dll "$@"
