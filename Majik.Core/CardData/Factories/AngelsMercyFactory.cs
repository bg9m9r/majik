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
/// Named-card factory for Angel's Mercy (Magic 2010, {2}{W}{W}).
///
/// Instant. Oracle text:
///   "You gain 7 life."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{W}{W}.
/// - Resolve effect via <see cref="BuildSpellDefinition"/>: the spell
///   controller gains 7 life (CR 119.3 — <see cref="Fx.GainLife"/>).
///   No targets — the gain is unconditional and applies to the caster
///   only (CR 609.3 — "you" refers to the spell's controller).
///
/// ## Notes
/// - No target selection required: "you gain 7 life" is a pure controller-
///   affecting effect with no target (CR 114.1 — "target" keyword absent).
///   <see cref="SpellDefinition.TargetRequests"/> is empty.
/// - Life gain is routed through <see cref="Fx.GainLife"/> which calls
///   <see cref="Player.GainLife"/>, honouring the
///   <see cref="Majik.Core.Effects.LifeGainIntent"/> replacement bus so
///   cards like Teferi's Protection or Platinum Emperion interact
///   correctly (CR 119.6).
/// </summary>
[CardName("Angel's Mercy")]
public static class AngelsMercyFactory
{
    public const string CardName = "Angel's Mercy";
    public const string PrintedManaCost = "{2}{W}{W}";

    /// <summary>The amount of life the caster gains on resolution (CR 119.3).</summary>
    public const int LifeGainAmount = 7;

    /// <summary>CardDef DSL — Instant shape only. The resolve closure is
    /// produced by <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    /// <summary>
    /// Construct Angel's Mercy as an <see cref="Instant"/> owned by
    /// <paramref name="owner"/>. Shape-only; the resolve closure is not
    /// wired on this path (mirrors FaithsRewardFactory / ReadTheBonesFactory).
    /// </summary>
    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Angel's Mercy.
    /// No target requests — the spell affects only the caster unconditionally.
    /// On resolution the caster gains <see cref="LifeGainAmount"/> (7) life
    /// (CR 119.3, <see cref="Fx.GainLife"/>).
    /// </summary>
    /// <param name="caster">Spell controller — gains 7 life on resolution.</param>
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
                        // CR 119.3 — controller gains 7 life. Routed through
                        // Fx.GainLife so the LifeGainIntent replacement bus
                        // is honoured (CR 119.6).
                        Fx.GainLife(caster, LifeGainAmount);
                    }),
            });
    }
}
