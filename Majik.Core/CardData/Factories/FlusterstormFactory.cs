using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flusterstorm (Commander 2011, {U}).
///
/// Instant. Oracle text:
///   "Counter target instant or sorcery spell unless its controller pays
///    {1}.
///    Storm (When you cast this spell, copy it for each spell cast before
///    it this turn. You may choose new targets for the copies.)"
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - 1..1 "target instant or sorcery spell" <see cref="TargetRequest"/>.
///   At resolution the engine re-verifies the target type (CR 608.2b);
///   then auto-pays {1} from the target controller's mana pool if able
///   (v1 auto-pay posture — same as <see cref="ManaLeakFactory"/> /
///   <see cref="MysticalDisputeFactory"/>). If the controller can pay,
///   the counter no-ops. Otherwise the target is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> + zone-move to
///   graveyard (CR 701.5; CR 701.5b — uncounterable check baked in).
/// - <b>Storm (CR 702.40)</b> — built via <see cref="StormHelper.Build"/>.
///   Fires on this spell's <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/>,
///   counts the controller's spells cast this turn (minus this one), and
///   pushes that many copies through <see cref="Majik.Core.Services.SpellCopier"/>.
///   Storm-count inheritance + retarget gap mirror
///   <see cref="BrainFreezeFactory"/> / <see cref="TendrilsOfAgonyFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Real "do you want to pay {1}?" agent prompt</b> — same queue as
///   Daze / Mana Leak / Mystical Dispute. v1 is deterministic: "pay if able."
/// - <b>Retargeting copies</b> (CR 702.40a) — inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; copies hit the original
///   chosen target.
/// - <b>Copies as distinct stack objects</b> — inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; copies re-execute the
///   original effect list in place rather than pushing real
///   <see cref="ISpell"/> stack items. Observable contract holds: N copies
///   → N additional counter attempts against the same target. Once the
///   first copy counters the target, subsequent copies' RemoveFromStack
///   short-circuits and no-ops cleanly.
/// </summary>
[CardName("Flusterstorm")]
public static class FlusterstormFactory
{
    public const string CardName = "Flusterstorm";
    public const string PrintedManaCost = "{U}";

    /// <summary>Pay-or-counter rider (CR 118.4 — "unless its controller pays {1}").</summary>
    public const int UnlessPayGeneric = 1;

    /// <summary>
    /// Construct Flusterstorm as an Instant card with no live Storm wiring
    /// (shape / dispatcher tests). Use the
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
        // wired — shape-only). Inspectable via card.Abilities for shape
        // tests; firing requires the bus-wired overload below.
        var storm = StormHelper.Build(card, owner, stack: null, turnState: null);
        card.AddAbility(storm);

        return card;
    }

    /// <summary>
    /// Construct Flusterstorm with full storm wiring. The storm trigger is
    /// registered with <paramref name="triggers"/>, reads spells-cast counts
    /// from <paramref name="turnState"/> at trigger-evaluation time, and
    /// creates copies on <paramref name="stack"/> via
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
    /// Build the "counter target instant or sorcery spell unless its
    /// controller pays {1}" SpellDefinition. CR 118.4 — the target's
    /// controller may pay {1} to prevent the counter; v1 auto-pays when
    /// able. CR 608.2b — if the chosen target is neither an instant nor a
    /// sorcery spell at resolution time (uncommon — type changes on the
    /// stack), the effect does nothing.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen by
    /// the caster to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var unlessCost = ManaCost.Zero.AddGenericCost(UnlessPayGeneric);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target instant or sorcery spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "Flusterstorm — counter target instant or sorcery spell unless its controller pays {1}",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;

                            // CR 608.2b — defensive type gate.
                            var isInstantOrSorcery =
                                spell.Card.HasType(CardType.Instant) ||
                                spell.Card.HasType(CardType.Sorcery);
                            if (!isInstantOrSorcery) return;

                            // CR 118.4 — target's controller may pay {1} to
                            // prevent the counter. v1 auto-pays when able.
                            if (spell.Controller is not null
                                && spell.Controller.PayMana(unlessCost))
                            {
                                return;
                            }

                            // CR 701.5 / 701.5b — counter, respecting
                            // uncounterability.
                            if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
                            spell.Card.SetZone(ZoneType.Graveyard);
                        }),
                };
            });
    }
}
