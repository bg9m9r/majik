using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hidetsugu's Second Rite (Champions of Kamigawa
/// / NEO reprint, {2}{R}).
///
/// Sorcery. Oracle text:
///   "If target opponent's life total is exactly 10, Hidetsugu's Second
///    Rite deals 10 damage to them."
///
/// ## Implementation
///
/// Single 1..1 "target opponent" <see cref="TargetRequest"/>. On
/// resolution the chosen player's <see cref="Player.LifeTotal"/> is
/// sampled and compared against the printed threshold of 10. If the
/// equality holds the spell deals 10 damage via
/// <see cref="OracleSpellBinder.DealDamage"/> (routes through
/// <see cref="Player.LoseLife"/> for Player targets); otherwise the
/// resolve effect is a clean no-op (CR 608.2c — the "if ... " clause is
/// a resolve-time condition check that simply does nothing on failure,
/// not an illegal-target check).
///
/// Mirrors the structural pattern used by
/// <see cref="TendrilsOfAgonyFactory.BuildDefinition"/>: target-opponent
/// request + resolver-driven token unwrap (illegal target → resolver
/// returns a non-<see cref="Player"/>, the effect no-ops per CR 608.2b).
/// </summary>
[CardName("Hidetsugu's Second Rite")]
public static class HidetsugusSecondRiteFactory
{
    public const string CardName = "Hidetsugu's Second Rite";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>The printed life-total threshold for the conditional
    /// damage clause (CR 608.2c — checked at resolution).</summary>
    public const int LifeTrigger = 10;

    /// <summary>The amount of damage dealt when the life-total check
    /// passes.</summary>
    public const int DamageAmount = 10;

    /// <summary>CardDef DSL — card shape only. Conditional damage body
    /// lives in <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Hidetsugu's Second Rite
    /// uses when cast. Single 1..1 "target opponent" request; on
    /// resolution, if the chosen player's life total equals
    /// <see cref="LifeTrigger"/>, the spell deals
    /// <see cref="DamageAmount"/> damage to that player.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen
    /// by the caster (expected to yield a <see cref="Player"/>). When
    /// the resolver returns anything that isn't a
    /// <see cref="Player"/> the effect no-ops per CR 608.2b (illegal
    /// target at resolution).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver)
    {
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
            EffectFactory: chosen =>
            {
                var resolved = targetResolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: if target opponent's life is exactly {LifeTrigger}, deal {DamageAmount} damage",
                        () =>
                        {
                            // CR 608.2b — illegal target: clean no-op.
                            if (resolved is not Player target) return;

                            // CR 608.2c — printed "if ... " resolve-time
                            // condition. Equality check only; any other
                            // life total resolves with no effect.
                            if (target.LifeTotal != LifeTrigger) return;

                            OracleSpellBinder.DealDamage(target, DamageAmount);
                        }),
                };
            });
    }
}
