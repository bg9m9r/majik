using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Faerie Macabre (Morningtide, {1}{B}{B}).
///
/// Creature — Faerie Rogue 2/2. Oracle text:
///   "Flash
///    Flying
///    Discard Faerie Macabre: Exile up to two target cards from
///    graveyards."
///
/// ## Implemented (v1)
/// - <b>Creature — Faerie Rogue</b> 2/2 {1}{B}{B} with owner / controller
///   wiring.
/// - <b>Flash</b> + <b>Flying</b> as <see cref="KeywordAbility"/> markers
///   (CR 702.8 / 702.9). The data-driven load route gets these via
///   <see cref="Majik.Core.CardData.Parsing.KeywordBinder"/>; the
///   <see cref="NamedCardFactory"/> path attaches them inline so shape
///   tests can read them off the card.
/// - <b>Discard Faerie Macabre: Exile up to two target cards from
///   graveyards</b> (CR 117 — activated ability with no mana cost and a
///   non-mana additional cost). Wired as an
///   <see cref="ActivatedAbility"/> whose only cost is
///   <see cref="DiscardSelfCost"/> (the cost itself gates activation to
///   the controller's <see cref="ZoneType.Hand"/> per CR 702.74a — same
///   zone-gate every Channel land uses). The ability declares a single
///   0..2 "target card in a graveyard" <see cref="TargetRequest"/> with
///   <c>MinTargets=0</c> so the activation can resolve without any
///   target (CR 115.1b — "up to" lets the chooser pick zero). On
///   resolution iterates the chosen targets in agent order, gating each
///   pick on still being in a graveyard (CR 608.2b — illegal target →
///   that target does nothing; the activation as a whole still
///   resolves), and moves it to its owner's <see cref="ZoneType.Exile"/>.
///
/// ## Lifecycle
/// Discarding Faerie Macabre as the cost moves the card itself from
/// Hand → Graveyard before the exile effect resolves — so Faerie
/// Macabre's own card lands in its owner's graveyard during cost
/// payment, and the controller may (if legal at resolution) target it
/// for self-exile if desired. Cost payment routes through
/// <see cref="DiscardSelfCost"/> which moves Hand → Graveyard via
/// <see cref="IZone.AddCard"/> (Zone.AddCard calls
/// <see cref="ICard.SetZone"/> internally — same wiring Channel lands
/// take).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent target prompt</b>: v1 honours <c>ChosenTargets</c> when
///   set (test-driven) and otherwise resolves to a no-op (no
///   deterministic auto-pick — mirrors Cling to Dust's
///   <c>MinTargets=0</c> posture for "up to" rider). Full agent-driven
///   targeting deferred.
/// - <b>ZoneService routing for the exile move</b>: v1 performs raw
///   zone manipulation (Graveyard → Exile) — same shape Tormod's Crypt
///   / Nihil Spellbomb take. Wire ZoneService through when the broader
///   graveyard-hate sweep audit lands.
/// </summary>
[CardName("Faerie Macabre")]
public static class FaerieMacabreFactory
{
    public const string CardName = "Faerie Macabre";
    public const string PrintedManaCost = "{1}{B}{B}";

    /// <summary>
    /// Construct Faerie Macabre. The discard-self activated ability is
    /// attached to the card shape; activation is gated to the controller's
    /// hand by <see cref="DiscardSelfCost.CanPay"/> (CR 702.74a-style
    /// activation-zone check).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Faerie, CardSubtype.Rogue });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Keyword markers — CR 702.8 (Flash), CR 702.9 (Flying). When this
        // factory is used directly (test / NamedCardFactory path) the
        // markers aren't supplied by KeywordBinder, so attach them inline
        // for consistency with SolitudeFactory / EnduranceFactory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Discard Faerie Macabre: Exile up to two target cards from
        // graveyards.
        //
        // CR 117 — activated ability with a non-mana cost only. Cost:
        // DiscardSelfCost (gates activation to controller's Hand per
        // CR 702.74a-style zone check via DiscardSelfCost.CanPay).
        // Target: single 0..2 TargetRequest "target card in a graveyard"
        // (CR 115.1b — "up to two" means MinTargets=0). On resolution
        // iterates chosen targets, gates each on still being in a
        // graveyard (CR 608.2b), and moves each to its owner's Exile.
        // ----------------------------------------------------------------
        ActivatedAbility? exileAbility = null;
        var exileEffect = new Effect(
            $"{CardName}: exile up to two target cards from graveyards",
            () =>
            {
                if (exileAbility == null) return;
                if (exileAbility.ChosenTargets.Count == 0) return;

                var slot = exileAbility.ChosenTargets[0];
                // CR 115.1b — "up to two" tolerates 0/1/2 picks.
                var picks = slot.Count > 2 ? slot.Take(2).ToList() : slot.ToList();

                foreach (var raw in picks)
                {
                    if (raw is not ICard target) continue;

                    // CR 608.2b — illegal-on-resolution recheck. Target
                    // must still be in a graveyard.
                    if (target.Zone != ZoneType.Graveyard) continue;
                    if (target.Owner == null) continue;

                    target.Owner.Zones.Graveyard.RemoveCard(target);
                    target.Owner.Zones.Exile.AddCard(target);
                    target.SetZone(ZoneType.Exile);
                }
            });

        exileAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new DiscardSelfCost(card) },
            effects: new IEffect[] { exileEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to two target cards in graveyards",
                    MinTargets: 0,
                    MaxTargets: 2,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(exileAbility);

        return card;
    }
}
