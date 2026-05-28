using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grapeshot (Time Spiral / many reprints, {1}{R}).
///
/// Sorcery. Oracle text:
///   "Grapeshot deals 1 damage to any target.
///    Storm (When you cast this spell, copy it for each spell cast before
///    it this turn. You may choose new targets for the copies.)"
///
/// ## Implemented (v1)
/// - Sorcery {1}{R} (Red) card shape with owner / controller wired.
/// - <b>1 damage to any target</b> — <see cref="BuildSpellDefinition"/>
///   declares a single "any target" <see cref="TargetRequest"/> and on
///   resolve deals 1 damage through <see cref="Fx.DealDamageAny"/>, which
///   routes creature / player / planeswalker / battle targets correctly
///   (CR 115.3). Same shape as <see cref="LightningStrikeFactory"/> / <see cref="ShockFactory"/>.
/// - <b>Storm trigger (CR 702.39)</b> — built via
///   <see cref="StormHelper.Build"/>. Fires on this spell's
///   <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/> with
///   <c>activeZones = Stack</c> and copies the spell for each OTHER spell
///   the controller has cast this turn. Storm count is read from
///   <see cref="TurnState.SpellsCastByPlayer"/> at trigger-evaluation time;
///   copies are re-executions of the original spell's effect list via
///   <see cref="Majik.Core.Services.SpellCopier"/>. Identical storm
///   infrastructure to <see cref="BrainFreezeFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Retargeting copies</b>: CR 702.39a — inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; all copies hit the
///   original chosen target.
/// - <b>Copies as distinct stack objects</b>: inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; copies re-execute the
///   original effect list in place rather than pushing real
///   <see cref="Majik.Core.Spells.ISpell"/> stack items.
/// </summary>
[CardName("Grapeshot")]
public static class GrapeshotFactory
{
    public const string CardName = "Grapeshot";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 1;

    /// <summary>
    /// Construct Grapeshot as a Sorcery card with the Storm trigger
    /// attached (shape-only — no stack / turn-state wired). Suitable for
    /// identity / dispatcher / structural Storm shape tests. Use the
    /// <see cref="Create(Player, TriggerManager, Majik.Core.Stack.Stack, TurnState)"/>
    /// overload for fully-wired storm firing.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // Attach storm trigger structurally (no stack / turn-state wired).
        // Trigger is inspectable via card.Abilities for shape tests.
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
    /// The chosen target token is resolved via <paramref name="resolver"/>
    /// and routed through <see cref="Fx.DealDamageAny"/> so creature /
    /// player / planeswalker / battle targets all resolve correctly
    /// (CR 115.3). When the resolver returns an illegal target the effect
    /// no-ops per CR 608.2b.
    /// </summary>
    /// <param name="resolver">Resolves the raw target token chosen by the
    /// caster (same pattern as <see cref="LightningStrikeFactory"/>).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline($"Grapeshot: {Damage} damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
