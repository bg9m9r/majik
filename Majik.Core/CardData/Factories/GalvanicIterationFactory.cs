using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Galvanic Iteration (Innistrad: Midnight Hunt,
/// {1}{R}).
///
/// Instant. Oracle text:
///   "When you next cast an instant or sorcery spell this turn, copy that
///    spell. You may choose new targets for the copy.
///    Flashback {U}{R}"
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{R} (CardDef DSL).
/// - <b>Delayed "next-spell copy" rider (CR 603.7 / 603.8)</b>: at
///   resolution the controller gets a one-shot
///   <see cref="DelayedTriggeredAbility"/> registered against the
///   <see cref="TriggerManager"/>. The trigger condition matches the
///   controller's NEXT <see cref="SpellCastEvent"/> whose spell card has
///   <see cref="CardType.Instant"/> or <see cref="CardType.Sorcery"/>; on
///   match, <see cref="SpellCopier.PushCopyOfTopSpell"/> re-executes the
///   captured spell's effect list (CR 707.10 — copy primitive, lossy v1
///   stub). Mirrors the binding produced by
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Bespoke.NextSpellCopyTemplate"/>.
///   Both the printed-cost cast and the flashback cast share this resolve
///   shape — flashback's post-resolve exile (CR 702.34b) is handled by
///   <see cref="FlashbackAlternativeCost.OnResolved"/>, not here.
/// - <b>Printed Flashback {U}{R}</b> alt-cost: produced via
///   <see cref="BuildFlashbackCost"/> using
///   <see cref="FlashbackOracleParser"/> on <see cref="OracleText"/> so the
///   data-driven binder path and this factory agree on cost shape. Callers
///   thread the returned <see cref="FlashbackAlternativeCost"/> through
///   <see cref="SpellCastFlow.CastAsync"/> when casting from graveyard.
///
/// ## Deferred (v1 gaps — inherited from <see cref="SpellCopier"/>)
/// - <b>"You may choose new targets for the copy"</b> (CR 707.10a): the v1
///   copier reuses the original spell's targets verbatim. No re-target
///   prompt is surfaced. Tracked on <see cref="SpellCopier"/>.
/// - <b>Copy as a distinct stack object</b>: the v1 copier doesn't push a
///   new <see cref="Majik.Core.Stack.IStackObject"/> — it re-runs the
///   original's effect list in place. Subscribers to
///   <see cref="StackObjectAddedEvent"/> or callers counting
///   <see cref="Majik.Core.Stack.Stack.Count"/> won't see the copy. Same
///   gap as Doublecast / Howl of the Horde under the same primitive.
/// - <b>"this turn" expiry on the delayed trigger</b>: the delayed trigger
///   is one-shot and self-unregisters on first match. If no instant or
///   sorcery is cast for the rest of the turn the registration silently
///   lingers; an end-of-turn cleanup hook is deferred. Practical effect of
///   "fires only on the NEXT spell" is preserved.
/// - <b>Self-copy guard</b>: the bound condition excludes the source
///   Galvanic Iteration spell only by the temporal "next cast after this
///   one resolves" semantic — Galvanic Iteration has already left the
///   stack by the time the delayed trigger is registered (its effect runs
///   at resolution, after the spell leaves the stack), so a Galvanic
///   Iteration cast can't be its own "next spell". Two Galvanic Iterations
///   in a row chain correctly (second one is the next instant cast → its
///   own copy fires on whatever spell follows).
/// </summary>
[CardName("Galvanic Iteration")]
public static class GalvanicIterationFactory
{
    public const string CardName = "Galvanic Iteration";
    public const string PrintedManaCost = "{1}{R}";
    public const string FlashbackManaCost = "{U}{R}";

    /// <summary>
    /// Oracle text — fed to <see cref="FlashbackOracleParser"/> so this
    /// factory and the data-driven binder agree on the flashback cost
    /// shape. Mirrors <see cref="FaithlessLootingFactory.OracleText"/>'s
    /// posture.
    /// </summary>
    public const string OracleText =
        "When you next cast an instant or sorcery spell this turn, copy that spell. " +
        "You may choose new targets for the copy.\n" +
        "Flashback {U}{R}";

    /// <summary>CardDef DSL — card shape only. The delayed-copy body is
    /// built via <see cref="BuildResolveEffect"/>; flashback alt-cost via
    /// <see cref="BuildFlashbackCost"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time effect for Galvanic Iteration. At resolution
    /// the controller registers a one-shot <see cref="DelayedTriggeredAbility"/>
    /// that fires on their next instant/sorcery <see cref="SpellCastEvent"/>;
    /// on fire, <see cref="SpellCopier.PushCopyOfTopSpell"/> re-executes the
    /// captured spell's effects (CR 707.10 — v1 lossy stub).
    /// </summary>
    /// <param name="caster">Controller of the Galvanic Iteration that is
    /// resolving — the delayed trigger's "you cast" predicate gates on
    /// this player.</param>
    /// <param name="triggers">Trigger manager that owns delayed
    /// registrations. Required — without it the rider can't subscribe and
    /// the body no-ops (shape-only path).</param>
    /// <param name="stack">Active stack — forwarded to
    /// <see cref="SpellCopier.PushCopyOfTopSpell"/> for the future
    /// real-stack-object implementation. v1 ignores it.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        TriggerManager? triggers,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: register delayed 'copy next instant/sorcery you cast' trigger",
                () =>
                {
                    if (triggers == null || stack == null) return;

                    // The triggering spell is captured into a closure by the
                    // condition — same plumbing as NextSpellCopyTemplate. The
                    // condition runs at event-publish time (TriggerManager
                    // .EvaluateTriggers), which is before the queued effect
                    // resolves, so the capture is well-defined.
                    ISpell? captured = null;

                    var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
                    {
                        var spell = e.Spell;
                        if (!ReferenceEquals(spell.Controller, caster)) return false;
                        var card = spell.Card;
                        if (card is null) return false;
                        var isInstantOrSorcery =
                            card.HasType(CardType.Instant) || card.HasType(CardType.Sorcery);
                        if (!isInstantOrSorcery) return false;
                        captured = spell;
                        return true;
                    });

                    var copyEffect = new Effect(
                        $"{CardName}: copy captured spell",
                        () =>
                        {
                            if (captured is null) return;
                            SpellCopier.PushCopyOfTopSpell(stack, captured);
                        });

                    var delayed = new DelayedTriggeredAbility(
                        source: caster,
                        controller: caster,
                        condition: condition,
                        effects: new IEffect[] { copyEffect });
                    triggers.RegisterDelayed(delayed);
                }),
        };
    }

    /// <summary>
    /// Build the printed Flashback {U}{R} alternative cost by routing
    /// <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>.
    /// Going through the parser keeps the named-factory path and the
    /// data-driven oracle binder path agreeing on shape. Post-resolve exile
    /// (CR 702.34b) is handled by <see cref="FlashbackAlternativeCost.OnResolved"/>.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = Majik.Core.CardData.FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                $"FlashbackOracleParser failed to parse {CardName}'s oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}
