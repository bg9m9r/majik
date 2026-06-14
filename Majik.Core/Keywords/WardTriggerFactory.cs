using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.21e/f — the reusable <b>Ward triggered-ability primitive</b>.
///
/// Ward is a triggered ability (CR 702.21e): "Whenever this permanent becomes
/// the target of a spell or ability an opponent controls, counter that spell
/// or ability unless its controller pays [cost]." On resolution it offers the
/// targeting player the choice to pay the ward cost; if they don't, the spell
/// or ability is countered (CR 702.21f).
///
/// Every "this permanent becomes the target" Ward card (Kappa Cannoneer,
/// Tolarian Terror, Colossal Skyturtle, Sire of Seven Deaths, …) shares the
/// exact same wiring: a <see cref="TriggeredAbility"/> over
/// <see cref="TargetsChosenEvent"/> gated to (a) an opponent-controlled
/// targeting stack object, and (b) the warded permanent being one of the
/// chosen targets; whose resolution invokes
/// <see cref="WardEffect.Resolve(Player, bool)"/> to charge the ward cost and
/// otherwise counter through the live stack. This factory centralises that
/// shape so each per-card factory builds onto the
/// <see cref="WardEffect"/> primitive declaratively rather than re-deriving
/// the predicate + counter closure (the pre-existing duplication on
/// Reality Smasher / Graveyard Trespasser / Unsettled Mariner).
/// </summary>
public static class WardTriggerFactory
{
    /// <summary>
    /// CR 702.21e — build the Ward <see cref="TriggeredAbility"/> for
    /// <paramref name="ward"/>'s warded permanent (<see cref="WardEffect.Source"/>).
    ///
    /// The trigger fires on a <see cref="TargetsChosenEvent"/> where:
    ///   (a) the targeting stack object's controller is an OPPONENT of the
    ///       warded permanent's controller (CR 102.1 — "an opponent controls"),
    ///       and — when <paramref name="spellsOnly"/> is true — the targeting
    ///       object is a <see cref="Majik.Core.Spells.ISpell"/> (cards whose
    ///       printed text reads "a spell" rather than "a spell or ability");
    ///   (b) the warded permanent itself is among the chosen targets
    ///       (CR 702.21e — "this permanent becomes the target").
    ///
    /// On resolution (CR 702.21f) the bound <paramref name="ward"/> charges its
    /// <see cref="WardEffect.PaymentCost"/> against the targeting player; if the
    /// cost is not paid the targeting spell/ability is countered through the
    /// live stack (CR 701.5b — a countered spell goes to its owner's graveyard).
    /// </summary>
    /// <param name="ward">The bound <see cref="WardEffect"/> (carries the warded
    /// permanent + the ward <see cref="WardEffect.PaymentCost"/>).</param>
    /// <param name="stack">Optional construction-time stack — the resolution
    /// fallback for the legacy synchronous <see cref="IEffect.Execute()"/> path
    /// (no <see cref="ResolutionContext.Game"/>). The prod path resolves off the
    /// live <see cref="ResolutionContext.Game"/> stack instead, so this may be
    /// null.</param>
    /// <param name="spellsOnly">When true the ward only fires on opponent
    /// SPELLS (printed "a spell"); when false it fires on any opponent spell OR
    /// ability (printed "a spell or ability" — the default Ward wording).</param>
    public static TriggeredAbility Build(
        WardEffect ward,
        Majik.Core.Stack.Stack? stack = null,
        bool spellsOnly = false)
    {
        ArgumentNullException.ThrowIfNull(ward);

        var warded = ward.Source;
        var owner = warded.Owner ?? warded.Controller
            ?? throw new ArgumentException(
                "Ward source must have an owner or controller set before wiring its Ward trigger.",
                nameof(ward));

        // "this permanent becomes the target of a spell [or ability] an
        // opponent controls" — capture the targeting object so the resolution
        // effect can counter it.
        IStackObject? capturedSource = null;

        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            var controller = warded.Controller ?? owner;

            // (a) "an opponent controls" (CR 102.1) — the targeting object must
            //     be controlled by a player OTHER than the warded permanent's
            //     controller.
            var sourceController = e.StackObject.Controller;
            if (sourceController == null) return false;
            if (ReferenceEquals(sourceController, controller)) return false;

            // (a') "a spell" wording — restrict to spells when the printed text
            //      does not read "or ability".
            if (spellsOnly && e.StackObject is not Majik.Core.Spells.ISpell)
                return false;

            // (b) "this permanent becomes the target" (CR 702.21e) — the warded
            //     permanent itself must be among the chosen targets.
            foreach (var t in e.Targets)
            {
                if (TargetIsWardedPermanent(t, warded))
                {
                    capturedSource = e.StackObject;
                    return true;
                }
            }

            return false;
        });

        var effect = new Effect(
            $"{warded.Name} — Ward: counter that spell or ability unless its controller pays the ward cost",
            ctx =>
            {
                var source = capturedSource;
                capturedSource = null;

                // CR 608 — resolve through the LIVE stack handed to the trigger
                // via ResolutionContext.Game.Stack. The prod build path passes
                // no captured stack, so reading a construction-time stack here
                // would make the counter a silent no-op in real games — prefer
                // the live context stack, fall back to the captured stack only
                // for the legacy synchronous Execute() path.
                var liveStack = ctx.Game?.Stack ?? stack;
                if (source == null || liveStack == null)
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                // CR 608.2b — recheck the targeting object is still on the stack
                // at resolution. If it already left, there is nothing to counter.
                if (!liveStack.GetAll().Contains(source))
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                var caster = source.Controller;
                if (caster == null)
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                // CR 702.21f — "unless its controller pays [cost]." The bound
                // WardEffect charges its PaymentCost when the caster can (and,
                // in v1, auto-chooses to) pay. Resolve returns true when the
                // spell/ability should be COUNTERED (cost not paid).
                if (!ward.Resolve(caster))
                    return System.Threading.Tasks.ValueTask.CompletedTask; // paid → not countered.

                // CR 701.5b — counter the spell/ability. RemoveFromStack returns
                // false for an uncounterable spell (it stays put).
                if (!OracleSpellBinder.RemoveFromStack(liveStack, source))
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                // CR 701.5b — a countered SPELL goes to its owner's graveyard.
                // (Countered ABILITIES simply cease to exist — no zone move.)
                if (source is Majik.Core.Spells.ISpell spell && spell.Card is Card spellCard)
                {
                    spellCard.SetZone(ZoneType.Graveyard);
                }

                return System.Threading.Tasks.ValueTask.CompletedTask;
            });

        return new TriggeredAbility(
            source: warded,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });
    }

    /// <summary>
    /// CR 702.21e — is <paramref name="target"/> the warded permanent itself?
    /// True when the target's object is reference-equal to
    /// <paramref name="warded"/>.
    /// </summary>
    private static bool TargetIsWardedPermanent(ITarget target, Permanent warded)
    {
        if (target is not Target concrete) return false;

        return concrete.TargetType switch
        {
            TargetType.Permanent or TargetType.Card =>
                ReferenceEquals(concrete.TargetObject, warded),
            _ => false,
        };
    }
}
