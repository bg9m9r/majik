using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Uro, Titan of Nature's Wrath (Theros Beyond Death,
/// {1}{G}{U}).
///
/// ## Card text
/// "When Uro, Titan of Nature's Wrath enters, sacrifice it unless it
///  escaped.
///  Whenever Uro enters or attacks, you gain 3 life and draw a card. Then
///  you may put a land card from your hand onto the battlefield.
///  Escape—{G}{G}{U}{U}, Exile five other cards from your graveyard."
///
/// Legendary Creature — Elder Giant 6/6.
///
/// ## Implemented (v1)
/// - 6/6 Legendary Creature — Giant, mana cost {1}{G}{U}.
///   ("Elder" creature subtype is not yet in <see cref="CardSubtype"/> —
///    Giant is wired; Elder is deferred — see gaps below.)
/// - <b>Self-sacrifice ETB trigger (CR 603.1 / CR 701.16 / CR 702.138b)</b>:
///   When Uro enters, sacrifice it unless it escaped. The trigger reads
///   the cast-time <see cref="Card.WasCastForEscape"/> stamp set by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> when the cast used
///   <see cref="Majik.Core.Costs.EscapeAlternativeCost"/>; hardcast Uro
///   gets sacrificed, escaped Uro stays on the battlefield. Sacrifice
///   routes battlefield → owner's graveyard via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> when still on the
///   battlefield (skips if a previous effect already moved it).
/// - <b>ETB + attack triggered ability (CR 603.1 + CR 508.1f)</b>: On each
///   trigger, the controller gains 3 life (CR 119.3), draws a card
///   (CR 121.1 — top-of-library; flags <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
///   when the library is empty per CR 704.5b), then optionally puts a land
///   card from hand onto the battlefield (CR 113.6c — alt-zone "play").
///   v1 deterministic first-land-in-hand pick (auto-accepts the "you may"
///   when a candidate exists — same shape as Aether Vial / Sneak Attack /
///   Through the Breach). Land move routes through
///   <see cref="ZoneService.MoveCard"/> when supplied so ETB-on-land
///   triggers fire (CR 603.6a); falls back to raw zone moves otherwise.
///
/// - <b>Escape (CR 702.138) — wired via
///   <see cref="EscapeAlternativeCost"/></b>: cast-from-graveyard alt
///   cost with the "exile five other cards from your graveyard" rider.
///   <see cref="BuildAlternativeCost"/> returns the bound alt-cost
///   instance. The "sacrifice it unless it escaped" rider on the ETB
///   trigger now consults <see cref="Card.WasCastForEscape"/>: a
///   hardcast Uro still gets sacrificed; an escaped Uro stays on the
///   battlefield.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompts</b>: each trigger's land-play clause is
///   optional in the oracle text. v1 always plays the first land in
///   hand when one exists; a first-class yes/no agent prompt is deferred
///   (same gap as Sun Titan / Primeval Titan / Stoneforge Mystic).
/// - <b>Elder subtype</b>: the printed creature type is "Elder Giant".
///   <see cref="CardSubtype"/> only carries Giant; Elder is not yet in
///   the enum, mirroring the same gap for other "Elder X" creatures
///   (Elder Dragons etc).
/// </summary>
[CardName("Uro, Titan of Nature's Wrath")]
public static class UroTitanFactory
{
    public const string CardName = "Uro, Titan of Nature's Wrath";
    public const string PrintedManaCost = "{1}{G}{U}";

    /// <summary>CR 702.138 — printed Escape mana cost: {G}{G}{U}{U}.</summary>
    public const string EscapeManaCost = "{G}{G}{U}{U}";

    /// <summary>CR 702.138a — Escape rider: exile five OTHER cards from
    /// your graveyard.</summary>
    public const int EscapeExileCount = 5;

    /// <summary>
    /// CR 702.138 — Uro's printed Escape alt-cost ({G}{G}{U}{U}, exile
    /// five OTHER graveyard cards). The cast pipeline picks this up
    /// from <see cref="Players.Agents.EscapeAltCostProbe"/> / direct
    /// <c>SpellCaster</c> calls; the ETB sacrifice trigger reads the
    /// resulting <see cref="Card.WasCastForEscape"/> stamp to skip
    /// the sacrifice when Uro escaped.
    /// </summary>
    public static EscapeAlternativeCost BuildAlternativeCost() =>
        new(ValueObjects.ManaCost.Parse(EscapeManaCost), EscapeExileCount);

