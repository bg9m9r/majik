using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sacred Nectar (Exodus, {1}{W}).
///
/// Sorcery. Oracle text:
///   "You gain 4 life."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{W}, mana value 2.
/// - Resolve effect via <see cref="BuildSpellDefinition"/>: the spell
///   controller gains 4 life (CR 119.3 — <see cref="Fx.GainLife"/>).
///   No targets — the gain is unconditional and applies to the caster
///   only (CR 609.3 — "you" refers to the spell's controller).
///
/// ## Notes
/// - No target selection required: "you gain 4 life" is a pure controller-
///   affecting effect with no target (CR 114.1 — "target" keyword absent).
///   <see cref="SpellDefinition.TargetRequests"/> is empty.
/// - Life gain is routed through <see cref="Fx.GainLife"/> which calls
///   <see cref="Player.GainLife"/>, honouring the
///   <see cref="Majik.Core.Effects.LifeGainIntent"/> replacement bus so
///   cards like Teferi's Protection or Platinum Emperion interact
///   correctly (CR 119.6).
/// </summary>
[CardName("Sacred Nectar")]
public static class SacredNectarFactory
{
    public const string CardName = "Sacred Nectar";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>The amount of life the caster gains on resolution (CR 119.3).</summary>
    public const int LifeGainAmount = 4;

    /// <summary>CardDef DSL — Sorcery shape only. The resolve closure is
    /// produced by <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    /// <summary>
    /// Construct Sacred Nectar as a <see cref="Sorcery"/> owned by
    /// <paramref name="owner"/>. Shape-only; the resolve closure is not
    /// wired on this path (mirrors AngelsMercyFactory).
    /// </summary>
    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Sacred Nectar.
    /// No target requests — the spell affects only the caster unconditionally.
    /// On resolution the caster gains <see cref="LifeGainAmount"/> (4) life
    /// (CR 119.3, <see cref="Fx.GainLife"/>).
    /// </summary>
    /// <param name="caster">Spell controller — gains 4 life on resolution.</param>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: you gain {LifeGainAmount} life.",
                    () =>
                    {
                        // CR 119.3 — controller gains 4 life. Routed through
                        // Fx.GainLife so the LifeGainIntent replacement bus
                        // is honoured (CR 119.6).
                        Fx.GainLife(caster, LifeGainAmount);
                    }),
            });
    }
}
