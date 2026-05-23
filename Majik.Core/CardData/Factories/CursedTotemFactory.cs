using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cursed Totem (Mirage, {2}).
///
/// Artifact — {2}.
/// Oracle text:
///   "Activated abilities of creatures can't be activated unless they're
///    mana abilities."
///
/// ## Implemented (v1)
/// - Artifact with mana cost {2} and correct identity / owner / controller.
/// - <b>Printed static</b> (CR 602.5c / 605): global creature activated-
///   ability suppression. Wired via <see cref="CursedTotemStaticEffect"/>;
///   as Cursed Totem enters the battlefield, a predicate restriction is
///   registered into <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions"/>
///   matching any activated ability whose source is an on-battlefield
///   creature. <see cref="Majik.Core.Rules.ActionValidator"/> consults the
///   registry during activation validation. Both players' creatures are
///   gated — Cursed Totem is symmetric, mirroring
///   <see cref="StonySilenceFactory"/>'s artifact-side equivalent.
/// - <b>CR 605 mana-ability exemption</b>: the registry short-circuits on
///   <see cref="Majik.Core.Abilities.IManaAbility"/>; mana abilities also
///   route through <see cref="Majik.Core.Services.ManaAbilityActivator"/>
///   which bypasses <see cref="Majik.Core.Rules.ActionValidator"/>, so
///   {T}: Add {G} on a Birds of Paradise / Llanowar Elves / etc. still
///   works under Cursed Totem.
/// </summary>
public static class CursedTotemFactory
{
    public const string CardName = "Cursed Totem";
    public const string Cost = "{2}";

    /// <summary>
    /// Construct a Cursed Totem with no live wiring. The printed static
    /// is not registered (no event bus). Suitable for card-shape /
    /// dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct a Cursed Totem whose printed static is wired against
    /// <paramref name="eventBus"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be
    /// null — the lifecycle will still sync once on Attach.</param>
    public static Artifact Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        if (eventBus != null)
        {
            var lifecycle = new CursedTotemStaticEffect(source: card, eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }
}
