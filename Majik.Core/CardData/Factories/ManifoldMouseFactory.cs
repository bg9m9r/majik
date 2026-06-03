using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Manifold Mouse (Bloomburrow, {1}{R}).
///
/// Creature — Mouse Soldier, 1/2. Oracle text (Scryfall, verified 2026-06-02):
///   "Offspring {2} (You may pay an additional {2} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    At the beginning of combat on your turn, target Mouse you control gains
///    your choice of double strike or trample until end of turn."
///
/// ## Offspring {2} (CR 702.169)
///
/// Wired through the generic Offspring keyword subsystem:
/// <see cref="OffspringAdditionalCost"/> (the optional additional cast cost,
/// CR 702.169a — drains {2} and stamps <see cref="Card.WasOffspringPaid"/>) +
/// <see cref="OffspringAbility.Attach"/> (the ETB trigger, CR 702.169b — when
/// this creature enters, if its Offspring cost was paid, create a 1/1 token
/// copy of it). The caller layers <see cref="BuildOffspringCost"/> onto the
/// cast via <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c>
/// when the caster chooses to pay; declining simply omits it.
///
/// ## Begin-combat grant (CR 508.1 / 702.4 / 702.19)
///
/// An "at the beginning of combat on your turn" <see cref="TriggeredAbility"/>
/// (<see cref="Triggers.OnStepBegin"/> restricted to the controller's turns)
/// with a 1..1 "target Mouse you control" request. On resolution the chosen
/// Mouse gains the controller's choice of Double strike (CR 702.4) or Trample
/// (CR 702.19) until end of turn, registered as a
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/> on the target's
/// <see cref="Permanent.ActiveEffects"/> (CR 514.2 — expires at cleanup). The
/// "your choice" modal pick (CR 700.2) is resolved through the resolving
/// context's agent (<see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseModeAsync"/>),
/// defaulting to Double strike when no agent is wired (shape / direct-call
/// tests).
/// </summary>
[CardName("Manifold Mouse")]
public static class ManifoldMouseFactory
{
    public const string CardName = "Manifold Mouse";
    public const string PrintedManaCost = "{1}{R}";
    public const string OffspringCostText = "{2}";

    public const string ModeDoubleStrike = "Double strike";
    public const string ModeTrample = "Trample";

    /// <summary>CR 702.169 — the Offspring additional cost ({2}). Exposed so
    /// callers build the cost without hard-coding the value.</summary>
    public static ManaCost OffspringCost => ManaCost.Parse(OffspringCostText);

    /// <summary>Shape-only construction (no live trigger-manager / zone-service
    /// wiring). Suitable for <see cref="NamedCardFactory"/> dispatch / shape
    /// tests.</summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Manifold Mouse. When <paramref name="triggers"/> is supplied
    /// the Offspring ETB trigger and the begin-combat trigger are registered so
    /// the centralised event pump queues them automatically in a real match.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var mouse = new Creature(
            CardName, PrintedManaCost, power: 1, toughness: 2,
            subtypes: new[] { CardSubtype.Mouse, CardSubtype.Soldier })
        {
            Owner = owner,
            Controller = owner,
        };

        // Offspring {2} ETB token-copy (CR 702.169b).
        OffspringAbility.Attach(mouse, triggers);

        // CR 702.169 — expose the keyword marker so the keyword scan surface is
        // uniform (Trample / Haste shape). The "{cost}" rider is carried by the
        // OffspringAdditionalCost the caller layers onto the cast.
        mouse.AddAbility(new KeywordAbility("Offspring", mouse, owner, arg: 2));

        // At the beginning of combat on your turn, target Mouse you control
        // gains your choice of double strike or trample until end of turn.
        AttachBeginCombatGrant(mouse, owner, triggers);

        return mouse;
    }

    /// <summary>Build the Offspring {2} additional cost for this spell. Layer it
    /// onto the cast via SpellCastFlow's <c>additionalCosts</c> when the caster
    /// chooses to pay Offspring; omit it to decline.</summary>
    public static IAdditionalCost BuildOffspringCost(ICard card) =>
        new OffspringAdditionalCost(card, OffspringCost);

    private static void AttachBeginCombatGrant(Creature mouse, Player owner, TriggerManager? triggers)
    {
        TriggeredAbility? trigger = null;
        var effect = new Effect(
            $"{CardName}: target Mouse gains double strike or trample until end of turn",
            async ctx =>
            {
                if (trigger == null) return;
                if (trigger.ChosenTargets.Count == 0) return;
                if (trigger.ChosenTargets[0].Count == 0) return;
                if (trigger.ChosenTargets[0][0] is not Creature target) return;

                // CR 608.2b — resolve-time legality recheck: the chosen target
                // must still be a Mouse the controller controls on the
                // battlefield, with a live continuous-effects service.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.GetEffectiveSubtypes().Contains(CardSubtype.Mouse)) return;
                if (target.ActiveEffects == null) return;

                // CR 700.2 — "your choice of double strike or trample". Resolve
                // the modal pick through the resolving agent; default to Double
                // strike (index 0) when no agent / game context is wired.
                var keyword = ModeDoubleStrike;
                if (ctx.Agent != null && ctx.Game != null)
                {
                    var modes = new[] { ModeDoubleStrike, ModeTrample };
                    var idx = await ctx.Agent
                        .ChooseModeAsync(ctx.Game, modes, modeIntents: null, ctx.Ct)
                        .ConfigureAwait(false);
                    keyword = idx == 1 ? ModeTrample : ModeDoubleStrike;
                }

                target.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(target, keyword));
            });

        trigger = new TriggeredAbility(
            source: mouse,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.BeginningOfCombat),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target Mouse you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        mouse.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }
}
