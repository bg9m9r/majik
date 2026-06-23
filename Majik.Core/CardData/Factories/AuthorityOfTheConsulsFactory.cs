using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Authority of the Consuls (Kaladesh — Enchantment {W}).
///
/// Oracle text (verified against Scryfall):
///   "Creatures your opponents control enter tapped.
///    Whenever a creature an opponent controls enters, you gain 1 life."
///
/// The card's base shape (name, Enchantment, {W}) is materialised from the
/// embedded JSON definition (<c>authority-of-the-consuls.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (the opponent enters-tapped static + the opponent-creature-ETB lifegain
/// trigger) are layered on top here — the JSON <c>AbilityDefinition</c> schema
/// doesn't express replacement-effect statics, and its
/// <c>whenever_another_creature_enters</c> trigger variant carries no
/// opponent-only controller scope (only any-player / you-control), so they
/// live in the factory (same posture as
/// <see cref="ThaliaHereticCatharFactory"/>).
///
/// ## Implemented
///
/// ### "Creatures your opponents control enter tapped." (CR 614.1c)
/// A static ability generating a one-sided ETB replacement. Wired via
/// <see cref="AuthorityOfTheConsulsEntersTappedEffect"/>: while Authority is on
/// the battlefield, an <see cref="IReplacementEffect{ZoneMoveIntent}"/> sets
/// <see cref="ZoneMoveIntent.EntersTapped"/> = true for any battlefield-entry
/// intent carrying a creature whose controller is an opponent of Authority's
/// controller (CR 109.5). The lifecycle unregisters when Authority leaves the
/// battlefield, so the effect lifts automatically. Same global-replacement +
/// ETB/LTB-lifecycle shape as Thalia, but restricted to creatures only
/// (Authority does not affect lands).
///
/// ### "Whenever a creature an opponent controls enters, you gain 1 life."
/// (CR 603.6e / CR 119.3 / CR 109.5)
/// A triggered ability over <see cref="CardMovedEvent"/>: fires when a creature
/// entering the battlefield is controlled by a player other than Authority's
/// controller (every other player is an opponent — CR 102.2). On resolution
/// Authority's controller gains 1 life via <see cref="Player.GainLife"/>.
/// Controller is resolved live (<c>card.Controller</c>) at fire time so a
/// control change carries the trigger (CR 109.5 — same posture as the
/// declarative variants). Hand-rolled because the declarative
/// <c>whenever_another_creature_enters</c> trigger has no opponent-only scope.
///
/// ## Deferred
/// - <b>Ordering with other ETB-tapped replacements</b>: when multiple
///   replacements apply to the same entry the affected player should choose
///   the order (CR 616.1). <see cref="ReplacementBus"/> applies in
///   registration order for now; the observable result (the permanent enters
///   tapped) is unchanged for the enters-tapped case.
/// </summary>
[CardName("Authority of the Consuls")]
public static class AuthorityOfTheConsulsFactory
{
    public const string CardName = "Authority of the Consuls";
    public const string Slug = "authority-of-the-consuls";
    public const int LifeGainAmount = 1;

    /// <summary>
    /// Construct Authority with no live wiring. Suitable for card-shape /
    /// dispatcher tests — the lifegain trigger is attached to the card shape
    /// (so dispatcher tests see it) but the enters-tapped replacement is not
    /// registered and the trigger is not bus-driven. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, replacementBus: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Authority of the Consuls with the opponent
    /// enters-tapped replacement lifecycle attached against
    /// <paramref name="replacementBus"/> / <paramref name="eventBus"/>, and the
    /// opponent-creature-ETB lifegain trigger registered with
    /// <paramref name="triggers"/> when supplied.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to register
    /// the enters-tapped replacement on. May be null — the replacement simply
    /// won't activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be null — the
    /// lifecycle will still sync once on Attach.</param>
    /// <param name="triggers">The <see cref="TriggerManager"/> to register the
    /// lifegain trigger with. May be null — the trigger is still attached to the
    /// card shape but won't fire from the bus.</param>
    public static Enchantment Create(
        Player owner,
        ReplacementBus? replacementBus,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Enchantment,
        // {W}). The JSON carries no abilities — the static + trigger are
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Enchantment card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Enchantment but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // "Whenever a creature an opponent controls enters, you gain 1 life."
        // CR 603.6e — triggered ability over CardMovedEvent. Predicate gates
        // on (a) destination = Battlefield, (b) the moved card is a Creature,
        // (c) its controller is a player other than Authority's controller
        // (an opponent — CR 102.2 / 109.5). Controller resolved live so a
        // control change carries the trigger.
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;

            var authorityController = card.Controller ?? card.Owner;
            if (authorityController is null) return false;

            var enteringController = e.Card.Controller ?? e.Card.Owner;
            if (enteringController is null) return false;

            // CR 109.5 / CR 102.2 — "an opponent controls": controller is a
            // player other than Authority's controller. Authority's own
            // creatures entering do not fire it.
            return !ReferenceEquals(enteringController, authorityController);
        });

        var gainLifeEffect = new Effect(
            $"{CardName}: you gain {LifeGainAmount} life",
            // CR 119.3 — direct life gain routed through Player.GainLife so
            // lifegain observers (Ajani's Pridemate / Heliod) see it. Resolve
            // the controller live in case Authority changed hands.
            () => (card.Controller ?? owner).GainLife(LifeGainAmount));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { gainLifeEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // ----------------------------------------------------------------
        // "Creatures your opponents control enter tapped." (CR 614.1c)
        // Registered as a one-sided global ETB replacement while Authority is
        // on the battlefield.
        // ----------------------------------------------------------------
        if (replacementBus != null)
        {
            var lifecycle = new AuthorityOfTheConsulsEntersTappedEffect(
                source: card,
                replacementBus: replacementBus,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }
}
