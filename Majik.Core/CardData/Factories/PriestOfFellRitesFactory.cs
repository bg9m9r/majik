using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Priest of Fell Rites (Modern Horizons 2, {W}{B}).
///
/// Creature — Human Cleric 2/1. Oracle text:
///   "When Priest of Fell Rites enters, you may return target creature card
///    with mana value 3 or less from your graveyard to the battlefield.
///    {2}{W}{B}, Exile Priest of Fell Rites from your graveyard: Return
///    target creature card from your graveyard to the battlefield. Activate
///    only as a sorcery."
///
/// ## Implemented (v1)
/// - 2/1 Human Cleric with mana cost {W}{B}.
/// - <b>ETB triggered ability (CR 603.1)</b>: When Priest of Fell Rites
///   enters, the controller may return target creature card with mana
///   value 3 or less from their graveyard to the battlefield. v1 picks
///   the first eligible creature card deterministically; the "you may"
///   defaults to taking the action when an eligible candidate exists.
///   Movement is funnelled through <see cref="ZoneService.MoveCard"/>
///   when a service is supplied so ETB triggers on the reanimated
///   permanent fire (CR 603.6a). When no ZoneService is supplied the
///   move falls back to raw zone manipulation suitable for shape tests.
/// - <b>Graveyard-activated unearth-style ability (CR 113.6 / 117.1a)</b>:
///   <c>{2}{W}{B}, Exile Priest of Fell Rites from your graveyard: Return
///    target creature card from your graveyard to the battlefield.</c>
///   The exile-self portion of the cost is folded into the resolution
///   effect (no <c>ExileSelfFromGraveyardCost</c> ICost shape exists at
///   v1; mirrors the Stoneforge pattern of expressing the second-half
///   cost work inside the Effect body). The mana cost is exposed as a
///   <see cref="ManaCostCost"/> on the activated ability for shape
///   inspection.
///   <para>
///   The engine does NOT presently gate activated abilities on source
///   zone — i.e. abilities are not zone-scoped. So this activated
///   ability is enumerable while Priest of Fell Rites is in any zone;
///   the resolution body checks that the Priest is in the graveyard
///   before paying the exile portion of the cost, so spurious
///   activations from battlefield/exile are no-op-shaped at v1. Tests
///   exercise the ability by enumerating activated abilities and
///   executing their effects, mirroring StoneforgeMystic / VexingBauble.
///   </para>
///
/// ## Deferred (v1 gaps)
/// - <b>Activate only as a sorcery</b>: CR 117.1a — the activation
///   restriction is not enforced. There is no per-activated-ability
///   sorcery-speed gate yet; only spell-casting goes through
///   <see cref="CastingRestrictions"/> via the
///   <see cref="SorcerySpeedRestrictionEffect"/>. Tests do not exercise
///   the timing gate.
/// - <b>"You may" prompt</b>: the ETB trigger autopicks the first
///   eligible creature card; declining and target-selection are
///   deferred to the agent-prompt MVP.
/// - <b>Zone-scoped activated abilities</b>: graveyard activations are
///   enumerable from any zone; a future engine pass should restrict
///   activation to the printed source zone (CR 113.6).
/// </summary>
public static class PriestOfFellRitesFactory
{
    /// <summary>
    /// Construct Priest of Fell Rites with no live ZoneService /
    /// TriggerManager wiring (the shape/dispatcher path). The ETB
    /// trigger is attached but not registered; the activated ability
    /// uses raw zone moves for the reanimation target — suitable for
    /// unit / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Priest of Fell Rites with optional runtime services.
    /// When <paramref name="zoneService"/> is supplied, both the ETB
    /// trigger and the activated ability route the graveyard →
    /// battlefield move through <see cref="ZoneService.MoveCard"/> so
    /// ETB triggers / replacements on the reanimated creature fire
    /// (CR 603.6a). When <paramref name="triggers"/> is supplied, the
    /// ETB trigger is registered so a <see cref="CardMovedEvent"/> to
    /// the battlefield places it on the stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Priest of Fell Rites",
            manaCost: "{W}{B}",
            power: 2,
            toughness: 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When Priest of Fell Rites enters, you may return target
        //    creature card with mana value 3 or less from your graveyard
        //    to the battlefield."
        // v1: deterministic — pick the first creature card (mana value
        // ≤ 3) in the controller's graveyard; "you may" defaults to
        // taking the action when an eligible candidate exists. See
        // class xmldoc for agent-prompt deferral.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Priest of Fell Rites: reanimate target creature card (mv ≤ 3)",
            () => ReanimatePick(owner, zoneService, maxManaValue: 3));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Activated ability — {2}{W}{B}, Exile this from graveyard:
        //   Return target creature card from your graveyard to the
        //   battlefield. Activate only as a sorcery.
        //
        // CR 113.6 / 117.1a — printed zone is graveyard. Mana cost is
        // expressed as a ManaCostCost on the ability for shape
        // inspection. The exile-self portion of the cost is performed
        // inside the resolution effect (no ExileSelfFromGraveyardCost
        // ICost shape exists at v1). The sorcery-speed restriction is
        // not enforced (see class xmldoc — "Deferred").
        //
        // Guard: only fire when the Priest is currently in its owner's
        // graveyard, so spurious activations from other zones are
        // no-op-shaped while engine zone-scoping is deferred.
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            "Priest of Fell Rites: exile from graveyard, reanimate target creature",
            () =>
            {
                // Cost half: exile Priest from graveyard. Skip if not
                // currently in graveyard — activation is illegal from
                // other zones (engine gating deferred; the guard keeps
                // shape tests honest).
                if (card.Zone != ZoneType.Graveyard) return;
                if (card.Owner == null) return;
                if (!ReferenceEquals(card.Owner, owner)) return;

                owner.Zones.Graveyard.RemoveCard(card);
                owner.Zones.Exile.AddCard(card);
                card.SetZone(ZoneType.Exile);

                // Effect half: reanimate first creature card in
                // controller's graveyard (no mana-value cap on the
                // activated ability — only the ETB has that rider).
                ReanimatePick(owner, zoneService, maxManaValue: null);
            });

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}{W}{B}") },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }

    /// <summary>
    /// Shared reanimation helper. Picks the first creature card in
    /// <paramref name="controller"/>'s graveyard whose mana value is
    /// less than or equal to <paramref name="maxManaValue"/> (when
    /// supplied) and moves it to the battlefield under the controller's
    /// control. Routes through <see cref="ZoneService.MoveCard"/> when
    /// available so ETB triggers on the reanimated permanent fire
    /// (CR 603.6a / PR #165); falls back to raw zone manipulation
    /// otherwise (shape-only path).
    /// </summary>
    private static void ReanimatePick(
        Player controller,
        ZoneService? zoneService,
        int? maxManaValue)
    {
        var pick = controller.Zones.Graveyard.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c =>
                maxManaValue is null
                    ? true
                    : c.ManaCostValue.TotalValue <= maxManaValue.Value);

        // CR 117.x — a "you may" / target-required effect with no valid
        // target resolves as a no-op.
        if (pick == null) return;

        if (zoneService != null)
        {
            zoneService.MoveCard(pick, ZoneType.Graveyard, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Graveyard.RemoveCard(pick);
            controller.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(controller);
        }
    }
}
