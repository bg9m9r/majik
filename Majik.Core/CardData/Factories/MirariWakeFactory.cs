using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mirari's Wake (Onslaught, {3}{G}{W}).
///
/// Enchantment. Oracle text:
///   "Creatures you control get +1/+1.
///    Whenever you tap a land for mana, add one mana of any type that
///    land produced."
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, mana cost {3}{G}{W}, owner / controller
///   wiring. Dispatchable via <see cref="NamedCardFactory"/>.
/// - <b>Anthem (+1/+1) to all creatures you control</b>: registered as a
///   <see cref="ControllerCreatureAnthemEffect"/> static at Layer 7c
///   (CR 613.7c). Symmetric on the controller's side — every creature
///   they control gets +1/+1; opponents' creatures are unaffected.
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when Mirari's
///   Wake isn't on the battlefield so the bonus lifts on LTB. Multiple
///   copies stack additively.
///
/// ## Deferred (v1 gaps)
/// - <b>Mana-tap doubling ("Whenever you tap a land for mana, add one
///   mana of any type that land produced.")</b>: no Mana Reflection /
///   Caged Sun-style "add a copy of the produced mana" primitive exists
///   yet. The required surface is a triggered ability subscribing to
///   <see cref="Majik.Core.Events.ManaAbilityActivatedEvent"/> (already
///   published by <see cref="Majik.Core.Services.ManaAbilityActivator"/>)
///   that, when the activator is the controller AND the source is a Land,
///   adds the event's <c>ManaGenerated</c> to the controller's mana pool.
///   Once that lands (same primitive Mana Reflection / Heartbeat of Spring
///   need), this factory can register the trigger alongside the anthem.
/// - <b>LTB unregister</b>: the anthem stays on the layers service across
///   zone changes; <see cref="ContinuousEffect.IsActive"/> gates it off
///   when Mirari's Wake leaves the battlefield. Same posture as Goblin
///   Chieftain / Engineered Plague.
/// </summary>
[CardName("Mirari's Wake")]
public static class MirariWakeFactory
{
    public const string CardName = "Mirari's Wake";
    public const string Cost = "{3}{G}{W}";

    /// <summary>
    /// Construct Mirari's Wake without live continuous-effects wiring.
    /// Suitable for shape / dispatcher tests; the +1/+1 anthem requires
    /// a live <see cref="ContinuousEffectsService"/> to take effect.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Mirari's Wake. When
    /// <paramref name="continuousEffects"/> is supplied, the +1/+1 anthem
    /// against the controller's creatures is registered against the layers
    /// service. The mana-tap doubling clause is deferred (see class
    /// xmldoc).
    /// </summary>
    public static Enchantment Create(Player owner, ContinuousEffectsService? continuousEffects)
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

        return card;
    }
}
