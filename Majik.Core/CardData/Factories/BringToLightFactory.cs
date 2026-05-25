using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bring to Light (Battle for Zendikar, {2}{G}{W}{U}).
///
/// Sorcery. Oracle text:
///   "Converge — Search your library for a creature, instant, or sorcery
///    card with mana value less than or equal to the number of colors of
///    mana spent to cast this spell, exile it, then shuffle. You may
///    cast that card without paying its mana cost if five or more
///    colors of mana were spent to cast this spell."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{G}{W}{U}.
/// - <b>Converge bound</b> via a caller-supplied
///   <c>Func&lt;int&gt; colorsSpentProvider</c> — same mana-provenance
///   shape as <see cref="PrismaticEndingFactory"/>. When null, the cap
///   defaults to <see cref="DefaultColorsSpent"/> (3 — the printed
///   minimum count of distinct colored pips: G, W, U). Real cast-time
///   provenance can be plugged in once the mana resolver exposes a
///   distinct-colors ledger to spell definitions; until then the
///   default models the printed pips.
/// - Tutor body via <see cref="BuildResolveEffect"/>:
///   1. Optional caller-supplied <c>tutorSelector</c> picks the card.
///      When null, falls back to <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
///      filtered to legal candidates (Creature / Instant / Sorcery
///      with mv ≤ cap). When no agent is registered, picks the first
///      legal candidate deterministically (dispatcher / shape-test
///      posture mirroring Scapeshift / Eternal Witness).
///   2. Validates the pick (still in library, correct type, mv ≤ cap).
///   3. Moves Library → Exile (CR 701.21).
///   4. Shuffles via <see cref="LibraryShuffle.ShuffleLibrary"/>
///      (CR 701.20a).
/// - Always exiles the tutored card. The "may cast for free if N ≥ 5"
///   rider is documented below as a v1 gap; the card sits in exile until
///   the free-cast pipeline lands. Bot heuristics already prefer to
///   exile useful Modern targets (Cryptic Command, Snapcaster Mage,
///   Karn Liberated) where the card-advantage line is worthwhile even
///   without the bonus free cast — same posture every wish-cycle
///   factory uses for the "you may cast" rider.
///
/// ## Deferred (v1 gaps)
/// - <b>Free cast at N ≥ 5</b>: the engine has no in-spell "now cast
///   that exiled card without paying its mana cost" hook yet (the
///   closest primitive is <see cref="Majik.Core.Costs.CastFromExileAlternativeCost"/>,
///   which gates a subsequent cast). Cascade-style mid-resolution
///   alt-cast pipeline is a separate workstream — when it lands, the
///   resolve closure should branch on <c>colorsSpent &gt;= 5</c> and
///   invoke the same shape Bloodbraid Elf / Shardless Agent use.
/// - <b>Mana provenance ledger</b>: matches Prismatic Ending's gap —
///   real distinct-colors-spent provenance requires the cost flow to
///   expose a per-spell ledger of paid pips. Until then callers
///   supply <c>colorsSpentProvider</c> explicitly (tests do; the
///   dispatcher path uses <see cref="DefaultColorsSpent"/>).
/// - <b>"You may" cast decline</b>: collapses with the free-cast gap —
///   when free-cast wires up, the picker also needs an opt-in prompt.
/// </summary>
[CardName("Bring to Light")]
public static class BringToLightFactory
{
    public const string CardName = "Bring to Light";
    public const string PrintedManaCost = "{2}{G}{W}{U}";

    /// <summary>
    /// Default colors-spent cap when no provider is supplied. The
    /// printed cost has 3 distinct colored pips ({G}, {W}, {U}), so 3
    /// is the floor any legal cast must reach — a defensible floor
    /// until mana-provenance is wired.
    /// </summary>
    public const int DefaultColorsSpent = 3;

