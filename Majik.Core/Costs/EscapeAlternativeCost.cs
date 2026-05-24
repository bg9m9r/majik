using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.138 — Escape. "Escape—[cost], Exile N other cards from your
/// graveyard." A two-part alternative cost on cards that have the Escape
/// keyword while in the graveyard:
///   1. a mana cost (the printed "Escape—" mana payment) that REPLACES
///      the printed mana cost, and
///   2. an additional rider that exiles N <em>other</em> cards from the
///      caster's graveyard (the spell itself is the "this card" the
///      "other" carves out).
///
/// CR 702.138a routes the cost-payment shape through the alternative-cost
/// rules (CR 601.2b + CR 601.2f–h). CR 702.138b stamps a runtime
/// "escaped" flag on the spell + resulting permanent, consumed by
/// downstream gates (Uro's "sacrifice it unless it escaped" trigger
/// being the canonical case; future <em>escapes with [counters]</em>
/// riders per CR 702.138c are reserved for later wiring).
///
/// ## Shape vs. other graveyard-cast alt costs
///
/// Mirrors <see cref="FlashbackAlternativeCost"/>'s cast-from-graveyard
/// zone gate but does NOT exile the card after resolution — an escaped
/// creature goes to the battlefield like any normal cast (CR 608.2,
/// permanents → battlefield, instants/sorceries → graveyard). The
/// post-resolution exile rider is Flashback-specific (CR 702.34b);
/// Escape has no analogous rider in CR 702.138.
///
/// The "exile N other cards from your graveyard" portion is paid in
/// <see cref="Pay"/> at cast-announce time (parallels
/// <see cref="ExileCardsFromGraveyardAdditionalCost.Pay"/>) — the
/// engine cast-flow drives the payment via the standard
/// alt-cost-side-effect hook (<see cref="OnResolved"/>) only for
/// post-resolution bookkeeping. Mana payment is owned by the cast
/// flow's normal mana resolver against
/// <see cref="AlternativeManaCost"/>.
///
/// ## Atomicity
///
/// <see cref="Pay"/> is "all-or-nothing": if the caster's graveyard
/// can't field N other cards the call returns false and no zones are
/// mutated. Once Pay starts moving cards it commits — partial failure
/// inside the loop is impossible because graveyard cards are pure data
/// objects with no payment side-effects, but the post-condition is
/// still "either every requested card moved or zero moved" to match
/// CR 601.2g.
/// </summary>
public sealed class EscapeAlternativeCost : IAlternativeCost
{
    private readonly List<ICard> _exiled = new();

    /// <summary>Number of OTHER graveyard cards to exile.</summary>
    public int ExileFromGraveyardCount { get; }

    /// <summary>
    /// Cards actually exiled by <see cref="Pay"/>. Empty before payment;
    /// stable thereafter. Exposed for diagnostics + escape-count consumers
    /// (e.g. future <em>escapes with [counters]</em> hooks).
    /// </summary>
    public IReadOnlyList<ICard> ExiledCards => _exiled.AsReadOnly();

    /// <summary>
    /// True after <see cref="Pay"/> succeeded. Read by tests + downstream
    /// gates that want to confirm the additional-cost rider actually fired
    /// (independent of mana payment, which is the cast flow's
    /// responsibility).
    /// </summary>
    public bool Paid { get; private set; }

    public string Description =>
        $"Escape — {AlternativeManaCost}, exile {ExileFromGraveyardCount} other card(s) from your graveyard";

    /// <inheritdoc/>
    public ManaCost AlternativeManaCost { get; }

    public EscapeAlternativeCost(ManaCost alternativeManaCost, int exileFromGraveyardCount)
    {
        if (exileFromGraveyardCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(exileFromGraveyardCount));
        AlternativeManaCost = alternativeManaCost ?? throw new ArgumentNullException(nameof(alternativeManaCost));
        ExileFromGraveyardCount = exileFromGraveyardCount;
    }

    /// <summary>
    /// CR 702.138a — Escape only functions while the card is in a
    /// graveyard, AND the caster must own the card (the printed text says
    /// "<em>your</em> graveyard"). Additionally, there must be enough
    /// OTHER cards in the same graveyard to cover the
    /// <see cref="ExileFromGraveyardCount"/> rider (CR 601.2g — illegal
    /// to announce a cost you can't pay).
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;
        if (card.Zone != ZoneType.Graveyard) return false;
        if (!ReferenceEquals(card.Owner, caster)) return false;
        return IsLegalInContext(caster, card);
    }

    /// <summary>
    /// Standalone legality probe — checks the additional-cost rider can be
    /// covered without already committing to a cast. Used by
    /// <see cref="Majik.Core.Players.Agents.EscapeAltCostProbe"/> /
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> as a fast pre-filter
    /// and exposed for unit tests that want to assert legality without
    /// driving the full cast.
    /// </summary>
    public bool IsLegalInContext(Player caster, ICard card)
    {
        if (caster == null || card == null) return false;
        // "Other" — the card being cast is not counted (CR 702.138a:
        // "exile [N] other cards"). Treat the spell's own graveyard
        // presence as the excluded entry.
        var otherCount = caster.Zones.Graveyard.GetCards()
            .Count(c => !ReferenceEquals(c, card));
        return otherCount >= ExileFromGraveyardCount;
    }

    /// <summary>
    /// Pay the exile rider: pick N other graveyard cards (deterministic
    /// v1 — first-N order matches
    /// <see cref="ExileCardsFromGraveyardAdditionalCost.Pay"/>'s posture;
    /// agent-driven pick is a future surface) and move them
    /// Graveyard → Exile via raw zone mutation. The mana portion is paid
    /// by the cast flow's mana resolver against
    /// <see cref="AlternativeManaCost"/>; this method is the
    /// non-mana side of CR 702.138a.
    ///
    /// Returns false (and mutates no zones) when the graveyard can't
    /// field N OTHER cards — matches the
    /// <see cref="ExileCardsFromGraveyardAdditionalCost"/> pre-flight
    /// posture and lets <see cref="Majik.Core.Game.SpellCastFlow"/>'s
    /// "fail before mutating" guard short-circuit cleanly.
    /// </summary>
    public bool Pay(Player caster, ICard card)
    {
        if (Paid) return true;
        if (!IsLegalInContext(caster, card)) return false;

        var picks = caster.Zones.Graveyard.GetCards()
            .Where(c => !ReferenceEquals(c, card))
            .Take(ExileFromGraveyardCount)
            .ToList();
        if (picks.Count < ExileFromGraveyardCount) return false;

        foreach (var pick in picks)
        {
            caster.Zones.Graveyard.RemoveCard(pick);
            caster.Zones.Exile.AddCard(pick);
            pick.SetZone(ZoneType.Exile);
            _exiled.Add(pick);
        }

        Paid = true;
        return true;
    }

    /// <summary>
    /// CR 702.138 — no post-resolution rider. An escaped creature lands
    /// on the battlefield, an escaped instant / sorcery lands in the
    /// graveyard, both per the default destination (CR 608.2). The
    /// "escaped" runtime stamp on the spell (consumed by Uro's
    /// sac-unless-escaped trigger) is set at cast time by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> via
    /// <see cref="Majik.Core.Spells.Spell.WasCastForEscape"/>.
    /// </summary>
    public void OnResolved(ICard card, Player caster) { /* default destination */ }
}
