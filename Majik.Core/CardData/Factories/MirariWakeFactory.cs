using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mirari's Wake (Onslaught, {3}{G}{W}).
///
/// Enchantment. Oracle text:
///   "Creatures you control get +1/+1.
///    Whenever you tap a land for mana, add one mana of any type that
///    land produced."
///
/// ## Implemented
/// - Card identity: Enchantment, mana cost {3}{G}{W}, owner / controller
///   wiring. Dispatchable via <see cref="NamedCardFactory"/>.
/// - <b>Anthem (+1/+1) to all creatures you control</b>: registered as a
///   <see cref="ControllerCreatureAnthemEffect"/> static at Layer 7c
///   (CR 613.7c). Symmetric on the controller's side — every creature
///   they control gets +1/+1; opponents' creatures are unaffected.
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when Mirari's
///   Wake isn't on the battlefield so the bonus lifts on LTB. Multiple
///   copies stack additively.
/// - <b>"Whenever you tap a land for mana, add one mana of any type that
///   land produced."</b> (CR 605.1b — a triggered mana ability that
///   triggers on mana being produced and itself produces mana): a
///   <see cref="TriggeredAbility"/> subscribing to
///   <see cref="ManaAbilityActivatedEvent"/> (published by
///   <see cref="Majik.Core.Services.ManaAbilityActivator"/> after the
///   activator's pool is topped up — the same surface Utopia Sprawl /
///   Badgermole Cub consume). The condition matches when the activator is
///   THIS card's controller ("you", CR 109.5) AND the tapped source is a
///   <see cref="Land"/>. The effect re-adds the event's
///   <see cref="ManaAbilityActivatedEvent.ManaGenerated"/> to that
///   controller's mana pool via <see cref="Player.AddManaToPool"/> — i.e.
///   one additional mana of exactly the type(s) the land just produced
///   ("any type that land produced", CR 106.6), read straight off the
///   event so a {C}-producing land or a multi-color land doubles correctly.
///
/// ## Routed production wiring
///
/// Mirari's Wake exposes a <c>Create(Player, ContinuousEffectsService)</c>
/// overload, so the production routed (instance-swap) build dispatches to it
/// via <c>NamedCardFactory.CreateGeneratedWithEffects</c> (the same path that
/// wires the anthem). That overload also resolves the live per-game
/// <see cref="TriggerManager"/> from
/// <see cref="TriggerManagerRegistry"/> (the ambient manager installed at
/// game start in <c>GameDriver.RunGameAsync</c>) and registers the mana
/// trigger with it — same posture as <see cref="UtopiaSprawlFactory"/> — so
/// the doubling actually fires in a real match. The trigger only matches
/// while Mirari's Wake is on the battlefield (<see cref="TriggeredAbility"/>'s
/// <c>ActiveZones</c> gate, CR 603.1), so registering at deck-build time is
/// harmless until it resolves.
///
/// Both clauses are fully wired through the production build path: the
/// source-gen dispatcher routes the effects-aware
/// <c>NamedCardFactory.Create(name, owner, effects)</c> (the path
/// <c>GameFacade.BuildDeckCard</c> uses) to the
/// <c>Create(Player, ContinuousEffectsService)</c> overload below, which
/// registers BOTH the anthem and the mana-doubling trigger. The
/// "mana-bonus trigger" gap is therefore CLOSED — see
/// <c>MirariWakeTests.TappingYourLandForMana_AddsAdditionalManaOfThatType</c>
/// and the prod-path guard
/// <c>MirariWakeTests.MirarisWake_ProdPath_BindsManaDoublingTrigger</c>.
///
/// ## Accepted v1 simplification (shared, not specific to this card)
/// - <b>LTB unregister</b>: the anthem stays on the layers service across
///   zone changes; <see cref="ContinuousEffect.IsActive"/> gates it off
///   when Mirari's Wake leaves the battlefield. Same posture as Goblin
///   Chieftain / Engineered Plague — a generic continuous-effect lifecycle
///   simplification, not a mana-trigger gap.
/// </summary>
[CardName("Mirari's Wake")]
public static class MirariWakeFactory
{
    public const string CardName = "Mirari's Wake";
    public const string Cost = "{3}{G}{W}";

