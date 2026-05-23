using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pithing Needle (Saviors of Kamigawa, {1}).
///
/// Artifact — {1}.
/// Oracle text:
///   "As Pithing Needle enters, choose a card name.
///    Activated abilities of sources with the chosen name can't be
///    activated unless they're mana abilities."
///
/// ## Implemented (v1)
/// - Artifact with mana cost {1} and correct identity / owner / controller.
/// - <b>Printed static</b> (CR 602.5c): name-targeted activated-ability
///   suppression. Wired via <see cref="PithingNeedleStaticEffect"/>: as
///   the Needle enters the battlefield, the supplied <c>nameSelector</c>
///   is invoked to resolve the chosen card name; that name is registered
///   into <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions"/>, and
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects activated-
///   ability activation whose source has that name.
/// - <b>CR 605 mana-ability exemption</b>: the restriction registry only
///   rejects <see cref="Majik.Core.Abilities.IActivatedAbility"/>
///   activations; mana abilities take the
///   <see cref="Majik.Core.Services.ManaAbilityActivator"/> path which
///   bypasses <see cref="Majik.Core.Rules.ActionValidator"/>, so they
///   activate normally even on a named source.
///
/// ## Deferred (v1 gaps)
/// - <b>"As ~ enters" choice timing</b>: CR 614.12 (replacement effect on
///   ETB) — the choice is technically made as part of the ETB
///   replacement, not after. The Needle's effect treats the resolution
///   point of the ETB as the prompt moment, which is observationally
///   equivalent in the engine's current ETB pipeline.
/// - <b>Agent-prompt integration</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   doesn't yet declare a ChooseCardName prompt. Until that lands, the
///   factory accepts a <c>Func&lt;Player, string&gt;</c> selector closure
///   — bots and tests supply the chosen name directly. When the prompt
///   lands, the selector signature stays; the closure simply forwards to
///   <c>agent.ChooseCardNameAsync(...)</c>.
/// </summary>
public static class PithingNeedleFactory
{
    public const string CardName = "Pithing Needle";
    public const string Cost = "{1}";

    /// <summary>
    /// Construct a Pithing Needle with no selector wired. Suitable for
    /// card-shape / dispatcher tests — the printed static will not
    /// register any name restriction.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, nameSelector: null, eventBus: null);

    /// <summary>
    /// Construct a Pithing Needle whose printed static is fully wired
    /// against <paramref name="eventBus"/> and resolves the chosen name
    /// via <paramref name="nameSelector"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="nameSelector">Resolves the chosen card name when the
    /// Needle enters the battlefield. Called with the Needle's
    /// controller. May be null — the suppression simply won't activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be
    /// null — the lifecycle will still sync once on Attach.</param>
    public static Artifact Create(
        Player owner,
        Func<Player, string>? nameSelector,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var needle = new Artifact(CardName, Cost);
        needle.SetOwner(owner);
        needle.SetController(owner);

        if (nameSelector != null)
        {
            var lifecycle = new PithingNeedleStaticEffect(
                source: needle,
                controller: owner,
                nameSelector: nameSelector,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return needle;
    }
}
