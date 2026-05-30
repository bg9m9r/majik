using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reveillark (Morningtide / Modern Masters, {4}{W}).
///
/// Creature — Elemental 4/3. Oracle text (verified against Scryfall):
///   "Flying
///    When this creature leaves the battlefield, return up to two target
///    creature cards with power 2 or less from your graveyard to the
///    battlefield.
///    Evoke {5}{W} (You may cast this spell for its evoke cost. If you do,
///    it's sacrificed when it enters.)"
///
/// Reveillark is the white half of the Lorwyn-block evoke cycle and the
/// canonical "evoke for the leaves-the-battlefield value" engine: hard-cast
/// {4}{W} for a 4/3 flier, or evoke {5}{W} so it enters and is immediately
/// sacrificed (CR 702.74b) — its sacrifice IS a leaves-the-battlefield event
/// that returns up to two small creatures, so the evoke line is the whole
/// point of the card.
///
/// The base shape (name, Creature, Elemental subtype, {4}{W}, 4/3) is
/// materialised from the embedded JSON definition (<c>reveillark.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="ThragtuskFactory"/>. The keyword markers + the LTB
/// reanimation trigger are layered on here because the JSON
/// <c>AbilityDefinition</c> schema doesn't express Flying / Evoke / a
/// leaves-the-battlefield reanimation.
///
/// ## Implemented (v1)
/// - 4/3 Creature — Elemental at {4}{W}, owner / controller wired.
/// - <b>Flying</b> (CR 702.9) — <see cref="KeywordAbility"/> marker,
///   consumed by the combat-validator block restrictions (same shape as
///   <see cref="MulldrifterFactory"/> / <see cref="SoulherderFactory"/>).
/// - <b>Evoke {5}{W}</b> keyword marker via <see cref="KeywordAbility"/>
///   ("Evoke"). The pure-mana evoke alt-cost is announced at cast time via
///   <see cref="EvokeAlternativeCost"/>(ManaCost.Parse("{5}{W}")); the
///   printed "when this enters, if its evoke cost was paid, sacrifice it"
///   trigger (CR 702.74b) is attached via <see cref="EvokeFactory.Build"/>
///   (same shape as Mulldrifter). The evoke sacrifice IS the trigger for
///   the LTB reanimation below.
/// - <b>LTB triggered ability (CR 603.6c / CR 603.10c)</b>: fires whenever
///   Reveillark moves OUT of the battlefield to any destination (dies,
///   bounce, exile, evoke-sacrifice, flicker all qualify) — filtered via
///   <see cref="CardMovedEvent"/> with <c>FromZone == Battlefield</c> (same
///   shape as <see cref="ThragtuskFactory"/>'s LTB and
///   <see cref="SkyclaveApparitionFactory"/>). On resolution it returns up
///   to two target creature cards with power 2 or less from the
///   controller's graveyard to the battlefield under the controller's
///   control (CR 701.20), reusing the
///   <see cref="Fx.ReturnFromGraveyardToBattlefield"/> plumbing
///   (<see cref="ReanimateFactory"/> / <see cref="GravediggerFactory"/>).
///   The 0..2 target slot honours an agent-set selection; absent one it
///   deterministically picks the first (up to two) legal creature cards in
///   the controller's graveyard (CR 608.2b — invalid / missing picks are a
///   clean no-op), mirroring Gravedigger's first-card fallback.
///
/// ## "power 2 or less" filter (CR 109.3 / CR 208)
/// "creature card with power 2 or less" reads the card's printed/base power
/// while it is in the graveyard (a card in a zone other than the
/// battlefield has only its characteristics defining values — CR 208.2),
/// so the candidate filter uses <see cref="Creature.BasePower"/> &lt;= 2.
///
/// ## Deferred (v1 gaps)
/// - <b>Real agent-driven "up to two" prompt</b>: production callers wire
///   <see cref="TriggeredAbility.SetChosenTargets"/> from an agent prompt
///   before the trigger resolves — same pattern as Gravedigger / Soulherder.
///   The first-cards fallback is the dispatcher-path safety net.
/// - <b>"New object" semantics on return (CR 400.7)</b>: v1 reuses the same
///   <see cref="Card"/> instances — identity-sensitive riders would diverge
///   from paper (same posture as Soulherder / Reanimate).
/// </summary>
[CardName("Reveillark")]
public static class ReveillarkFactory
{
    public const string CardName = "Reveillark";
    public const string Slug = "reveillark";
    public const string EvokeCost = "{5}{W}";

    /// <summary>Maximum power a graveyard creature card may have to be a
    /// legal reanimation target (CR — "power 2 or less").</summary>
    public const int MaxReturnedPower = 2;

    /// <summary>Up-to-two targets (CR — "up to two target creature cards").</summary>
    public const int MaxTargets = 2;

    /// <summary>
    /// Construct Reveillark with no live wiring. Keyword markers + the evoke
    /// sacrifice trigger + the LTB reanimation trigger are attached for
    /// shape observability; the LTB trigger is NOT registered with a
    /// <see cref="TriggerManager"/> and the reanimation move bypasses
    /// <see cref="ZoneService"/> when the effect is invoked manually.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Reveillark with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the LTB graveyard → battlefield
    /// moves route through <see cref="ZoneService.MoveCard"/> so the
    /// reanimated creatures' ETB triggers fire (CR 603.6a).</param>
    /// <param name="triggers">When supplied, the evoke sacrifice trigger +
    /// the LTB reanimation trigger register with the bus so their events
    /// land them on the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elemental subtype, {4}{W}, 4/3). The JSON carries no abilities —
        // the keyword markers + the LTB reanimation are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Keyword markers — CR 702.9 (Flying), CR 702.74 (Evoke). Attach
        // inline so the NamedCardFactory path matches the data-driven
        // KeywordBinder result (same shape as Mulldrifter / Soulherder).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Evoke", card, owner));

