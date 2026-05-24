using System.Text.RegularExpressions;

namespace Majik.Core.CardData.MechanicDeps;

/// <summary>
/// Canonical primitive definition — one row in the registry. The
/// <see cref="MatchPatterns"/> regexes are evaluated against each
/// extracted deferral sentence (case-insensitive); any match assigns
/// the mention to this primitive. The first match wins, so the
/// registry order matters (most-specific patterns first).
/// </summary>
public sealed record MechanicPrimitive(
    string Id,
    string DisplayName,
    string? CompRulesCitation,
    string? ImplementationHint,
    IReadOnlyList<Regex> MatchPatterns);

/// <summary>
/// Hard-coded registry of canonical primitives we care about. Sourced
/// from the spec table + a sweep over existing deferral comments in
/// <c>Majik.Core/CardData/Factories/</c>. New primitives matching no
/// pattern fall into the synthetic <c>Other</c> bucket for human review.
/// </summary>
public static class MechanicPrimitiveRegistry
{
    private static Regex Rx(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Synthetic bucket ID for unmatched mentions.</summary>
    public const string OtherId = "other";

    public static readonly IReadOnlyList<MechanicPrimitive> All = new List<MechanicPrimitive>
    {
        // --- Destroy-path riders ---
        new("regeneration", "Regeneration shield (CR 701.15)",
            "CR 701.15",
            "ReplacementBus filter on ZoneMoveIntent battlefield→graveyard, gated by sourceCard.HasAbility(\"Regenerate\").",
            new[]
            {
                Rx(@"regenerat\w*\b.{0,80}\bdefer"),
                Rx(@"can'?t be regenerat\w*\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bregenerat"),
            }),
        new("indestructible-bypass", "Indestructible bypass on destroy (CR 702.12)",
            "CR 702.12",
            "Destroy intent should consult a HasIndestructible() predicate before issuing ZoneMoveIntent → graveyard.",
            new[]
            {
                Rx(@"indestructible\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bindestructible"),
                Rx(@"indestructible bypass\b"),
                Rx(@"indestructible\b.{0,160}\bsame gap"),
            }),

        // --- Cast-from-elsewhere alt-cost mechanics ---
        new("adventure", "Adventure cast pipeline (CR 715)",
            "CR 715",
            "Adventure half is a non-creature spell cast from hand that exiles + permits a single later creature cast. Largely closed in PR #402; remaining mentions probably stale.",
            new[]
            {
                Rx(@"adventure\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\badventure"),
            }),
        new("escape", "Escape alt-cost (CR 702.143)",
            "CR 702.143",
            "Cast-from-graveyard alt cost that additionally exiles N cards. Sibling of Flashback's cast-from-graveyard, but with the extra exile-cost rider.",
            new[]
            {
                Rx(@"\bescape\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bescape\b"),
            }),
        new("plot", "Plot (CR 718)",
            "CR 718",
            "Activated-from-hand alt cost that exiles with a plotted marker; subsequent turn permits sorcery-speed cast for {0}. Sibling of Suspend's cast-from-exile rider.",
            new[]
            {
                Rx(@"\bplot\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bplot\b"),
            }),
        new("foretell", "Foretell (CR 702.143)",
            "CR 702.143",
            "Activated-from-hand alt cost ({2}) to exile face-down; subsequent turns permit cast for foretell cost.",
            new[]
            {
                Rx(@"foretell\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bforetell"),
            }),
        new("splice-arcane", "Splice onto Arcane (CR 702.46)",
            "CR 702.46",
            "Cast-time additive cost that copies an extra Arcane spell's effect onto the original. Requires a spell-modify hook during cast pipeline.",
            new[]
            {
                Rx(@"splice\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bsplice"),
            }),
        new("replicate", "Replicate (CR 702.99)",
            "CR 702.99",
            "Optional additive cast cost; for each replicate paid, create one extra copy of the spell on resolution.",
            new[]
            {
                Rx(@"replicate\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\breplicate"),
            }),
        new("cycling-from-hand", "Cycling-style activated-from-hand (CR 702.32 / Channel CR 702.74)",
            "CR 702.32",
            "Generic 'pay X, discard ~: <effect>' from hand. Shape covers Cycling, Channel, Forecast.",
            new[]
            {
                Rx(@"channel\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bchannel"),
            }),

        // --- Cast/activation restrictions ---
        new("sorcery-speed-gate", "\"Activate only as a sorcery\" gate (CR 117.1a)",
            "CR 117.1a",
            "ActionValidator-side check: ability/spell flagged sorcery-speed only legal during own main phase with empty stack.",
            new[]
            {
                Rx(@"sorcery[- ]speed\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bsorcery[- ]speed"),
                Rx(@"117\.1a\b.{0,80}\bdefer"),
            }),

        // --- Library / zone primitives ---
        new("library-shuffle", "Library shuffle (CR 701.20)",
            "CR 701.20",
            "Add IZone.Shuffle / ZoneService.ShuffleLibrary. Tutor-family factories all block on this single primitive.",
            new[]
            {
                Rx(@"shuffle\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bshuffle"),
            }),

        // --- Tokens / colour identity ---
        new("token-colour-identity", "Token colour identity (CR 105 / CR 903.4)",
            "CR 105",
            "TokenFactory needs an explicit Colors field separate from mana cost; today tokens default to colourless.",
            new[]
            {
                Rx(@"token col(?:ou|o)r\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\btoken col(?:ou|o)r"),
                Rx(@"col(?:ou|o)r identity\b.{0,80}\bdefer"),
                Rx(@"token col(?:ou|o)r\b.{0,160}\bsame gap"),
                Rx(@"token[- ]creature col(?:ou|o)r"),
                Rx(@"col(?:ou|o)r identity\b.{0,160}\bsame gap"),
                Rx(@"tokens are created as col(?:ou|o)rless"),
            }),

        // --- Newer mechanics ---
        new("ascend", "Ascend / city's blessing (CR 702.131)",
            "CR 702.131",
            "Per-player flag + state-based check (≥10 permanents). Static abilities then key off blessing-active predicate.",
            new[]
            {
                Rx(@"\bascend\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bascend\b"),
                Rx(@"city'?s blessing\b.{0,80}\bdefer"),
            }),
        new("manifest-dread", "Manifest dread (CR 701.59)",
            "CR 701.59",
            "Manifest the top card face-down + scry-style discard rider. Sibling of vanilla Manifest with an added zone-choice prompt.",
            new[]
            {
                Rx(@"manifest dread\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bmanifest dread"),
            }),
        new("disguise-cloak", "Disguise / Cloak (CR 702.166 / 702.167)",
            "CR 702.166",
            "Face-down creature with a turn-face-up alt cost; shares plumbing with Morph but adds Ward 2.",
            new[]
            {
                Rx(@"disguise\b.{0,80}\bdefer"),
                Rx(@"\bcloak\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\b(disguise|cloak)\b"),
            }),
        new("class-leveling", "Class leveling (CR 716)",
            "CR 716",
            "Class enchantment level-up cost + per-level static/triggered ability accretion.",
            new[]
            {
                Rx(@"class leveling\b.{0,80}\bdefer"),
                Rx(@"leveling\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bleveling"),
            }),
        new("gift", "Gift (Bloomburrow)",
            null,
            "Cast-time choice: a static/triggered side effect granting an opponent a defined gift (treasure, draw, etc.).",
            new[]
            {
                Rx(@"\bgift\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bgift\b"),
            }),
        new("ring-tempts-you", "The Ring tempts you (LTR)",
            null,
            "Per-player Ring counter + ringbearer selection + four staged static effects keyed off counter value.",
            new[]
            {
                Rx(@"ring[- ]tempts\b.{0,80}\bdefer"),
                Rx(@"the ring\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bring[- ]tempts"),
            }),

        // --- Bot / agent prompt deferrals ---
        new("bot-delve", "Bot-side Delve discovery",
            null,
            "Bot evaluator's cost-discovery pass must learn to exile cards from graveyard to satisfy Delve generic mana. Largely closed in PR #401; remaining mentions likely stale.",
            new[]
            {
                Rx(@"bot[- ]side delve\b.{0,80}\bdefer"),
                Rx(@"bot[- ]side\b.{0,80}\bdelve\b.{0,80}\bdefer"),
                Rx(@"\bdelve\b.{0,80}\bbot[- ]side\b.{0,80}\bdefer"),
            }),
        new("agent-prompt-targeting", "Agent-prompt targeting MVP",
            null,
            "IPlayerAgent needs ChooseTarget / ChooseYesNo surfaces; many spell factories punt on real targeting prompts.",
            new[]
            {
                Rx(@"agent[- ]prompt\b.{0,80}\bdefer"),
                Rx(@"agent[- ]driven\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bagent[- ](prompt|driven)"),
                Rx(@"full targeting\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bfull targeting"),
            }),

        // --- Alt-cost cousins ---
        new("kicker", "Kicker alt-cost (CR 702.33)",
            "CR 702.33",
            "Optional additive cast cost on a spell; flips a runtime 'kicked' flag the rest of the spell pipeline can branch on.",
            new[]
            {
                Rx(@"\bkicker\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bkicker"),
            }),
        new("dash", "Dash alt-cost (CR 702.108)",
            "CR 702.108",
            "Alternate cast cost + 'return at end of turn' delayed trigger + haste-while-on-battlefield rider.",
            new[]
            {
                Rx(@"\bdash\b\s*\{?.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bdash\b"),
            }),
        new("suspend", "Suspend alt-cost (CR 702.61)",
            "CR 702.61",
            "Exile-from-hand alt cost with time counters; upkeep auto-cast when last counter is removed.",
            new[]
            {
                Rx(@"\bsuspend\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bsuspend"),
            }),
        new("companion", "Companion runtime cast-from-outside (CR 702.139)",
            "CR 702.139",
            "Deck-construction predicate shipped via ICompanionRestriction + CompanionValidator. Remaining gap: 'cast from outside the game' once-per-game pipeline (needs a sideboard zone + Player.CompanionUsedThisGame ledger).",
            new[]
            {
                Rx(@"companion\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bcompanion"),
            }),
        new("morph", "Morph / face-down cast (CR 702.37)",
            "CR 702.37",
            "Face-down 2/2 creature spell cast for {3}; turn-face-up alt cost.",
            new[]
            {
                Rx(@"\bmorph\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bmorph"),
            }),

        // --- Equipment ---
        new("equip-primitive", "Equip activated-ability primitive (CR 702.6)",
            "CR 702.6",
            "EquipActivatedAbility — sorcery-speed activation, attaches/re-attaches Equipment to a chosen creature.",
            new[]
            {
                Rx(@"equip[- ]ability primitive\b"),
                Rx(@"equip\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bequip\b"),
                Rx(@"equip activation\b.{0,80}\bdefer"),
            }),

        // --- Cast tracking ---
        new("cast-marker", "Cast-marker on Card",
            null,
            "Persistent 'this object was cast (vs. put onto the battlefield)' flag — Bloodghast, The One Ring, Pact triggers all key off it.",
            new[]
            {
                Rx(@"cast[- ]marker\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bcast[- ]marker"),
                Rx(@"""if you cast it""\b.{0,80}\bdefer"),
            }),
        new("mana-provenance", "Mana provenance ledger",
            null,
            "Per-spell record of which colours of mana were spent on the cost — required for Prismatic Ending / Converge / mana-cost colour gates.",
            new[]
            {
                Rx(@"mana provenance\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\bmana provenance"),
            }),

        // --- Layer-6 grants ---
        new("layer6-ability-grant", "Layer-6 ability-grant subsystem (CR 613.1f)",
            "CR 613.1f",
            "ContinuousEffectsService needs an 'ability grant' layer so 'gains <ability>' effects flow through GetAbilities() at read time.",
            new[]
            {
                Rx(@"layer[- ]?6\b.{0,80}\bdefer"),
                Rx(@"ability[- ]grant\b.{0,80}\bdefer"),
                Rx(@"\bdefer\w*\b.{0,80}\b(layer[- ]?6|ability[- ]grant)"),
                Rx(@"grant[- ]on[- ]attach\b.{0,80}\bdefer"),
            }),
    };

    /// <summary>
    /// Look up the canonical primitive whose registry pattern first
    /// matches the supplied sentence. Returns null if nothing matches.
    /// </summary>
    public static MechanicPrimitive? Match(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return null;
        foreach (var p in All)
        {
            foreach (var rx in p.MatchPatterns)
            {
                if (rx.IsMatch(sentence)) return p;
            }
        }
        return null;
    }
}
