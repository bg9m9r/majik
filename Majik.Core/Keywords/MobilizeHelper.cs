using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.170 — Mobilize N. "Whenever this creature attacks, create N tapped
/// and attacking 1/1 red Warrior creature tokens. Sacrifice those tokens at
/// the beginning of the next end step."
///
/// <para>This is the reusable mechanic shared by every Mobilize card (Voice of
/// Victory, Hero of Bladehold-style helpers, Den of the Bugbear, Hanweir
/// Garrison, …). A factory builds and registers the attack trigger via
/// <see cref="AttachTo"/>; the trigger's resolution mints the tokens, splices
/// them into the live combat tapped-and-attacking
/// (<see cref="CombatManager.AddTappedAndAttackingToken"/>), and registers a
/// delayed end-step sacrifice (CR 603.7 / 500.4).</para>
///
/// <para>Because the tokens are "put onto the battlefield attacking" rather
/// than "declared" (CR 508.3g), they do NOT re-trigger Mobilize or other
/// "whenever a creature attacks" abilities.</para>
/// </summary>
public static class MobilizeHelper
{
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>The 1/1 red Warrior token spec minted by Mobilize.</summary>
    public static TokenFactory.TokenSpec WarriorTokenSpec { get; } =
        new(
            Name: "Warrior",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Warrior },
            Keywords: null,
            Colors: new[] { ManaColor.Red });

    /// <summary>
    /// Build (and optionally register) the Mobilize <paramref name="count"/>
    /// attack-triggered ability on <paramref name="source"/>. When
    /// <paramref name="triggers"/> is supplied the trigger is registered so a
    /// <see cref="CreatureAttacksEvent"/> for the source lands it on the stack
    /// automatically; when <paramref name="combat"/> is supplied the tokens are
    /// spliced into the in-progress combat tapped and attacking. With neither,
    /// the trigger is attached to the card shape only (dispatcher / shape tests).
    /// </summary>
    public static TriggeredAbility AttachTo(
        Creature source,
        int count,
        TriggerManager? triggers = null,
        CombatManager? combat = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Mobilize N ≥ 0.");

        var owner = source.Owner;

        var effect = new Effect(
            $"{source.Name}: Mobilize {count} — create {count} tapped & attacking 1/1 red Warriors",
            () => Resolve(source, count, combat, triggers));

        var trigger = new TriggeredAbility(
            source: source,
            controller: owner!,
            condition: Triggers.OnAttackSelf(source),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        source.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
        return trigger;
    }

    /// <summary>
    /// Resolve Mobilize N — mint the tokens, splice them tapped-and-attacking
    /// into combat, and register the end-step sacrifice. Exposed so factories
    /// that build their own trigger shape can reuse the resolution body.
    /// </summary>
    public static IReadOnlyList<Creature> Resolve(
        Creature source,
        int count,
        CombatManager? combat,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(source);
        var controller = source.Controller ?? source.Owner!;

        var tokens = new List<Creature>(count);
        for (int i = 0; i < count; i++)
        {
            var token = TokenFactory.CreateOnBattlefield(WarriorTokenSpec, controller);
            tokens.Add(token);
            // CR 508.3g — splice the token into the in-progress combat. No live
            // combat → the token stays on the battlefield (untapped, not
            // attacking); the tapped-and-attacking fidelity needs a live combat.
            combat?.AddTappedAndAttackingToken(token);
        }

        if (triggers != null && tokens.Count > 0)
            RegisterEndStepSacrifice(source, controller, tokens, triggers);

        return tokens;
    }

    /// <summary>
    /// CR 603.7 / 500.4 — delayed one-shot that sacrifices the Mobilize tokens
    /// at the start of the next end step. Fence-checks
    /// <c>e.Timestamp &gt; resolvedAt</c> so the current end step (if Mobilize
    /// resolves during one) doesn't trip it.
    /// </summary>
    public static void RegisterEndStepSacrifice(
        Creature source,
        Player controller,
        IReadOnlyList<Creature> tokens,
        TriggerManager triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();

        var sacEffect = new Effect(
            $"{source.Name}: sacrifice {tokens.Count} Mobilize tokens at next end step",
            () =>
            {
                foreach (var token in tokens)
                {
                    if (token.Zone != ZoneType.Battlefield) continue;
                    var bfPlayer = token.Controller ?? controller;
                    if (!bfPlayer.Zones.Battlefield.GetCards().Contains(token)) continue;

                    // CR 701.16 — sacrifice: controller's battlefield → owner's
                    // graveyard. Tokens cease to exist as an SBA afterwards
                    // (CR 111.7 / 704.5d), handled by the engine's SBA pass.
                    var graveyardOwner = token.Owner ?? controller;
                    bfPlayer.Zones.Battlefield.RemoveCard(token);
                    graveyardOwner.Zones.Graveyard.AddCard(token);
                    token.SetZone(ZoneType.Graveyard);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: source,
            controller: controller,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End && e.Timestamp > resolvedAt),
            effects: new IEffect[] { sacEffect });

        triggers.RegisterDelayed(delayed);
    }
}
