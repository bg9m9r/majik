using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cadaver Imp (Planeshift, {1}{B}{B}).
///
/// Creature — Imp 1/1. Oracle text:
///   "Flying
///    When this creature enters, you may return target creature card from
///    your graveyard to your hand."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Imp, mana cost {1}{B}{B}, mana value 3.
/// - <b>Flying</b> (CR 702.9) as a <see cref="KeywordAbility"/> marker.
/// - Single ETB <see cref="TriggeredAbility"/> (CR 603.6a) wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with a bespoke 1..1
///   <see cref="TargetRequest"/> exposing only <see cref="Creature"/> cards
///   in the controller's graveyard as candidates (unlike Eternal Witness
///   which allows any card type).
/// - Resolution body reads <see cref="TriggeredAbility.ChosenTargets"/>;
///   falls back to the first creature card in controller's graveyard when
///   no target is set (dispatcher-path posture, mirrors Eternal Witness /
///   Tasigur first-candidate fallback). Validates the chosen card is still
///   in the controller's graveyard at resolution (CR 608.2b — clean no-op
///   on fizzle); moves Graveyard → Hand via <see cref="ZoneService.MoveCard"/>
///   when supplied (so <see cref="CardMovedEvent"/> fires), otherwise direct
///   zone mutation.
/// - "You may" auto-accepted at v1 — same posture as Tireless Tracker /
///   Snapcaster Mage.
///
/// ## Posture
/// Single-arg <see cref="Create(Player)"/> path attaches Flying + ETB shape
/// WITHOUT live bus wiring (suitable for shape / dispatcher tests). The
/// (owner, zoneService, triggers) overload registers the ETB with the
/// supplied <see cref="TriggerManager"/> for bus-driven firing.
/// </summary>
[CardName("Cadaver Imp")]
public static class CadaverImpFactory
{
    public const string CardName = "Cadaver Imp";
    public const string PrintedManaCost = "{1}{B}{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Cadaver Imp with no runtime wiring. Card identity +
    /// ability shape only; ETB trigger is attached but NOT registered
    /// with a <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Cadaver Imp with optional runtime wiring. When
    /// <paramref name="triggers"/> is supplied the ETB trigger is
    /// registered for bus-driven firing; when <paramref name="zoneService"/>
    /// is supplied the Graveyard → Hand move routes through
    /// <see cref="ZoneService.MoveCard"/> so downstream zone-change triggers
    /// fire (CR 603.6a / CR 701.20).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Imp });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Marker keyword; combat code reads it.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, you may return target creature card
        //    from your graveyard to your hand."
        //
        // Bespoke 1..1 TargetRequest mirrors Animate Dead's graveyard-card
        // shape, filtered to Creature cards only (unlike Eternal Witness
        // which returns ANY card type). CR 700.6 — "creature card" means
        // a card with the Creature type in its printed card type line.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: return target creature card from your graveyard to your hand",
            () => ResolveReturnCreatureToHand(card, owner, etb, zoneService));

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
                    Description: "target creature card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: owner.Zones.Graveyard.GetCards()
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Resolution helper for the ETB return-creature effect. Reads the
    /// trigger's <see cref="TriggeredAbility.ChosenTargets"/>; falls back
    /// to the first creature card in the controller's graveyard when no
    /// target was set (deterministic single-arg dispatcher posture).
    /// Validates the chosen card is still in the graveyard at resolution
    /// (CR 608.2b — illegal target → clean no-op). Moves Graveyard → Hand
    /// via <see cref="ZoneService.MoveCard"/> when supplied; otherwise
    /// direct zone mutation.
    /// </summary>
    private static void ResolveReturnCreatureToHand(
        Creature imp,
        Player owner,
        TriggeredAbility? etb,
        ZoneService? zoneService)
    {
        // CR 110.2 — "your graveyard" is the controller's graveyard at
        // resolution (handles control-change edge cases).
        var controller = imp.Controller ?? owner;

        ICard? picked = null;

        // 1) Honour the agent-set target if present (production path).
        if (etb != null && etb.ChosenTargets.Count > 0
            && etb.ChosenTargets[0].Count > 0
            && etb.ChosenTargets[0][0] is ICard chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first creature card in controller's
        // graveyard (single-arg dispatcher path / no-agent posture).
        picked ??= controller.Zones.Graveyard.GetCards()
            .OfType<Creature>()
            .FirstOrDefault();

        // Empty graveyard / no creature cards → clean no-op (CR 608.2b).
        if (picked == null) return;

        // CR 608.2b illegal-on-resolution check — target must still be in
        // the controller's graveyard. Cards that left between trigger
        // creation and resolution cause the return to fizzle.
        if (picked.Zone != ZoneType.Graveyard) return;
        if (!controller.Zones.Graveyard.GetCards().Contains(picked)) return;

        // Move Graveyard → Hand. ZoneService path publishes CardMovedEvent
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
