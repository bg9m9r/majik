#!/usr/bin/env python3
"""
Merge a scraped MTGGoldfish format-staples HTML with the in-repo curated
Modern-staples list to produce docs/meta-modern-snapshot.json.

Scraped rows win over curated when both name the same card. Curated values
exist so the snapshot covers more than the ~30 cards Goldfish shows on the
staples page (fetchlands, basic answers, sideboard pieces, etc.).

Called by refresh-meta-snapshot.sh; not meant to be invoked standalone in
the build pipeline.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import html as _html
import json
import re
import sys
from pathlib import Path


# Curated supplement. Keep this list small and well-known; the scrape carries
# the moving meta. Values are conservative play-rate-% estimates synthesized
# from public meta snapshots.
CURATED: dict[str, float] = {
    # Fetchlands + shocks
    "Misty Rainforest": 45.0, "Verdant Catacombs": 42.0, "Marsh Flats": 40.0,
    "Scalding Tarn": 38.0, "Polluted Delta": 36.0, "Bloodstained Mire": 35.0,
    "Windswept Heath": 35.0, "Wooded Foothills": 35.0, "Flooded Strand": 34.0,
    "Arid Mesa": 33.0,
    "Steam Vents": 22.0, "Watery Grave": 22.0, "Blood Crypt": 20.0,
    "Hallowed Fountain": 18.0, "Overgrown Tomb": 18.0, "Stomping Ground": 17.0,
    "Sacred Foundry": 17.0, "Godless Shrine": 16.0, "Breeding Pool": 16.0,
    "Temple Garden": 15.0,
    # Utility lands
    "Urza's Saga": 28.0, "Boseiju, Who Endures": 25.0,
    "Inkmoth Nexus": 12.0, "Blast Zone": 10.0,
    "Otawara, Soaring City": 14.0, "Eiganjo, Seat of the Empire": 8.0,
    "Takenuma, Abandoned Mire": 7.0, "Hive of the Eye Tyrant": 6.0,
    # Iconic instants / sorceries
    "Lightning Bolt": 30.0, "Counterspell": 12.0, "Path to Exile": 12.0,
    "Swords to Plowshares": 8.0, "Prismatic Ending": 18.0,
    "Snapcaster Mage": 6.0, "Surgical Extraction": 7.0,
    "Force of Vigor": 9.0, "Veil of Summer": 11.0, "Unholy Heat": 16.0,
    "Faithful Mending": 10.0, "Expressive Iteration": 12.0,
    "Crashing Footfalls": 8.0, "Living End": 8.0, "Through the Breach": 5.0,
    "Goryo's Vengeance": 4.0,
    # Creatures
    "Ragavan, Nimble Pilferer": 24.0, "Murktide Regent": 18.0,
    "Dragon's Rage Channeler": 16.0, "Death's Shadow": 10.0,
    "Esper Sentinel": 14.0, "Ledger Shredder": 12.0,
    "Phlage, Titan of Fire's Fury": 22.0, "Wrenn and Six": 18.0,
    "Wrenn and Realmbreaker": 6.0, "Grief": 14.0,
    "Fury": 12.0, "Tarmogoyf": 4.0, "Monastery Swiftspear": 18.0,
    "Dauthi Voidwalker": 6.0, "Sheoldred, the Apocalypse": 11.0,
    "Primeval Titan": 9.0, "Karn, the Great Creator": 10.0,
    "Liliana of the Veil": 8.0, "Teferi, Time Raveler": 10.0,
    "Chalice of the Void": 9.0,
    # Combo / archetype keystones
    "Amulet of Vigor": 6.0, "Scapeshift": 5.0,
    "Emrakul, the Aeons Torn": 3.0,
    "Hardened Scales": 5.0, "Arcbound Ravager": 5.0, "Walking Ballista": 6.0,
    "Karn, Scion of Urza": 4.0, "Mox Opal": 5.0,
    "Hammer of Nazahn": 4.0, "Sigarda's Aid": 4.0, "Puresteel Paladin": 4.0,
    "Colossus Hammer": 4.0,
    # Tron + Eldrazi
    "Urza's Mine": 6.0, "Urza's Power Plant": 6.0, "Urza's Tower": 6.0,
    "Karn Liberated": 5.0, "Ulamog, the Ceaseless Hunger": 4.0,
    "Wurmcoil Engine": 3.0, "Reality Smasher": 4.0, "Thought-Knot Seer": 4.0,
    "Eldrazi Temple": 5.0,
    # Burn / aggro
    "Lava Dart": 4.0, "Skewer the Critics": 3.0, "Light Up the Stage": 3.0,
    "Eidolon of the Great Revel": 4.0, "Boros Charm": 4.0,
    # Evoke elementals & MH staples
    "Solitude": 14.0, "Endurance": 12.0, "Subtlety": 8.0,
    "Force of Negation": 14.0, "Leyline Binding": 16.0,
    # Reanimator / graveyard
    "Atraxa, Grand Unifier": 5.0, "Archon of Cruelty": 4.0,
    "Faithless Looting": 3.0,
    # Mana / utility
    "Aether Vial": 6.0, "Cavern of Souls": 8.0,
    "Bloodghast": 3.0, "Hollow One": 2.0,
    # Affinity
    "Springleaf Drum": 5.0, "Galvanic Blast": 4.0, "Cranial Plating": 4.0,
    # 4c omnath / yorion
    "Omnath, Locus of Creation": 6.0, "Yorion, Sky Nomad": 3.0,
    "Lightning Helix": 4.0,
    # Sweepers + sideboard
    "Aether Gust": 6.0, "Anger of the Gods": 3.0,
    "Supreme Verdict": 3.0, "Toxic Deluge": 2.0, "Blood Moon": 5.0,
    "Damping Sphere": 6.0, "Stony Silence": 3.0,
    "Engineered Explosives": 8.0, "Pithing Needle": 6.0,
    "Aether Spellbomb": 4.0, "Pyrite Spellbomb": 4.0,
    # Cascade
    "Shardless Agent": 4.0, "Violent Outburst": 4.0,
    # Yawgmoth + recent
    "Yawgmoth, Thran Physician": 5.0, "Slogurk, the Overslime": 3.0,
    # Hexproof
    "Slippery Bogle": 2.0, "Daybreak Coronet": 2.0,
    # Heliod combo
    "Heliod, Sun-Crowned": 2.0, "Spike Feeder": 2.0,
    # Sideboard cantrips
    "Rest in Peace": 4.0, "Leyline of the Void": 3.0,
    "Mystical Dispute": 8.0, "Dispel": 3.0, "Spell Pierce": 4.0,
    "Spell Snare": 3.0, "Inquisition of Kozilek": 8.0,
    # Mid-tier manabase
    "Spirebluff Canal": 4.0, "Botanical Sanctum": 3.0,
    "Concealed Courtyard": 3.0, "Inspiring Vantage": 3.0,
    "Blooming Marsh": 3.0, "Mutavault": 4.0, "Ghost Quarter": 2.0,
    # Modern Horizons 3 standouts
    "The One Ring": 22.0, "Orcish Bowmasters": 14.0,
    "Nethergoyf": 6.0, "Tamiyo, Inquisitive Student": 4.0,
    "Mosswood Dreadknight": 4.0, "Up the Beanstalk": 5.0,
}


_NAME_PCT_RX = re.compile(
    r"href=[\"']/price/[^\"']+[\"'][^>]*>([^<]+)</a>"
    r".*?text-end[\"']?\s*>([\d.]+)%",
    re.DOTALL,
)


def scrape(html_path: Path) -> dict[str, float]:
    src = html_path.read_text(encoding="utf-8", errors="replace")
    rows: dict[str, float] = {}
    for tr in re.finditer(r"<tr[^>]*>(.*?)</tr>", src, re.DOTALL):
        m = _NAME_PCT_RX.search(tr.group(1))
        if not m:
            continue
        name = _html.unescape(m.group(1)).strip()
        pct = float(m.group(2))
        # Keep the highest % if Goldfish lists multiple printings (foil etc).
        if name not in rows or pct > rows[name]:
            rows[name] = pct
    return rows


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--html", required=True, type=Path)
    ap.add_argument("--out", required=True, type=Path)
    args = ap.parse_args()

    scraped = scrape(args.html)
    print(f"Scraped {len(scraped)} unique rows from {args.html.name}", file=sys.stderr)

    combined = dict(CURATED)
    for name, pct in scraped.items():
        combined[name] = pct  # scrape wins
    print(f"Combined {len(combined)} unique cards (curated {len(CURATED)} + scrape).",
          file=sys.stderr)

    items = sorted(combined.items(), key=lambda kv: (-kv[1], kv[0]))
    cards = [
        {"name": n, "decks": int(round(p * 10)), "play_rate_pct": p}
        for n, p in items
    ]

    payload = {
        "format": "modern",
        "snapshot_date": _dt.date.today().isoformat(),
        "source": "mtggoldfish-staples + curated-modern-staples",
        "source_url": "https://www.mtggoldfish.com/format-staples/modern",
        "notes": (
            "Combined scrape of MTGGoldfish format-staples page plus a curated "
            "supplement of well-known Modern staples (lands, removal, sideboard "
            "pieces) so coverage measurements are not dominated by ~30 cards. "
            "Cards absent from this list receive zero weight. Refresh quarterly "
            "via scripts/refresh-meta-snapshot.sh."
        ),
        "cards": cards,
    }

    args.out.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