    /// <summary>
    /// Construct Uro with no live ZoneService / TriggerManager wiring (the
    /// shape/dispatcher path). Triggers are attached but not registered;
    /// land-from-hand moves use raw zone manipulation suitable for shape
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Uro with optional runtime services. When
    /// <paramref name="zoneService"/> is supplied, the land-play route
    /// uses <see cref="ZoneService.MoveCard"/> so ETB triggers on the
    /// played land fire (CR 603.6a). When <paramref name="triggers"/> is
    /// supplied, all three triggers are registered so dispatched events
    /// place them on the stack automatically.
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
            power: 6,
            toughness: 6,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Giant });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Self-sacrifice ETB trigger — CR 603.1 / CR 701.16 / CR 702.138b.
        //   "When Uro enters, sacrifice it unless it escaped."
        // Reads the cast-time escape stamp off the card (set by
        // SpellCastFlow when the cast used an EscapeAlternativeCost).
        // Hardcast Uro lacks the stamp → always sacrificed; escaped
        // Uro skips the sacrifice and stays on the battlefield.
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: sacrifice unless escaped",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                // CR 702.138b — "escaped" gate. Hardcast Uro is sacrificed;
                // escaped Uro stays.
                if (card.WasCastForEscape) return;
                // CR 701.16 — sacrifice bypasses Indestructible /
                // regeneration (CR 702.12b).
                OracleSpellBinder.MoveToGraveyard(card, Majik.Core.Zones.ZoneMoveReason.Sacrifice);
            });

        var sacTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { sacEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(sacTrigger);
        triggers?.RegisterTriggeredAbility(sacTrigger);

        // ----------------------------------------------------------------
        // Shared ETB/attack effect — gain 3, draw 1, may play land from hand.
        // CR 119.3 (life gain), CR 121.1 (draw), CR 113.6c (land-play
        // alt-zone). Sequenced exactly as printed.
        // ----------------------------------------------------------------
        IEffect BuildGainDrawPlayLandEffect(string label) =>
            new Effect(label, () => GainDrawPlayLand(owner, zoneService));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "Whenever Uro enters …, you gain 3 life and draw a card. Then
        //    you may put a land card from your hand onto the battlefield."
        // ----------------------------------------------------------------
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { BuildGainDrawPlayLandEffect($"{CardName}: ETB +3 life, draw 1, may play land from hand") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Attack triggered ability — CR 508.1f. Same body as ETB.
        // ----------------------------------------------------------------
        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new[] { BuildGainDrawPlayLandEffect($"{CardName}: attack +3 life, draw 1, may play land from hand") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Shared body for the ETB and attack triggers. Gains 3 life
    /// (CR 119.3), draws one card top-of-library (CR 121.1 — marks
    /// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> on empty per
    /// CR 704.5b), then puts the first land card from hand onto the
    /// battlefield (v1 deterministic auto-accept "you may"). Land move
    /// routes through <paramref name="zoneService"/> when supplied so
    /// ETB triggers / replacements on the played land fire (CR 603.6a);
    /// otherwise raw zone manipulation (test/shape path).
    /// </summary>
    private static void GainDrawPlayLand(Player controller, ZoneService? zoneService)
    {
        // CR 119.3 — "gain 3 life".
        controller.GainLife(3);

        // CR 121.1 — "draw a card". Empty library is the CR 704.5b
        // pending-loss flag, not an exception.
        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            controller.MarkTriedToDrawFromEmptyLibrary();
        }
        else
        {
            controller.Zones.Library.RemoveCard(top);
            controller.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }

        // CR 113.6c — "Then you may put a land card from your hand onto
        // the battlefield." v1 picks the first land in hand
        // deterministically and auto-accepts the "may" when a candidate
        // exists. No-land-in-hand resolves as a clean no-op.
        var land = controller.Zones.Hand.GetCards()
            .FirstOrDefault(c => c.HasType(CardType.Land));
        if (land == null) return;

        // CR 603.6a — prefer the caller-supplied zoneService; fall back
        // to ZoneServiceRegistry so the dispatcher path also publishes
        // CardMovedEvent for the played land (drives bounce-land ETB
        // bounce + Amulet of Vigor untap triggers).
        var effectiveZones = zoneService
            ?? Majik.Core.Services.ZoneServiceRegistry.Get(controller);
        if (effectiveZones != null)
        {
            effectiveZones.MoveCard(land, ZoneType.Hand, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Hand.RemoveCard(land);
            controller.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
            land.SetController(controller);
        }
    }
}
