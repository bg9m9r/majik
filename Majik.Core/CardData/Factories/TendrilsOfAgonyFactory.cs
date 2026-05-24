using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tendrils of Agony (Scourge, {2}{B}{B}).
///
/// Sorcery. Oracle text:
///   "Target opponent loses 2 life and you gain 2 life.
///    Storm (When you cast this spell, copy it for each spell cast before
///    it this turn. You may choose new targets for the copies.)"
///
/// ## Implemented (v1)
/// - Sorcery {2}{B}{B} (Black) card shape with owner / controller wired.
/// - <b>Life swing</b> — <see cref="BuildDefinition"/> declares a single
///   "target opponent" <see cref="TargetRequest"/> and on resolve the
///   chosen player loses 2 life (<see cref="Player.LoseLife"/>) and the
///   controller gains 2 life (<see cref="Player.GainLife"/>). Mirrors
///   <see cref="BrainFreezeFactory.BuildDefinition"/>'s target-resolver
///   plumbing — illegal-target picks (resolver returns a non-Player)
///   no-op per CR 608.2b.
/// - <b>Storm trigger (CR 702.40)</b> — built via
///   <see cref="StormHelper.Build"/>. Fires on this spell's
///   <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/> with
///   <c>activeZones = Stack</c> and copies the spell for each OTHER spell
///   the controller has cast this turn. Storm count is read from
///   <see cref="TurnState.SpellsCastByPlayer"/> at trigger-evaluation
///   time; copies are re-executions of the original spell's effect list
///   via <see cref="Majik.Core.Services.SpellCopier"/>. The observable
///   contract: N copies → N additional 2-life swings against the original
///   chosen opponent. CR 702.40a's "you may choose new targets for the
///   copies" rider is deferred (see <see cref="StormHelper"/> +
///   <see cref="Majik.Core.Services.SpellCopier"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Retargeting copies</b>: CR 702.40a — inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; all copies hit the
///   original chosen opponent.
/// - <b>Copies as distinct stack objects</b>: inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; copies re-execute the
///   original effect list in place rather than pushing real
///   <see cref="Majik.Core.Spells.ISpell"/> stack items.
/// - <b>Action validator filtering</b>: target list is "target opponent";
///   the agent's pick is honoured verbatim (no extra opponent-only
///   filtering yet — same gap as <see cref="GriefFactory"/>).
/// </summary>
[CardName("Tendrils of Agony")]
public static class TendrilsOfAgonyFactory
{
    public const string CardName = "Tendrils of Agony";
    public const string PrintedManaCost = "{2}{B}{B}";
    public const int LifeSwing = 2;

    /// <summary>
    /// Construct Tendrils of Agony as a Sorcery card with no Storm trigger
    /// registered. Suitable for shape / dispatcher tests. Use the
    /// <see cref="Create(Player, TriggerManager, Majik.Core.Stack.Stack, TurnState)"/>
    /// overload for fully-wired storm firing.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
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
    /// Construct Tendrils of Agony with full storm wiring. The storm
    /// trigger is registered with <paramref name="triggers"/>, reads
    /// spells-cast counts from <paramref name="turnState"/> at trigger-
    /// evaluation time, and creates copies on <paramref name="stack"/>
    /// via <see cref="Majik.Core.Services.SpellCopier.PushCopyOfTopSpell"/>.
    /// </summary>
    public static Sorcery Create(
        Player owner,
        TriggerManager triggers,
        Majik.Core.Stack.Stack stack,
        TurnState turnState)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(turnState);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        var storm = StormHelper.Build(card, owner, stack, turnState);
        card.AddAbility(storm);
        triggers.RegisterTriggeredAbility(storm);

        return card;
    }

    /// <summary>
    /// Build the "target opponent loses 2 life and you gain 2 life"
    /// SpellDefinition. The chosen target token is resolved via
    /// <paramref name="targetResolver"/> (typically the agent's chosen
    /// <see cref="Player"/> passed through verbatim — same pattern as
    /// Brain Freeze / Grief).
    /// </summary>
    /// <param name="controller">The Tendrils caster — the player who
    /// gains 2 life on resolution (CR 119.3).</param>
    /// <param name="targetResolver">Resolves the raw target token chosen
    /// by the caster (expected to yield a <see cref="Player"/>). When the
    /// resolver returns anything that isn't a <see cref="Player"/> the
    /// life-loss half no-ops per CR 608.2b (illegal target at resolution);
    /// the controller's life-gain half still fires (no target — CR 119.3
    /// "do as much as possible").</param>
    public static SpellDefinition BuildDefinition(
        Player controller,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
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
                        $"{CardName} — target opponent loses {LifeSwing} life and you gain {LifeSwing} life",
                        () =>
                        {
                            // CR 608.2b — illegal target: life-loss half
                            // does nothing. Controller's life-gain half
                            // is not target-gated and still fires.
                            if (resolved is Player target)
                            {
                                target.LoseLife(LifeSwing);
                            }
                            controller.GainLife(LifeSwing);
                        }),
                };
            });
    }
}
