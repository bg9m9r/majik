using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Deathcap Marionette (Bloomburrow, {1}{B}).
///
/// Creature — Fungus 1/1. Oracle text (Scryfall, verified):
///   "Deathtouch
///    When this creature enters, you may mill two cards. (You may put the top
///    two cards of your library into your graveyard.)"
///
/// ## Shape source
/// Card identity (name, {1}{B}, 1/1, Creature — Fungus) and the Deathtouch
/// keyword are loaded from <c>Majik.Core/CardData/Cards/deathcap-marionette.json</c>
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The JSON <c>keywords</c> array carries
/// Deathtouch (CR 702.2) as a <see cref="KeywordAbility"/> marker, which the
/// combat engine reads via <c>CombatAbilities.HasDeathtouch</c> (same posture as
/// Engine Rat / every other JSON-driven deathtouch creature). The optional ETB
/// self-mill trigger is attached in code below — same JSON-shape + code-trigger
/// split as <see cref="CogworkWrestlerFactory"/>.
///
/// ## Implemented (v1)
/// - 1/1 Creature — Fungus at {1}{B} with Deathtouch (from JSON).
/// - <b>Optional ETB triggered ability (CR 603.6a)</b>: "When this creature
///   enters, you may mill two cards." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> as an async ctx-effect. At
///   resolution the controller's agent is asked a Yes/No (CR 601.2b — an
///   optional "may" action) via
///   <see cref="IPlayerAgent.ChooseYesNoAsync(Majik.Core.Game.GameContext?, string, string?, System.Threading.CancellationToken)"/>;
///   on "yes" the controller mills two from their own library (CR 701.13b) via
///   <see cref="MillAction.Apply"/>. Self-mill, so the controller is read off the
///   source card at resolution (CR 608.2 — resolve under current game state),
///   not "each opponent". Mirrors the optional-"may" agent-prompt posture of
///   <see cref="CabalTherapistFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>No-agent dispatcher fallback</b>: when no agent is reachable (shape-only
///   construction / <c>ResolveAsync(agent: null)</c> with no
///   <see cref="AgentRegistry"/> entry) the optional mill defaults to declining
///   (the safe no-op posture for a "you may"). Same as
///   <see cref="CabalTherapistFactory"/>.
/// </summary>
[CardName("Deathcap Marionette")]
public static class DeathcapMarionetteFactory
{
    public const string CardName = "Deathcap Marionette";

    /// <summary>Cards milled by the optional ETB trigger (printed value).</summary>
    public const int MillCount = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("deathcap-marionette");

    /// <summary>
    /// Construct Deathcap Marionette with the optional ETB self-mill trigger
    /// attached to the card shape but NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// Deathtouch is attached from the JSON keyword array.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Deathcap Marionette with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied the ETB trigger is
    /// registered so it fires (and prompts the controller's agent) automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 603.6a — ETB triggered ability:
        //   "When this creature enters, you may mill two cards."
        // CR 601.2b — the "you may" is an optional action: ask the controller's
        // agent before milling. CR 701.13b — Mill moves the top N of the
        // controller's library into their graveyard.
        var etbEffect = new Effect(
            $"{CardName}: you may mill {MillCount}",
            async ctx =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var controller = card.Controller ?? owner;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                if (agent == null) return; // no decision-maker → "you may" declines.

                var wantsTo = await agent
                    .ChooseYesNoAsync(ctx.Game, $"Mill {MillCount} cards?", CardName, ctx.Ct)
                    .ConfigureAwait(false);
                if (!wantsTo) return;

                // CR 701.13b — empty / short library handled inside MillAction.Apply
                // (mills all remaining; does NOT itself cause a loss).
                MillAction.Apply(controller, MillCount);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
