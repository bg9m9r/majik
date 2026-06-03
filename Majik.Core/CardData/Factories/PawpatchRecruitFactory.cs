using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pawpatch Recruit (Bloomburrow, {G}).
///
/// Creature — Rabbit Warrior, 2/1. Oracle text (Scryfall, verified 2026-06-02):
///   "Offspring {2} (You may pay an additional {2} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    Trample
///    Whenever a creature you control becomes the target of a spell or ability
///    an opponent controls, put a +1/+1 counter on target creature you control
///    other than that creature."
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
/// when the caster chooses to pay; declining omits it.
///
/// ## Trample (CR 702.19)
///
/// A plain <see cref="KeywordAbility"/> marker — combat math reads it via the
/// keyword scan (same posture as every other Trample creature).
///
/// ## Residual trigger — "becomes the target of an opponent's spell/ability"
///
/// "Whenever a creature you control becomes the target of a spell or ability an
/// opponent controls, put a +1/+1 counter on target creature you control other
/// than that creature." (CR 603.6c / 115.6 / 603.2-3) — wired via
/// <see cref="TargetsChosenEvent"/>, the engine's existing "becomes the target"
/// seam (published by both <see cref="Majik.Core.Services.SpellCaster"/> and
/// <see cref="Majik.Core.Services.AbilityActivator"/>, so "a spell or ability"
/// is covered uniformly — the same attachment point as
/// <see cref="NaduWingedWisdomFactory"/> / <see cref="HeartfireHeroFactory"/>).
/// The earlier deferral (no "becomes the target" event existed) is obsolete:
/// <see cref="TargetsChosenEvent"/> already locks the targeting source +
/// targets at CR 601.2c / 603.3e. Pawpatch's distinguishing filters layered on
/// top of the Nadu shape:
/// <list type="bullet">
///   <item><b>opponent-controlled source</b> (CR 109.5) — the stack object's
///   <see cref="Majik.Core.Stack.IStackObject.Controller"/> must NOT be
///   Pawpatch's controller (same not-our-controller posture as
///   <see cref="Majik.Core.Keywords.WardEffect.Applies"/>'s opponent test).</item>
///   <item><b>"a creature you control"</b> — some chosen target is a creature
///   whose controller is Pawpatch's controller (resolved live, CR 109.5).</item>
///   <item><b>"target creature you control OTHER than that creature"</b> — the
///   trigger is itself a targeted ability (CR 603.3d); the counter recipient is
///   supplied by a caller resolver that receives the originally-targeted
///   creature and returns a different controlled creature to receive the +1/+1
///   counter (same caller-supplied-choice posture as
///   <see cref="HeartfireHeroFactory"/>'s opponent resolver). When no resolver
///   / no eligible other creature is available the counter side no-ops.</item>
/// </list>
///
/// The Offspring + Trample halves remain as before.
/// </summary>
[CardName("Pawpatch Recruit")]
public static class PawpatchRecruitFactory
{
    public const string CardName = "Pawpatch Recruit";
    public const string PrintedManaCost = "{G}";
    public const string OffspringCostText = "{2}";

    /// <summary>CR 702.169 — the Offspring additional cost ({2}).</summary>
    public static ManaCost OffspringCost => ManaCost.Parse(OffspringCostText);

    /// <summary>Shape-only construction (no live trigger-manager wiring).</summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, counterRecipientResolver: null);

    /// <summary>
    /// Construct Pawpatch Recruit. When <paramref name="triggers"/> is supplied
    /// the Offspring ETB trigger is registered so the centralised event pump
    /// queues it automatically in a real match. Back-compat overload — wires no
    /// bus and no counter-recipient resolver, so the becomes-the-target trigger
    /// surfaces as pending but its counter side no-ops.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers) =>
        Create(owner, eventBus: null, triggers: triggers, counterRecipientResolver: null);

