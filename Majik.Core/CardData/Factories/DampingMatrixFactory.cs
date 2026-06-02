using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Damping Matrix (Mirrodin, {3}).
///
/// Artifact — {3}.
/// Oracle text:
///   "Activated abilities of artifacts and creatures can't be activated
///    unless they're mana abilities."
///
/// ## Implemented (v1)
/// - Artifact with mana cost {3} and correct identity / owner / controller.
/// - <b>Printed static</b> (CR 602.5c / 605): global artifact- AND creature-
///   activated-ability suppression — functionally the union of
///   <see cref="StonySilenceFactory"/> (artifacts) and
///   <see cref="CursedTotemFactory"/> (creatures). Wired via
///   <see cref="DampingMatrixStaticEffect"/>; as Damping Matrix enters the
///   battlefield, a predicate restriction is registered into
///   <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions"/> matching
///   any activated ability whose source is an on-battlefield artifact or
///   creature. <see cref="Majik.Core.Rules.ActionValidator"/> consults the
///   registry during activation validation. Both players' permanents are
///   gated — Damping Matrix is symmetric (no "you control" qualifier).
///   Damping Matrix itself is an artifact, but its static ability is not
///   an activated ability, so the printed static remains in effect while
///   Damping Matrix is on the battlefield (CR 113.6).
/// - <b>CR 605 mana-ability exemption</b>: the registry short-circuits on
///   <see cref="Majik.Core.Abilities.IManaAbility"/>; mana abilities also
///   route through <see cref="Majik.Core.Services.ManaAbilityActivator"/>
///   which bypasses <see cref="Majik.Core.Rules.ActionValidator"/>, so
///   {T}: Add mana on a Mox / Sol Ring / Birds of Paradise / etc. still
///   works under Damping Matrix.
/// </summary>
[CardName("Damping Matrix")]
public static class DampingMatrixFactory
{
    public const string CardName = "Damping Matrix";
    public const string Cost = "{3}";

    /// <summary>
    /// Construct a Damping Matrix with no live wiring. The printed static
    /// is not registered (no event bus). Suitable for card-shape /
    /// dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct a Damping Matrix whose printed static is wired against
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
            var lifecycle = new DampingMatrixStaticEffect(source: card, eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }
}
