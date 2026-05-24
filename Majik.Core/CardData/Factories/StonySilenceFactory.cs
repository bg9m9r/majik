using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stony Silence (Return to Ravnica, {1}{W}).
///
/// Enchantment — {1}{W}.
/// Oracle text:
///   "Activated abilities of artifacts can't be activated unless they're
///    mana abilities."
///
/// ## Implemented (v1)
/// - Enchantment with mana cost {1}{W} and correct identity / owner /
///   controller.
/// - <b>Printed static</b> (CR 602.5c / 605): global artifact activated-
///   ability suppression. Wired via <see cref="StonySilenceStaticEffect"/>;
///   as Stony Silence enters the battlefield, a predicate restriction is
///   registered into <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions"/>
///   matching any activated ability whose source is an on-battlefield
///   artifact. <see cref="Majik.Core.Rules.ActionValidator"/> consults the
///   registry during activation validation. Both players' artifacts are
///   gated — Stony Silence is symmetric, unlike
///   <see cref="KarnTheGreatCreatorFactory"/>'s opponent-only variant.
/// - <b>CR 605 mana-ability exemption</b>: the registry short-circuits on
///   <see cref="Majik.Core.Abilities.IManaAbility"/>; mana abilities also
///   route through <see cref="Majik.Core.Services.ManaAbilityActivator"/>
///   which bypasses <see cref="Majik.Core.Rules.ActionValidator"/>, so
///   {T}: Add {C} on a Mox / Sol Ring / etc. still works under Stony
///   Silence.
/// </summary>
[CardName("Stony Silence")]
public static class StonySilenceFactory
{
    public const string CardName = "Stony Silence";
    public const string Cost = "{1}{W}";

    /// <summary>
    /// Construct a Stony Silence with no live wiring. The printed static
    /// is not registered (no event bus). Suitable for card-shape /
    /// dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct a Stony Silence whose printed static is wired against
    /// <paramref name="eventBus"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be
    /// null — the lifecycle will still sync once on Attach.</param>
    public static Enchantment Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        if (eventBus != null)
        {
            var lifecycle = new StonySilenceStaticEffect(source: card, eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }
}