    /// <summary>
    /// Construct Pawpatch Recruit with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for live wiring; currently unused by the
    /// becomes-the-target trigger (it reads no turn-scoped state). May be
    /// null.</param>
    /// <param name="triggers">TriggerManager the Offspring ETB + becomes-the-
    /// target triggers are registered with so they surface as pending. May be
    /// null.</param>
    /// <param name="counterRecipientResolver">CR 603.3d — supplies the "target
    /// creature you control other than that creature" that receives the +1/+1
    /// counter (CR 122). Receives the originally-targeted creature and must
    /// return a DIFFERENT creature the controller controls, or <c>null</c> when
    /// none is eligible (in which case the counter side no-ops, matching the
    /// caller-supplied-choice posture of
    /// <see cref="HeartfireHeroFactory"/>). May be null.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<Creature, Creature?>? counterRecipientResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var rabbit = new Creature(
            CardName, PrintedManaCost, power: 2, toughness: 1,
            subtypes: new[] { CardSubtype.Rabbit, CardSubtype.Warrior })
        {
            Owner = owner,
            Controller = owner,
        };

        // Offspring {2} ETB token-copy (CR 702.169b).
        OffspringAbility.Attach(rabbit, triggers);

        // CR 702.169 — keyword marker (the "{cost}" rider rides on the
        // OffspringAdditionalCost the caller layers onto the cast).
        rabbit.AddAbility(new KeywordAbility("Offspring", rabbit, owner, arg: 2));

        // CR 702.19 — Trample.
        rabbit.AddAbility(new KeywordAbility("Trample", rabbit, owner));

        // CR 603.6c / 115.6 — "Whenever a creature you control becomes the
        // target of a spell or ability an opponent controls, put a +1/+1
        // counter on target creature you control other than that creature."
        var targeted = BuildBecomesTargetTrigger(rabbit, owner, counterRecipientResolver);
        rabbit.AddAbility(targeted);
        triggers?.RegisterTriggeredAbility(targeted);

        return rabbit;
    }

    /// <summary>
    /// Build the residual becomes-the-target trigger (CR 603.6c / 115.6 /
    /// 603.2-3). Fires on a <see cref="TargetsChosenEvent"/> whose stack object
    /// is controlled by an OPPONENT of <paramref name="rabbit"/>'s controller
    /// (CR 109.5) and whose chosen targets include a creature that controller
    /// controls. On resolution it puts a +1/+1 counter (CR 122) on a chosen
    /// OTHER creature the controller controls, supplied by
    /// <paramref name="counterRecipientResolver"/>.
    /// </summary>
    private static TriggeredAbility BuildBecomesTargetTrigger(
        Creature rabbit, Player owner, Func<Creature, Creature?>? counterRecipientResolver)
    {
        // The creature of Pawpatch's controller that became the target,
        // captured at trigger-evaluation time so the resolution effect knows
        // which creature counts as "that creature" (the one to exclude).
        Creature? capturedTargeted = null;

        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            // "an opponent controls" — the targeting spell/ability's controller
            // must NOT be Pawpatch's controller (CR 109.5). Same opponent test
            // as WardEffect.Applies. TargetsChosenEvent is published by both
            // SpellCaster and AbilityActivator, so "spell or ability" is
            // covered uniformly.
            if (ReferenceEquals(e.StackObject.Controller, rabbit.Controller)) return false;

            // "a creature you control becomes the target" — some chosen target
            // is a creature whose controller is Pawpatch's controller
            // (resolved live, CR 109.5).
            foreach (var t in e.Targets)
            {
                if (t.TargetType != TargetType.Permanent && t.TargetType != TargetType.Card)
                {
                    continue;
                }
                if (t is not Target concrete) continue;
                if (concrete.TargetObject is not Creature targetCreature) continue;
                if (!targetCreature.HasType(CardType.Creature)) continue;
                if (!ReferenceEquals(targetCreature.Controller, rabbit.Controller)) continue;

                capturedTargeted = targetCreature;
                return true;
            }

            return false;
        });

        var counterEffect = new Effect(
            "Pawpatch Recruit: put a +1/+1 counter on target creature you control other than that creature",
            () =>
            {
                var thatCreature = capturedTargeted;
                capturedTargeted = null;
                if (thatCreature == null) return;

                // CR 603.3d — the trigger targets a creature you control OTHER
                // than the one that became the target. The caller resolver
                // supplies that choice; a null return (no eligible other
                // creature) is a legal no-op (CR 608.2b — if no legal target,
                // the ability does nothing).
                var recipient = counterRecipientResolver?.Invoke(thatCreature);
                if (recipient == null) return;
                if (ReferenceEquals(recipient, thatCreature)) return; // guard: must be "other"

                recipient.Counters.Add(CounterType.PlusOnePlusOne); // CR 122
            });

        return new TriggeredAbility(
            source: rabbit,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });
    }

    /// <summary>Build the Offspring {2} additional cost for this spell.</summary>
    public static IAdditionalCost BuildOffspringCost(ICard card) =>
        new OffspringAdditionalCost(card, OffspringCost);
}
