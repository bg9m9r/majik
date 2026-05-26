using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spell Pierce (Zendikar / many reprints, {U}).
///
/// Instant. Oracle text:
///   "Counter target noncreature spell unless its controller pays {2}."
///
/// The cheap soft counter — {U} for a "pay {2} or be countered" tax on
/// any noncreature spell. Mirrors <see cref="ManaLeakFactory"/>'s
/// "counter unless pay" shape with the unless-cost dropped from {3} to
/// {2} and the noncreature gate added (same posture as
/// <see cref="NegateFactory"/>).
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - <b>Counter target noncreature spell unless its controller pays {2}</b>
///   — at resolution, if the target spell's controller has {2} available
///   it is spent automatically and the counter no-ops (v1 auto-pay
///   posture — same as Daze / Mana Tithe / Mana Leak). If not, the spell
///   is countered (CR 701.5) and its card moves to the graveyard.
/// - Noncreature gate: at resolution, if the target spell has type
///   Creature (<see cref="CardType.Creature"/>) the effect does nothing
///   (CR 608.2b — illegal-target check). Same defensive posture as Negate.
///
/// ## Deferred
/// - Real "do you want to pay {2}?" agent prompt — same queue as Daze /
///   Stubborn Denial / Mana Leak. v1 is deterministic: "pay if able."
/// </summary>
[CardName("Spell Pierce")]
public static class SpellPierceFactory
{
    public const string CardName = "Spell Pierce";
    public const string PrintedManaCost = "{U}";

    /// <summary>CardDef DSL — card shape only. The counter-unless-pay-{2}
    /// SpellDefinition is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target noncreature spell unless its controller
    /// pays {2}" SpellDefinition. Two gates apply at resolve:
    /// (1) CR 608.2b — if the target is a creature spell, do nothing;
    /// (2) controller-pays — if the spell's controller can pay {2}, they
    /// pay automatically and the counter no-ops (v1 auto-pay).
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live
    /// engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var unlessCost = ManaCost.Zero.AddGenericCost(2);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target noncreature spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Spell Pierce — counter target noncreature spell unless its controller pays {2}", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — creature spell at resolution → illegal
                        // target; the effect does nothing for it.
                        if (spell.Card.HasType(CardType.Creature)) return;

                        // CR 118.4 — controller may pay {2}; v1 auto-pays
                        // when able (same posture as Mana Leak / Daze).
                        if (spell.Controller is not null
                            && spell.Controller.PayMana(unlessCost))
                        {
                            // Controller paid {2} — spell is NOT countered.
                            return;
                        }

                        // Controller couldn't pay — counter the spell.
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
