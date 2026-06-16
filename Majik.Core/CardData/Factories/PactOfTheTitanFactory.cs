using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pact of the Titan (Future Sight, {0}).
///
/// Instant. Oracle text:
///   "Create a 4/4 red Giant creature token.
///    At the beginning of your next upkeep, pay {4}{R}. If you don't,
///    you lose the game."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {0}, owner / controller.
/// - <b>Create a 4/4 Giant token</b> — <see cref="BuildDefinition"/>
///   builds a target-less <see cref="SpellDefinition"/>; on resolution it
///   calls <see cref="TokenFactory.CreateOnBattlefield"/> with a 4/4
///   <see cref="CardSubtype.Giant"/> spec under the caster's control
///   (CR 111 / CR 111.6).
/// - <b>Delayed upkeep pact</b> — mirrors the
///   <see cref="PactOfNegationFactory"/> / <see cref="SlaughterPactFactory"/>
///   pattern. When a <see cref="TriggerManager"/> is supplied the resolve
///   effect registers a <see cref="DelayedTriggeredAbility"/> (CR 603.7)
///   that fires on the controller's next <see cref="StepStateType.Upkeep"/>
///   <see cref="StepStartedEvent"/>. The trigger calls
///   <see cref="Player.PayMana"/> with {4}{R} against the controller's
///   mana pool; on failure <see cref="Player.MarkLost"/> flags the
///   controller (CR 104.3 / CR 118.3 — "if you don't, you lose the game").
///
/// ## Cost-payment prompt
/// At upkeep the caster's agent is prompted "Pay {4}{R} or lose the game?"
/// via the shared
/// <see cref="Majik.Core.Primitives.UpkeepPayUnlessConsequence"/> primitive
/// (CR 117.1); on "yes" + affordable {4}{R} is drained, on "no" /
/// can't-afford the caster loses (CR 104.3 / CR 118.3). The legacy /
/// shape-only sync path keeps the deterministic "pay if able" posture.
///
/// ## Deferred (v1 gaps)
/// - <b>No in-trigger tap-lands step</b>: the {4}{R} is paid from whatever
///   is already in the caster's pool when the delayed trigger resolves — the
///   decision flows through the agent prompt, but there is no resolution-time
///   "tap a land for {4}{R}" sub-prompt.
/// </summary>
[CardName("Pact of the Titan")]
public static class PactOfTheTitanFactory
{
    public const string CardName = "Pact of the Titan";
    public const string PrintedManaCost = "{0}";
    public const string DelayedUpkeepCost = "{4}{R}";
    public const int TokenPower = 4;
    public const int TokenToughness = 4;

    /// <summary>
    /// Construct the Pact of the Titan card shape (Instant, {0}). Resolve
    /// behaviour is built on demand via <see cref="BuildDefinition"/> so
    /// the dispatcher path can produce a shape-only card.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. No targets;
    /// on resolution creates a 4/4 red Giant creature token under the
    /// caster's control and (when a <see cref="TriggerManager"/> is
    /// supplied) registers a one-shot delayed trigger that fires at the
    /// caster's next upkeep.
    /// </summary>
    /// <param name="caster">The controller of Pact of the Titan — also
    /// the controller of the new token and the delayed upkeep trigger.</param>
    /// <param name="triggers">Optional trigger manager. When null the
    /// delayed upkeep payment / loss is skipped (suitable for
    /// token-only shape tests).</param>
    /// <param name="zoneService">Optional zone service so token-ETB
    /// CardMovedEvent fires (Soul Warden etc.). Pass <c>null</c> for raw
    /// zone moves.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return SpellDefinition.Vanilla(_ => new IEffect[]
        {
            new Effect("Pact of the Titan: create 4/4 Giant token + queue delayed upkeep pact", () =>
            {
                // ----------------------------------------------------
                // CR 111 / CR 111.6 — create one 4/4 red Giant
                // creature token under the caster's control.
                // ----------------------------------------------------
                CreateGiantToken(caster, zoneService);

                // ----------------------------------------------------
                // CR 603.7 — register the "at the beginning of your
                // next upkeep" delayed trigger. Mirrors Pact of
                // Negation: fires on the first Upkeep StepStartedEvent
                // strictly after this resolve, attempts to pay {4}{R}
                // from the controller's pool, falls back to MarkLost
                // on failure (CR 104.3 / CR 118.3).
                // ----------------------------------------------------
                if (triggers == null) return;

                var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
                var pactCost = ManaCost.Parse(DelayedUpkeepCost);

                // CR 117.1 — at upkeep the caster's agent is prompted
                // "Pay {4}{R}?" via the shared
                // Majik.Core.Primitives.UpkeepPayUnlessConsequence primitive;
                // on "yes" + affordable {4}{R} is drained, on "no" /
                // can't-afford the caster loses (CR 104.3 / CR 118.3). The
                // legacy / shape-only sync path keeps "pay if able".
                var payOrLoseEffect = Majik.Core.Primitives.UpkeepPayUnlessConsequence.Build(
                    $"Pact of the Titan: pay {DelayedUpkeepCost} at upkeep or lose the game",
                    caster,
                    pactCost,
                    consequence: caster.MarkLost,
                    promptText: $"Pay {DelayedUpkeepCost} or lose the game?",
                    guard: () => !caster.HasLost);

                var delayed = new DelayedTriggeredAbility(
                    source: caster,
                    controller: caster,
                    condition: new EventTriggerCondition<StepStartedEvent>(
                        (e, _) => e.StepType == StepStateType.Upkeep
                                  && ReferenceEquals(e.Player, caster)
                                  && e.Timestamp > resolvedAt),
                    effects: new IEffect[] { payOrLoseEffect });

                triggers.RegisterDelayed(delayed);
            }),
        });
    }

    /// <summary>
    /// Create a single 4/4 Giant creature token under
    /// <paramref name="controller"/>. CR 111 / CR 111.6.
    /// </summary>
    public static Creature CreateGiantToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Giant",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Giant },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "4/4 red Giant creature token".
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.Red });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