    /// <summary>
    /// Construct Mirari's Wake without a live continuous-effects service.
    /// Suitable for shape / dispatcher tests; the +1/+1 anthem requires a
    /// live <see cref="ContinuousEffectsService"/> to take effect. The
    /// mana-doubling trigger is still attached, and registered with the
    /// ambient per-game <see cref="TriggerManager"/> when one is present.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: TriggerManagerRegistry.Get());

    /// <summary>
    /// Convenience overload for end-to-end tests that supply only a live
    /// <see cref="TriggerManager"/> (no layers service) — registers the
    /// mana-doubling trigger so it surfaces as pending.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
        => Create(owner, continuousEffects: null, triggers: triggers);

    /// <summary>
    /// Construct a fully-wired Mirari's Wake. When
    /// <paramref name="continuousEffects"/> is supplied, the +1/+1 anthem
    /// against the controller's creatures is registered against the layers
    /// service. This is the effects-aware overload the production routed
    /// build dispatches to; it resolves the ambient per-game
    /// <see cref="TriggerManager"/> from <see cref="TriggerManagerRegistry"/>
    /// so the mana-doubling trigger fires in a real match.
    /// </summary>
    public static Enchantment Create(Player owner, ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects, triggers: TriggerManagerRegistry.Get());

    /// <summary>
    /// Construct a fully-wired Mirari's Wake. The +1/+1 anthem is registered
    /// against <paramref name="continuousEffects"/> (when non-null); the
    /// mana-doubling triggered ability is always attached to the card's
    /// <see cref="Card.Abilities"/> collection and, when
    /// <paramref name="triggers"/> is supplied, also registered with the
    /// <see cref="TriggerManager"/> so it surfaces as pending end-to-end.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the anthem
    /// against. May be null — no live +1/+1 bonus.</param>
    /// <param name="triggers">Optional live trigger manager for end-to-end
    /// mana-doubling firing.</param>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.7c — "Creatures you control get +1/+1." Layer 7c P/T
            // modification scoped to the source's controller, with no
            // subtype filter (same effect type used by Heartless
            // Summoning's -1/-1 penalty).
            continuousEffects.Register(new ControllerCreatureAnthemEffect(
                source: card, power: 1, toughness: 1));
        }

        // --------------------------------------------------------------------
        // "Whenever you tap a land for mana, add one mana of any type that
        // land produced." CR 605.1b — a triggered mana ability (triggers on
        // mana being produced; itself produces mana). It subscribes to the
        // ManaAbilityActivatedEvent published by ManaAbilityActivator after the
        // activator's pool is topped up (same surface Utopia Sprawl / Badgermole
        // Cub consume). CR 109.5 / 603.2 — "you" is THIS card's controller; the
        // trigger only fires when the controller is the player who tapped a
        // land.
        // --------------------------------------------------------------------
        ManaCost? pendingBonus = null;
        Player? pendingController = null;

        var tapCondition = new EventTriggerCondition<ManaAbilityActivatedEvent>((e, _) =>
        {
            // "you tap" — the activator must be the Wake's current controller.
            var you = card.Controller ?? owner;
            if (!ReferenceEquals(e.Player, you)) return false;
            // "a land for mana" — the tapped source must be a Land.
            if (e.Source is not Land) return false;
            // "add one mana of any type that land produced" (CR 106.6) — re-add
            // exactly what the land's mana ability produced, read off the event.
            pendingBonus = e.ManaGenerated;
            pendingController = e.Player;
            return true;
        });

        var addManaEffect = new Effect(
            "Mirari's Wake — add one additional mana of the type the land produced",
            () =>
            {
                var controller = pendingController;
                var bonus = pendingBonus;
                pendingController = null;
                pendingBonus = null;
                if (controller != null && bonus != null)
                {
                    controller.AddManaToPool(bonus);
                }
            });

        var tapTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: tapCondition,
            effects: new IEffect[] { addManaEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(tapTrigger);
        triggers?.RegisterTriggeredAbility(tapTrigger);

        return card;
    }
}
