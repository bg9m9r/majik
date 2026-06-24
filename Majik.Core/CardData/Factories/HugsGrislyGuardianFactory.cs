using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hugs, Grisly Guardian (Bloomburrow Commander,
/// <c>{X}{R}{R}{G}{G}</c>). Legendary Creature — Badger Warrior 5/5.
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "Trample
///    When Hugs enters, exile the top X cards of your library. Until the end
///    of your next turn, you may play those cards.
///    You may play an additional land on each of your turns."
///
/// The base shape (name, Legendary supertype, Creature, Badger + Warrior
/// subtypes, <c>{X}{R}{R}{G}{G}</c>, 5/5, Trample) is materialised from the
/// embedded JSON definition (<c>hugs-grisly-guardian.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (Trample is an intrinsic
/// <see cref="KeywordAbility"/> stamped by the JSON <c>keywords</c> array). The
/// ETB triggered ability and the extra-land static are layered on here — the
/// JSON <c>AbilityDefinition</c> schema doesn't express a variable-X impulse-exile
/// ETB, so it lives in the factory (same posture as
/// <see cref="TersaLightshatterFactory"/> / <see cref="HydroidKrasisFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Trample (CR 702.19)</b> — intrinsic <see cref="KeywordAbility"/> marker
///   stamped by the JSON <c>keywords</c> array, observed by the combat surface.
///
/// - <b>ETB triggered ability (CR 603.1 / 603.6a)</b> — "When Hugs enters,
///   exile the top X cards of your library. Until the end of your next turn,
///   you may play those cards." X is the value paid for the <c>{X}</c> in Hugs's
///   own mana cost, captured at cast time on <see cref="Card.PendingCastX"/>
///   (stamped by <see cref="Majik.Core.Game.SpellCastFlow"/> right after the
///   dispatcher's <c>ChooseXAsync</c>) — read on resolution, same seam as
///   <see cref="HydroidKrasisFactory"/>. On resolve it exiles the top X cards of
///   the controller's library to their Exile zone (CR 701.20) and stamps the
///   reusable "you may play those cards until end of your next turn" permission
///   (<see cref="ExilePlayPermission.GrantUntil"/> with
///   <see cref="ExilePlayExpiry.EndOfYourNextTurn"/>) on each — which covers BOTH
///   the spell-cast half and the exiled-land land-play half (CR 305.2 / 601.1).
///   A single shared revocation (<see cref="ExilePlayPermission.ScheduleRevocation"/>)
///   clears the whole batch on the controller's SECOND Cleanup (the first
///   belongs to the turn Hugs entered; the second is the controller's NEXT turn
///   — CR 514.2). X &lt;= 0 or an empty library is a clean no-op.
///
/// - <b>"Play an additional land on each of your turns" (CR 305.2 / 720)</b> —
///   a controller-scoped, battlefield-gated static raising the controller's
///   per-turn land-play cap by 1 while Hugs is on the battlefield. Modeled as
///   <see cref="Permanent.AdditionalLandPlaysGranted"/> = 1, summed live over
///   the controller's permanents by <see cref="Majik.Core.Game.LandDropTracker"/>
///   — identical posture to <see cref="AzusaLostButSeekingFactory"/> (Azusa is
///   +2; Hugs is +1). The bonus appears the instant Hugs enters, resets correctly
///   each turn (CR 505.5b), and stacks additively with other sources.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — canonical build (no bus). The ETB trigger is
///   attached for shape observability; the exile play-permission persists until
///   cleared by hand (test path) because no Cleanup subscription is scheduled.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully wired. The
///   trigger registers with <paramref name="triggers"/>; the play permission
///   clears at the controller's second Cleanup (CR 514.2) via the supplied bus.
///
/// ## Deferred (v1 gaps)
/// - <b>Empty / shallow library mid-exile</b>: the ETB stops exiling when the
///   library runs out (CR 701.20 imposes no "tried to draw from empty library"
///   flag for an exile move) — it simply stamps fewer grants, mirroring
///   <see cref="LightUpTheStageFactory"/>.
/// </summary>
[CardName("Hugs, Grisly Guardian")]
public static class HugsGrislyGuardianFactory
{
    public const string CardName = "Hugs, Grisly Guardian";
    public const string Slug = "hugs-grisly-guardian";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>CR 720 — "play an additional land on each of your turns" = +1.</summary>
    public const int AdditionalLandPlays = 1;

    /// <summary>
    /// Canonical build with no live wiring (the shape / dispatcher path). This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to. The ETB
    /// trigger is attached but not registered; its exile play-permission will
    /// not auto-clear without an event bus.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Hugs with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB triggered ability is
    /// registered. When <paramref name="eventBus"/> is supplied, the exile
    /// play-permission clears at the controller's SECOND Cleanup step (end of
    /// the controller's next turn — CR 514.2).
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Badger + Warrior, {X}{R}{R}{G}{G}, 5/5, Trample). Trample is
        // an intrinsic KeywordAbility stamped by the JSON keywords array.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 305.2 / 720 — "You may play an additional land on each of your
        // turns." Battlefield-gated, controller-scoped, summed live by
        // LandDropTracker.AdditionalLandPlaysFromBattlefield. Same modeling as
        // Azusa (Azusa grants +2; Hugs grants +1).
        card.AdditionalLandPlaysGranted = AdditionalLandPlays;

        AddEtbTrigger(card, owner, eventBus, triggers);

        return card;
    }

    // -----------------------------------------------------------------------
    // ETB trigger — "When Hugs enters, exile the top X cards of your library.
    // Until the end of your next turn, you may play those cards."
    // (CR 603.1 / 603.6a.)
    // -----------------------------------------------------------------------
    private static void AddEtbTrigger(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        var etbEffect = new Effect(
            $"{CardName}: exile top X library cards; play them until end of your next turn",
            () => ResolveEtb(card, owner, eventBus));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);
    }

    private static void ResolveEtb(Creature card, Player owner, IEventBus? eventBus)
    {
        var controller = card.Controller ?? owner;

        // X = the value paid for {X} in Hugs's own mana cost, snapshotted at
        // cast time on PendingCastX (Hydroid Krasis seam). Null/0 when not cast
        // via the X-prompt path → exile nothing (clean no-op).
        var x = card.PendingCastX ?? 0;
        if (x <= 0) return;

        // "exile the top X cards of your library" — CR 701.20.
        var stamped = new List<Card>(x);
        for (var i = 0; i < x; i++)
        {
            var top = controller.Zones.Library.GetCards().FirstOrDefault();
            if (top is not Card concrete) break; // library underflow — fewer grants

            controller.Zones.Library.RemoveCard(concrete);
            controller.Zones.Exile.AddCard(concrete);
            concrete.SetZone(ZoneType.Exile);

            // "Until the end of your next turn, you may play those cards."
            // CR 118.9 / 305.2 / 601.1 — the reusable permission authorises both
            // the spell-cast half and the exiled-land land-play half. Stamp only
            // (bus null) so one shared subscription expires the whole batch.
            ExilePlayPermission.GrantUntil(
                concrete, controller, concrete.ManaCostValue,
                ExilePlayExpiry.EndOfYourNextTurn, eventBus: null);
            stamped.Add(concrete);
        }

        if (stamped.Count == 0) return;

        // CR 514.2 — "until end of your next turn": one shared revocation on the
        // controller's SECOND Cleanup (the first belongs to the turn Hugs entered).
        ExilePlayPermission.ScheduleRevocation(
            controller, ExilePlayExpiry.EndOfYourNextTurn, eventBus,
            () => { foreach (var s in stamped) { s.ClearRuntimeExileCast(); s.ClearRuntimeExileLandPlay(); } });
    }
}
