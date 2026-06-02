using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sorcerous Spyglass (Ixalan, {2}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "As this artifact enters, look at an opponent's hand, then choose any
///    card name.
///    Activated abilities of sources with the chosen name can't be
///    activated unless they're mana abilities."
///
/// Sorcerous Spyglass is the functional twin of
/// <see cref="PithingNeedleFactory"/> — same printed static (CR 602.5c)
/// gated by the same name-restriction registry, made "as ~ enters"
/// (CR 614.12). The only differences are the {2} cost and an information-
/// only ETB rider ("look at an opponent's hand") that precedes the name
/// choice. The base shape (name, single Artifact card type, {2}) is
/// materialised from the embedded JSON definition
/// (<c>sorcerous-spyglass.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="RenegadeMapFactory"/> — and the Pithing-Needle printed
/// static is layered on top.
///
/// ## Implemented (v1)
/// - Artifact with mana cost {2} and correct identity / owner / controller.
/// - <b>Printed static</b> (CR 602.5c): name-targeted activated-ability
///   suppression. Wired via <see cref="PithingNeedleStaticEffect"/>: as the
///   Spyglass enters the battlefield, the supplied <c>nameSelector</c> is
///   invoked to resolve the chosen card name; that name is registered into
///   <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions"/>, and
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects activated-
///   ability activation whose source has that name.
/// - <b>CR 605 mana-ability exemption</b>: inherited from
///   <see cref="PithingNeedleStaticEffect"/> — mana abilities take the
///   <see cref="Majik.Core.Services.ManaAbilityActivator"/> path which
///   bypasses <see cref="Majik.Core.Rules.ActionValidator"/>, so they
///   activate normally even on a named source.
///
/// ## Deferred (v1 gaps)
/// - <b>"Look at an opponent's hand" rider</b>: a pure-information ETB
///   action (CR 701.16 look semantics). It reveals the opponent's hand to
///   the Spyglass's controller but changes no game state — the same class
///   of gap as the unemitted reveal events on every tutor-to-hand factory
///   (e.g. <see cref="RenegadeMapFactory"/>). The selector closure is the
///   natural home for surfacing that information to the agent when the
///   ChooseCardName prompt lands; this factory's static is observationally
///   complete without it.
/// - <b>"As ~ enters" choice timing</b>: CR 614.12 — the choice is
///   technically made as part of the ETB replacement, not after. The
///   effect treats the resolution point of the ETB as the prompt moment,
///   which is observationally equivalent in the engine's current ETB
///   pipeline. Same wrinkle as Pithing Needle / Phyrexian Revoker.
/// - <b>Agent-prompt integration</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   doesn't yet declare a ChooseCardName prompt. Until that lands, the
///   factory accepts a <c>Func&lt;Player, string&gt;</c> selector closure
///   — bots and tests supply the chosen name directly. When the prompt
///   lands, the selector signature stays; the closure simply forwards to
///   <c>agent.ChooseCardNameAsync(...)</c>.
/// </summary>
[CardName("Sorcerous Spyglass")]
public static class SorcerousSpyglassFactory
{
    public const string CardName = "Sorcerous Spyglass";
    public const string Slug = "sorcerous-spyglass";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct a Sorcerous Spyglass with no selector wired. Suitable for
    /// card-shape / dispatcher tests — the printed static will not register
    /// any name restriction.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, nameSelector: null, eventBus: null);

    /// <summary>
    /// Construct a Sorcerous Spyglass whose printed static is fully wired
    /// against <paramref name="eventBus"/> and resolves the chosen name via
    /// <paramref name="nameSelector"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="nameSelector">Resolves the chosen card name when the
    /// Spyglass enters the battlefield. Called with the Spyglass's
    /// controller. May be null — the suppression simply won't activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be
    /// null — the lifecycle will still sync once on Attach.</param>
    public static Artifact Create(
        Player owner,
        Func<Player, string>? nameSelector,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Artifact, {2}) from the embedded JSON definition.
        var spyglass = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        spyglass.SetOwner(owner);
        spyglass.SetController(owner);

        if (nameSelector != null)
        {
            // Reuse PithingNeedleStaticEffect — identical CR 602.5c
            // suppression semantics, same name-restriction registry, same
            // LTB cleanup via CardMovedEvent. The "look at an opponent's
            // hand" rider is information-only and carries no game-state
            // change in the current engine (see class xmldoc), so the
            // static is the entirety of the observable behaviour.
            var lifecycle = new PithingNeedleStaticEffect(
                source: spyglass,
                controller: owner,
                nameSelector: nameSelector,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return spyglass;
    }
}
