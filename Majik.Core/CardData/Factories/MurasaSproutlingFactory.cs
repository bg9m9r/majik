using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Murasa Sproutling (Modern Horizons 3, {2}{G}).
///
/// Creature — Plant Elemental 3/3. Oracle text:
///   "Kicker {1}{G} (You may pay an additional {1}{G} as you cast this spell.)
///    When this creature enters, if it was kicked, return target card with a
///    kicker ability from your graveyard to your hand."
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: 3/3 Creature — Plant Elemental at printed cost
///   {2}{G}, plus a printed "Kicker" <see cref="KeywordAbility"/> marker
///   (same shape Vines of Vastwood / Burst Lightning expose) so the card is
///   itself detectable as "a card with a kicker ability"
///   (<see cref="KickerAbilityDetector"/>).
/// - <b>Kicker additional cost (CR 702.33)</b>: <see cref="BuildAdditionalCost"/>
///   constructs the {1}{G} <see cref="KickerAdditionalCost"/> rider — the
///   caller (tests, bot decision layer) layers it onto the cast via
///   <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c>.
///   <see cref="KickerAdditionalCost.Pay"/> stamps <see cref="Card.WasKicked"/>
///   at cast announcement (mirrored live on the card by
///   <see cref="Majik.Core.Game.SpellCastFlow"/>) and the post-resolution
///   cleanup clears it (CR 400.7).
/// - <b>ETB triggered ability (CR 603.6a) with intervening-if (CR 603.4 /
///   702.33b)</b>: fires on Stack → Battlefield. The resolve body reads the
///   intervening-if "if it was kicked" via <see cref="Card.WasKicked"/> and
///   short-circuits to a clean no-op when the kicker wasn't paid (same
///   intervening-if-collapse convention used by
///   <see cref="RecklessBushwhackerFactory"/>'s surge branch — the trigger
///   structurally goes on the stack but does nothing on resolution per
///   CR 603.4).
/// - <b>"card with a kicker ability" target (CR 702.32 / 702.33)</b>: a
///   bespoke 1..1 <see cref="TargetRequest"/> exposing every card in the
///   controller's graveyard that <see cref="KickerAbilityDetector"/> flags
///   as having a printed Kicker / Multikicker ability. This is the kicker
///   analogue of <see cref="GravediggerFactory"/>'s "creature card" filter
///   (which filters on <see cref="CardType.Creature"/>) — same
///   return-from-graveyard return-template, different candidate predicate.
/// - <b>Resolution</b>: reads <see cref="TriggeredAbility.ChosenTargets"/>;
///   falls back to the first kicker card in the controller's graveyard when
///   no target was set (deterministic single-arg / no-agent posture —
///   mirrors <see cref="EternalWitnessFactory"/> / <see cref="GravediggerFactory"/>);
///   re-validates the chosen card is STILL a kicker card in the controller's
///   graveyard at resolution (CR 608.2b — illegal target → clean no-op);
///   moves Graveyard → Hand via <see cref="ZoneService.MoveCard"/> when
///   supplied so any downstream zone-change triggers fire (CR 603.6a /
///   CR 701.20), otherwise direct-zone mutation.
///
/// ## Notes
/// - <b>"return target …" is mandatory</b> (no "you may") when kicked, so the
///   ETB always returns a card if a legal kicker card exists; the only opt-out
///   is the intervening-if (it wasn't kicked).
/// - <b>Reach to the bot kicker-probe layer</b>: Murasa Sproutling is a
///   creature ETB, not a spell rider the bot bids on at cast time the way
///   <see cref="Players.Agents.KickerAltCostProbe"/> handles instants — the
///   bot pays the kicker as an additional cost via the normal cast path.
/// </summary>
[CardName("Murasa Sproutling")]
public static class MurasaSproutlingFactory
{
    public const string CardName = "Murasa Sproutling";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>CR 702.33 — printed Kicker cost: {1}{G}.</summary>
    public const string KickerCostText = "{1}{G}";

    public const int BasePower = 3;
    public const int BaseToughness = 3;

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "Kicker {1}{G} (You may pay an additional {1}{G} as you cast this spell.) " +
        "When this creature enters, if it was kicked, return target card with a " +
        "kicker ability from your graveyard to your hand.";

