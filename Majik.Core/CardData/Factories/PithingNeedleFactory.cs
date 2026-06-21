using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
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
/// ## Agent-prompt integration (CR 614.12 / CR 201.4)
/// The deferral <c>choose-card-name-agent-surface</c> is paid down: the
/// production single-arg <see cref="Create(Player)"/> (and the
/// <see cref="Create(Player, Majik.Core.Game.GameContext?, IEventBus?)"/>
/// overload) install an agent-prompting <c>nameSelector</c> that resolves the
/// chosen name through
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseCardNameAsync"/> via
/// <see cref="Majik.Core.CardData.CardNameChoice"/> — the opponents' visible
/// "known threats" pool, most-threatening-first. The
/// <c>Func&lt;Player, string&gt;</c> selector overload remains for tests that
/// want to supply a fixed name.
///
/// ## Deferred (v1 gaps)
/// - <b>"As ~ enters" choice timing</b>: CR 614.12 (replacement effect on
///   ETB) — the choice is technically made as part of the ETB replacement,
///   not after. The Needle resolves the name at the ETB Sync point, which is
///   observationally equivalent in the engine's current ETB pipeline.
/// - <b>Remote free-text name entry</b>: a remote (human) agent's
///   <c>ChooseCardNameAsync</c> currently lands on the suggested-name default
///   (boxed strings don't round-trip the ChoiceCommand id map), the same
///   posture as <c>ChooseColorAsync</c>; a dedicated wire command is a
///   follow-up.
/// </summary>
[CardName("Pithing Needle")]
public static class PithingNeedleFactory
{
    public const string CardName = "Pithing Needle";
    public const string Cost = "{1}";

    /// <summary>
    /// Construct a Pithing Needle whose ETB name choice is resolved through
    /// the controller's <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
    /// (the production posture — pays down the
    /// <c>choose-card-name-agent-surface</c> deferral). The printed static
    /// prompts <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseCardNameAsync"/>
    /// at resolution via <see cref="CardNameChoice"/>. When no agent is
    /// registered for the owner (a pure shape / dispatcher test with no game),
    /// the choice returns empty and the static stays inert — the same
    /// observable behaviour the old null-selector single-arg build had.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, game: null, eventBus: null);

    /// <summary>
    /// Production-shaped overload: resolve the chosen name through the owner's
    /// agent at ETB. <paramref name="game"/> is threaded into
    /// <see cref="CardNameChoice"/> so the agent's suggestion pool is the
    /// opponents' visible "known threats" (most-threatening first). May be null
    /// (no live game → empty suggestion pool, agent falls back).
    /// </summary>
    public static Artifact Create(Player owner, GameContext? game, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Func<Player, string> selector = chooser =>
            CardNameChoice.ChooseSync(game, chooser, CardNameChoice.AnyCardNameLabel);
        return Create(owner, nameSelector: selector, eventBus: eventBus);
    }

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
