using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brain Freeze (Scourge, {U}{U}).
///
/// Instant. Oracle text:
///   "Target player mills three cards.
///    Storm (When you cast this spell, copy it for each spell cast before
///    it this turn. You may choose new targets for the copies.)"
///
/// ## Implemented (v1)
/// - Instant {U}{U} (Blue) card shape with owner / controller wired.
/// - <b>Mill 3 target player</b> — <see cref="BuildDefinition"/> declares
///   a single "target player" <see cref="TargetRequest"/> and on resolve
///   mills 3 cards from the chosen player via
///   <see cref="MillAction.Apply"/>. Matches the
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Library.LibrarySpellFactory"/>
///   <c>MillTargetSpell</c> shape used by template-bound mill spells.
/// - <b>Storm trigger (CR 702.40)</b> — built via
///   <see cref="StormHelper.Build"/>. Fires on this spell's
///   <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/> with
///   <c>activeZones = Stack</c> and copies the spell for each OTHER spell
///   the controller has cast this turn. Storm count is read from
///   <see cref="TurnState.SpellsCastByPlayer"/> at trigger-evaluation
///   time; copies are re-executions of the original spell's effect list
///   via <see cref="Majik.Core.Services.SpellCopier"/>. The observable
///   contract: N copies → N additional mills against the original chosen
///   target. CR 702.40a's "you may choose new targets for the copies"
///   rider is deferred (see <see cref="StormHelper"/> + <see cref="Majik.Core.Services.SpellCopier"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Retargeting copies</b>: CR 702.40a — inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; all copies hit the
///   original chosen target.
/// - <b>Copies as distinct stack objects</b>: inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; copies re-execute the
///   original effect list in place rather than pushing real
///   <see cref="Majik.Core.Spells.ISpell"/> stack items.
/// - <b>Action validator filtering</b>: target list is "target player";
///   the agent's pick is honoured verbatim (no extra filtering yet).
/// </summary>
public static class BrainFreezeFactory
{
    public const string CardName = "Brain Freeze";
    public const string PrintedManaCost = "{U}{U}";
    public const int MillCount = 3;

    /// <summary>
    /// Construct Brain Freeze as an Instant card with no Storm trigger
    /// registered. Suitable for shape / dispatcher tests. Use the
    /// <see cref="Create(Player, TriggerManager, Majik.Core.Stack.Stack, TurnState)"/>
    /// overload for fully-wired storm firing.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // Attach the storm trigger structurally (no stack / turn-state
        // wired — shape-only). The trigger is still inspectable via
        // card.Abilities for shape tests; firing requires the
        // bus-wired overload.
        var storm = StormHelper.Build(card, owner, stack: null, turnState: null);
        card.AddAbility(storm);

        return card;
    }

    /// <summary>
    /// Construct Brain Freeze with full storm wiring. The storm trigger
    /// is registered with <paramref name="triggers"/>, reads spells-cast
    /// counts from <paramref name="turnState"/> at trigger-evaluation
    /// time, and creates copies on <paramref name="stack"/> via
    /// <see cref="Majik.Core.Services.SpellCopier.PushCopyOfTopSpell"/>.
    /// </summary>
    public static Instant Create(
        Player owner,
        TriggerManager triggers,
        Majik.Core.Stack.Stack stack,
        TurnState turnState)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(turnState);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        var storm = StormHelper.Build(card, owner, stack, turnState);
        card.AddAbility(storm);
        triggers.RegisterTriggeredAbility(storm);

        return card;
    }

    /// <summary>
    /// Build the "target player mills three cards" SpellDefinition. The
    /// chosen target token is resolved via <paramref name="targetResolver"/>
    /// (typically the agent's chosen <see cref="Player"/> passed through
    /// verbatim — same pattern as Aether Gust / Force of Negation).
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen
    /// by the caster (expected to yield a <see cref="Player"/>). When the
    /// resolver returns anything that isn't a <see cref="Player"/> the
    /// effect no-ops per CR 608.2b (illegal target at resolution).</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"Brain Freeze — target player mills {MillCount} cards",
                        () =>
                        {
                            if (resolved is not Player target) return;
                            MillAction.Apply(target, MillCount);
                        }),
                };
            });
    }
}
