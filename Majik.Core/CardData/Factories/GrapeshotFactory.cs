using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grapeshot (Time Spiral, {1}{R}).
///
/// Sorcery. Oracle text:
///   "Grapeshot deals 1 damage to any target.
///    Storm (When you cast this spell, copy it for each spell cast before
///    it this turn. You may choose new targets for the copies.)"
///
/// ## Implemented (v1)
/// - Sorcery {1}{R} (Red) card shape with owner / controller wired.
/// - <b>1 damage to any target</b> — <see cref="BuildDefinition"/> declares
///   a single 1..1 "any target" <see cref="TargetRequest"/> and on resolve
///   deals 1 damage via <see cref="Fx.DealDamageAny"/> (Creature / Player /
///   Planeswalker / Battle funnel per CR 115.3 + CR 306.7).
/// - <b>Storm trigger (CR 702.40)</b> — built via
///   <see cref="StormHelper.Build"/>. Fires on this spell's
///   <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/> with
///   <c>activeZones = Stack</c> and copies the spell for each OTHER spell
///   the controller has cast this turn. Storm count is read from
///   <see cref="TurnState.SpellsCastByPlayer"/> at trigger-evaluation
///   time; copies are re-executions of the original spell's effect list
///   via <see cref="Majik.Core.Services.SpellCopier"/>. The observable
///   contract: N copies → N additional 1-damage pings against the
///   original chosen target. Same shape as
///   <see cref="TendrilsOfAgonyFactory"/> and
///   <see cref="BrainFreezeFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Retargeting copies</b>: CR 702.40a — inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; all copies hit the
///   original chosen target.
/// - <b>Copies as distinct stack objects</b>: inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; copies re-execute the
///   original effect list in place rather than pushing real
///   <see cref="Majik.Core.Spells.ISpell"/> stack items.
/// - <b>Action validator filtering</b>: target list is "any target"; the
///   agent's pick is honoured verbatim (no extra legality filtering yet).
/// </summary>
[CardName("Grapeshot")]
public static class GrapeshotFactory
{
    public const string CardName = "Grapeshot";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 1;

    /// <summary>
    /// Construct Grapeshot as a Sorcery card with the Storm trigger attached
    /// structurally (no stack / turn-state wired — shape-only). Use the
    /// <see cref="Create(Player, TriggerManager, Majik.Core.Stack.Stack, TurnState)"/>
    /// overload for fully-wired storm firing.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // Attach the storm trigger structurally — inspectable via
        // card.Abilities for shape tests; firing requires the bus-wired
        // overload below.
        var storm = StormHelper.Build(card, owner, stack: null, turnState: null);
        card.AddAbility(storm);

        return card;
    }

    /// <summary>
    /// Construct Grapeshot with full storm wiring. The storm trigger is
    /// registered with <paramref name="triggers"/>, reads spells-cast counts
    /// from <paramref name="turnState"/> at trigger-evaluation time, and
    /// creates copies on <paramref name="stack"/> via
    /// <see cref="Majik.Core.Services.SpellCopier.PushCopyOfTopSpell"/>.
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
    /// Build the "Grapeshot deals 1 damage to any target" SpellDefinition.
    /// Single 1..1 "any target" request; on resolution deals
    /// <see cref="Damage"/> (1) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/>.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen by
    /// the caster (Creature / Player / Planeswalker / Battle). Same shape
    /// as <see cref="ShockFactory.BuildSpellDefinition"/>.</param>
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
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = targetResolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline($"{CardName}: {Damage} damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
