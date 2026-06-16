using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.21e — shared wiring that turns a bound <see cref="WardEffect"/> into a
/// real battlefield-attached <see cref="TriggeredAbility"/>:
///
///   "Whenever this permanent becomes the target of a spell [or ability] an
///    opponent controls, counter that spell [or ability] unless its controller
///    pays {ward cost}."
///
/// <para>
/// Ward is a triggered ability (CR 702.21e). It fires off
/// <see cref="TargetsChosenEvent"/> — which the live cast path
/// (<c>SpellCastFlow</c>) and ability-activation path
/// (<c>AbilityActivator</c>) publish once targets are locked (CR 601.2c) — when
/// (a) the targeting stack object is controlled by an OPPONENT of the warded
/// permanent's controller (CR 102.1) and is of the matched kind
/// (<see cref="WardTriggerKind"/> — "a spell" vs "a spell or ability"), and
/// (b) the warded permanent is itself among the chosen targets
/// ("this permanent becomes the target", CR 702.21e).
/// </para>
///
/// <para>
/// On resolution the bound <see cref="WardEffect"/> charges its
/// <see cref="WardEffect.PaymentCost"/> against the targeting player; if they
/// can't (or, in v1's pay-when-able posture, won't) pay, the spell/ability is
/// countered (CR 701.5b — a countered spell goes to its owner's graveyard; a
/// countered ability simply ceases to exist). The counter reaches the LIVE
/// stack via <see cref="ResolutionContext.Game"/> (<c>ctx.Game.Stack</c>,
/// CR 608) — the <see cref="Majik.Core.Services.StackResolver"/> hands every
/// resolving trigger a live <see cref="Majik.Core.Game.GameContext"/> — with
/// an optional construction-time <paramref name="stackFallback"/> for the
/// legacy synchronous shape-test path.
/// </para>
///
/// <para>
/// This is the shared distillation of the per-card ward closures originally
/// hand-written on Reality Smasher / Unsettled Mariner / Pawpatch Recruit, so
/// every Ward carrier in the pool gets identical, rules-correct enforcement
/// from its <see cref="WardEffect"/> with no duplicated counter plumbing.
/// </para>
/// </summary>
public static class WardTriggerWiring
{
    /// <summary>
    /// CR 702.21 — what the printed ward triggers off. Most printed wards read
    /// "a spell or ability"; a handful (Reality Smasher) read only "a spell".
    /// </summary>
    public enum WardTriggerKind
    {
        /// <summary>"a spell or ability an opponent controls".</summary>
        SpellOrAbility,

        /// <summary>"a spell an opponent controls" (Reality Smasher).</summary>
        SpellOnly,
    }

    /// <summary>
    /// Build the ward <see cref="TriggeredAbility"/> for <paramref name="ward"/>
    /// (whose <see cref="WardEffect.Source"/> is the warded permanent).
    /// </summary>
    /// <param name="ward">The bound ward effect (carries the source permanent +
    /// the payment cost).</param>
    /// <param name="owner">The trigger's controller (the warded permanent's
    /// owner; the live controller is read at resolution off
    /// <see cref="Permanent.Controller"/> so a stolen permanent's ward protects
    /// its current controller — CR 702.21e).</param>
    /// <param name="kind">Whether the printed ward reads "a spell" or
    /// "a spell or ability".</param>
    /// <param name="stackFallback">Optional construction-time stack used only on
    /// the legacy synchronous resolve path (no <see cref="GameContext"/>). The
    /// live path uses <see cref="ResolutionContext.Game"/>'s stack.</param>
    public static TriggeredAbility Build(
        WardEffect ward,
        Player owner,
        WardTriggerKind kind = WardTriggerKind.SpellOrAbility,
        Majik.Core.Stack.Stack? stackFallback = null)
    {
        ArgumentNullException.ThrowIfNull(ward);
        ArgumentNullException.ThrowIfNull(owner);

        var permanent = ward.Source;
        IStackObject? capturedSource = null;

        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            var controller = permanent.Controller ?? owner;

            // CR 702.21e — match the printed object kind ("a spell" vs
            // "a spell or ability").
            if (kind == WardTriggerKind.SpellOnly
                && e.StackObject is not Majik.Core.Spells.ISpell)
            {
                return false;
            }

            // CR 102.1 — "an opponent controls". Ward never triggers off the
            // warded permanent's own controller's spells/abilities.
            var sourceController = e.StackObject.Controller;
            if (sourceController == null) return false;
            if (ReferenceEquals(sourceController, controller)) return false;

            // CR 702.21e — "this permanent becomes the target": the warded
            // permanent must be among the chosen targets.
            foreach (var t in e.Targets)
            {
                if (TargetIsPermanent(t, permanent))
                {
                    capturedSource = e.StackObject;
                    return true;
                }
            }

            return false;
        });

        var counterEffect = new Effect(
            $"{permanent.Name} — ward: counter unless its controller pays the ward cost",
            ctx =>
            {
                var source = capturedSource;
                capturedSource = null;

                var liveStack = ctx.Game?.Stack ?? stackFallback;
                if (source == null || liveStack == null)
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                // CR 608.2b — recheck the targeting object is still on the stack
                // at resolution. If it already left, nothing to counter.
                if (!liveStack.GetAll().Contains(source))
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                var caster = source.Controller;
                if (caster == null)
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                // CR 702.21f — "unless its controller pays {cost}". The bound
                // WardEffect charges its PaymentCost when the caster can (and,
                // in v1, auto-chooses to) pay. Resolve returns true when the
                // spell/ability should be COUNTERED (cost not paid).
                if (!ward.Resolve(caster))
                    return System.Threading.Tasks.ValueTask.CompletedTask; // paid → not countered.

                // CR 701.5b — counter the spell/ability. RemoveFromStack returns
                // false for an uncounterable spell (it stays put).
                if (!OracleSpellBinder.RemoveFromStack(liveStack, source))
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                // CR 701.5b — a countered SPELL goes to its owner's graveyard;
                // a countered ability simply ceases to exist (no zone move).
                if (source is Majik.Core.Spells.ISpell spell && spell.Card is Card spellCard)
                {
                    spellCard.SetZone(ZoneType.Graveyard);
                }

                return System.Threading.Tasks.ValueTask.CompletedTask;
            });

        return new TriggeredAbility(
            source: permanent,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });
    }

    /// <summary>
    /// Build the ward trigger AND attach it to the warded permanent + register
    /// it (when a <see cref="TriggerManager"/> is supplied). Returns the built
    /// trigger. This is the one-liner the per-card factories call.
    /// </summary>
    public static TriggeredAbility Attach(
        WardEffect ward,
        Player owner,
        WardTriggerKind kind = WardTriggerKind.SpellOrAbility,
        Majik.Core.Stack.Stack? stackFallback = null,
        TriggerManager? triggers = null)
    {
        var trigger = Build(ward, owner, kind, stackFallback);
        ward.Source.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
        return trigger;
    }

    /// <summary>
    /// CR 702.21e — is <paramref name="target"/> the warded
    /// <paramref name="permanent"/> itself?
    /// </summary>
    private static bool TargetIsPermanent(ITarget target, Permanent permanent)
    {
        if (target is not Target concrete) return false;

        return concrete.TargetType switch
        {
            TargetType.Permanent or TargetType.Card =>
                ReferenceEquals(concrete.TargetObject, permanent),
            _ => false,
        };
    }
}