    /// <summary>
    /// Construct Murasa Sproutling with no runtime wiring. Produces the
    /// correct card identity + Kicker marker + ETB trigger shape (the
    /// trigger is NOT registered with a <see cref="TriggerManager"/>).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Fully-wired construction. When <paramref name="triggers"/> is
    /// supplied the ETB ability is registered for bus-driven firing; when
    /// <paramref name="zoneService"/> is supplied the Graveyard → Hand move
    /// routes through <see cref="ZoneService.MoveCard"/> so downstream
    /// zone-change triggers fire (CR 603.6a / CR 701.20).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: BasePower,
            toughness: BaseToughness,
            supertypes: null,
            subtypes: new[] { CardSubtype.Plant, CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.33 — printed Kicker keyword marker. Same observable shape
        // Vines of Vastwood / Burst Lightning expose via WithKeyword, so
        // Murasa Sproutling is itself a "card with a kicker ability"
        // (KickerAbilityDetector reads this marker).
        card.AddAbility(new KeywordAbility(
            KickerAbilityDetector.KickerKeyword, card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a + intervening-if CR 603.4 /
        // 702.33b.
        //   "When this creature enters, if it was kicked, return target card
        //    with a kicker ability from your graveyard to your hand."
        //
        // Candidate predicate: cards in the controller's graveyard that
        // have a printed kicker ability (KickerAbilityDetector). This is
        // the kicker analogue of Gravedigger's "creature card" filter.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: if kicked, return target card with a kicker ability from your graveyard to your hand",
            () => ResolveReturnKickerCard(card, owner, etb, zoneService));

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target card with a kicker ability in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: owner.Zones.Graveyard.GetCards()
                        .Where(KickerAbilityDetector.HasKickerAbility)
                        .Cast<object>().ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Construct Murasa Sproutling's kicker <see cref="IAdditionalCost"/>
    /// ({1}{G}) for the supplied <paramref name="card"/> instance. The
    /// caller layers the returned cost onto the cast via
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter. Mirrors
    /// <see cref="BurstLightningFactory.BuildAdditionalCost"/>.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }

    /// <summary>
    /// Shared ETB resolution helper. CR 603.4 / 702.33b — short-circuits to
    /// a clean no-op when the creature wasn't kicked (intervening-if
    /// collapse). Otherwise reads <see cref="TriggeredAbility.ChosenTargets"/>;
    /// falls back to the first kicker card in the controller's graveyard;
    /// re-validates the pick is STILL a kicker card in the graveyard
    /// (CR 608.2b — illegal target → clean no-op); moves Graveyard → Hand.
    /// </summary>
    private static void ResolveReturnKickerCard(
        Creature sproutling,
        Player owner,
        TriggeredAbility? etb,
        ZoneService? zoneService)
    {
        // CR 603.4 / 702.33b — intervening-if "if it was kicked", sampled at
        // resolution. Not kicked → the trigger does nothing.
        if (!sproutling.WasKicked) return;

        // CR 110.2 — "your graveyard" is the controller's graveyard.
        var controller = sproutling.Controller ?? owner;

        ICard? picked = null;

        // 1) Honour the agent-set target if present (production path).
        if (etb != null && etb.ChosenTargets.Count > 0
            && etb.ChosenTargets[0].Count > 0
            && etb.ChosenTargets[0][0] is ICard chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first kicker card in controller's
        // graveyard (single-arg dispatcher / no-agent posture).
        picked ??= controller.Zones.Graveyard.GetCards()
            .FirstOrDefault(KickerAbilityDetector.HasKickerAbility);

        // No legal kicker card → clean no-op (CR 608.2b).
        if (picked == null) return;

        // CR 608.2b illegal-on-resolution check — target must still be a
        // kicker card in the controller's graveyard.
        if (picked.Zone != ZoneType.Graveyard) return;
        if (!controller.Zones.Graveyard.GetCards().Contains(picked)) return;
        if (!KickerAbilityDetector.HasKickerAbility(picked)) return;

        // Move Graveyard → Hand. ZoneService path publishes a CardMovedEvent
        // so any "leaves graveyard" triggers fire (CR 603.6a / CR 701.20).
        if (zoneService != null)
        {
            zoneService.MoveCard(picked, ZoneType.Graveyard, ZoneType.Hand, controller);
        }
        else
        {
            controller.Zones.Graveyard.RemoveCard(picked);
            controller.Zones.Hand.AddCard(picked);
            picked.SetZone(ZoneType.Hand);
        }
    }
}