        // ----------------------------------------------------------------
        // Printed evoke sacrifice trigger (CR 702.74b).
        //   "When this creature enters, if its evoke cost was paid,
        //    sacrifice it."
        // Pure-mana evoke ({5}{W}) — alt-cost announced at cast time via
        // EvokeAlternativeCost(ManaCost.Parse("{5}{W}")). OnResolved flips
        // Creature.EvokeWasPaid; the intervening-if reads it at queue-time
        // (CR 603.4). The sacrifice it produces is itself a
        // leaves-the-battlefield event, feeding the LTB reanimation below.
        // ----------------------------------------------------------------
        var evokeSac = EvokeFactory.Build(card);
        card.AddAbility(evokeSac);
        triggers?.RegisterTriggeredAbility(evokeSac);

        // ----------------------------------------------------------------
        // LTB reanimation trigger — CR 603.6c / CR 603.10c / CR 701.20.
        //   "When this creature leaves the battlefield, return up to two
        //    target creature cards with power 2 or less from your graveyard
        //    to the battlefield."
        // Fires whenever Reveillark moves OUT of Battlefield (any
        // destination — dies / bounce / exile / evoke-sacrifice / flicker),
        // same FromZone == Battlefield shape as Thragtusk's LTB.
        // ----------------------------------------------------------------
        TriggeredAbility? ltb = null;

        var ltbEffect = new Effect(
            $"{CardName}: return up to two target creature cards with power 2 or less from your graveyard to the battlefield",
            () => ResolveReanimate(card, owner, ltb, zones));

        ltb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                ReferenceEquals(e.Card, card)
                && e.FromZone == ZoneType.Battlefield),
            effects: new IEffect[] { ltbEffect },
            interveningIf: null,
            // CR 603.6d — leaves-the-battlefield abilities look back in time
            // at the game state just before the event; ActiveZones =
            // Battlefield matches Thragtusk / Skyclave Apparition.
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to two target creature cards with power 2 or less in your graveyard",
                    MinTargets: 0,
                    MaxTargets: MaxTargets,
                    LegalCandidates: LegalTargets(owner).Cast<object>().ToList()),
            });

        card.AddAbility(ltb);
        triggers?.RegisterTriggeredAbility(ltb);

        return card;
    }

    /// <summary>
    /// Legal reanimation candidates: creature cards with base power 2 or
    /// less in <paramref name="controller"/>'s graveyard (CR 109.3 /
    /// CR 208.2 — power read off the card's defining values in the
    /// graveyard).
    /// </summary>
    private static IReadOnlyList<Creature> LegalTargets(Player controller) =>
        controller.Zones.Graveyard.GetCards()
            .OfType<Creature>()
            .Where(c => c.BasePower <= MaxReturnedPower)
            .ToList();

    /// <summary>
    /// Shared LTB resolution. Reads the trigger's
    /// <see cref="TriggeredAbility.ChosenTargets"/> (production path); falls
    /// back to the first (up to two) legal creature cards in the
    /// controller's graveyard when none was set (deterministic dispatcher
    /// posture — mirrors <see cref="GravediggerFactory"/>'s first-card
    /// fallback). Each pick is re-validated at resolution (CR 608.2b —
    /// still in the controller's graveyard, still a creature card, still
    /// power 2 or less) before being returned to the battlefield under the
    /// controller's control via <see cref="Fx.ReturnFromGraveyardToBattlefield"/>.
    /// </summary>
    private static void ResolveReanimate(
        Creature reveillark,
        Player owner,
        TriggeredAbility? ltb,
        ZoneService? zones)
    {
        // CR 110.2 — "your graveyard" is the controller's graveyard. Snapshot
        // the controller as of the event (control-change edge cases).
        var controller = reveillark.Controller ?? owner;

        var picks = new List<Creature>();

        // 1) Honour agent-set targets if present (production path).
        if (ltb != null && ltb.ChosenTargets.Count > 0)
        {
            foreach (var slot in ltb.ChosenTargets)
            {
                foreach (var obj in slot)
                {
                    if (obj is Creature chosen && !picks.Contains(chosen))
                    {
                        picks.Add(chosen);
                    }
                }
            }
        }

        // 2) Deterministic fallback — first (up to two) legal creature cards
        // in the controller's graveyard (single-arg dispatcher path).
        if (picks.Count == 0)
        {
            picks.AddRange(LegalTargets(controller).Take(MaxTargets));
        }

        // CR — "up to two": never return more than the maximum.
        foreach (var pick in picks.Take(MaxTargets))
        {
            // CR 608.2b illegal-on-resolution re-checks:
            //   (a) still in the controller's graveyard,
            //   (b) still a creature card with power 2 or less.
            if (pick.Zone != ZoneType.Graveyard) continue;
            if (!controller.Zones.Graveyard.GetCards().Contains(pick)) continue;
            if (pick.BasePower > MaxReturnedPower) continue;

            // CR 701.20 — graveyard → battlefield under the controller's
            // control. ZoneService path fires the reanimated creature's ETB
            // triggers (CR 603.6a); raw-zone fallback otherwise.
            Fx.ReturnFromGraveyardToBattlefield(pick, controller, zones);
        }
    }
}