    /// <summary>
    /// Threshold at which the printed "you may cast without paying" rider
    /// activates. Tracked as a constant so the free-cast workstream (see
    /// class xmldoc) can reuse it.
    /// </summary>
    public const int FreeCastThreshold = 5;

    /// <summary>
    /// Construct Bring to Light as a Sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve closure is produced by
    /// <see cref="BuildResolveEffect"/> / <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Bring to Light. No
    /// target requests — the converge tutor resolves via library search
    /// (CR 701.19a) rather than a cast-time target.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<int>? colorsSpentProvider = null,
        Func<Player, int, ICard?>? tutorSelector = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, colorsSpentProvider, tutorSelector));
    }

    /// <summary>
    /// Build the resolve effect: tutor + exile + shuffle. The cap is read
    /// from <paramref name="colorsSpentProvider"/> when supplied,
    /// otherwise <see cref="DefaultColorsSpent"/>. The pick is supplied
    /// by <paramref name="tutorSelector"/> when present; otherwise the
    /// caster's registered agent is queried, with a deterministic
    /// first-legal-candidate fallback for the dispatcher path.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        Func<int>? colorsSpentProvider,
        Func<Player, int, ICard?>? tutorSelector)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: Converge tutor creature/instant/sorcery mv ≤ N, exile, shuffle.",
                () =>
                {
                    var cap = colorsSpentProvider?.Invoke() ?? DefaultColorsSpent;
                    if (cap < 0) cap = 0;

                    // ---- 1. Pick a legal card -----------------------------
                    ICard? pick = null;
                    if (tutorSelector != null)
                    {
                        pick = tutorSelector(caster, cap);
                    }
                    else
                    {
                        var candidates = LegalCandidates(caster, cap);
                        if (candidates.Count == 0) return; // no legal pick — CR 701.19a shuffle still runs

                        var agent = AgentRegistry.Get(caster);
                        pick = agent != null
                            ? agent.ChooseLibraryPickAsync(
                                    ctx: null, candidates,
                                    "creature, instant, or sorcery card with mv ≤ colors spent")
                                .GetAwaiter().GetResult()
                            : candidates[0];
                    }

                    // ---- 2. Validate ---------------------------------------
                    if (pick != null
                        && pick.Zone == ZoneType.Library
                        && caster.Zones.Library.GetCards().Contains(pick)
                        && IsLegalPick(pick, cap))
                    {
                        // CR 701.21 — exile.
                        caster.Zones.Library.RemoveCard(pick);
                        caster.Zones.Exile.AddCard(pick);
                        pick.SetZone(ZoneType.Exile);

                        // v1 gap (see class xmldoc): "you may cast that card
                        // without paying its mana cost if five or more colors
                        // of mana were spent" — deferred. Card sits in exile.
                    }

                    // ---- 3. Shuffle (CR 701.20a) ----------------------------
                    LibraryShuffle.ShuffleLibrary(caster, "bring-to-light");
                }),
        };
    }

    /// <summary>
    /// Legality predicate for a Bring to Light tutor pick — Creature,
    /// Instant, or Sorcery card with mana value ≤ <paramref name="cap"/>.
    /// </summary>
    public static bool IsLegalPick(ICard card, int cap)
    {
        if (card == null) return false;
        if (card is not Card concrete) return false;
        if (concrete.ManaCostValue.TotalValue > cap) return false;
        return card.HasType(CardType.Creature)
            || card.HasType(CardType.Instant)
            || card.HasType(CardType.Sorcery);
    }

    /// <summary>
    /// Helper: return all cards in <paramref name="caster"/>'s library
    /// that satisfy <see cref="IsLegalPick"/> for the given
    /// <paramref name="cap"/>. Surfaced for tests / bot heuristics.
    /// </summary>
    public static IReadOnlyList<ICard> LegalCandidates(Player caster, int cap)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return caster.Zones.Library.GetCards()
            .Where(c => IsLegalPick(c, cap))
            .ToList();
    }
}
