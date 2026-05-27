using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ulcerate (Journey into Nyx, {B}).
///
/// Instant. Oracle text:
///   "Target creature gets -3/-3 until end of turn. You lose 3 life."
///
/// ## Implemented (v1)
/// - Instant {B}, black.
/// - <see cref="DisfigureFactory"/>-shape -X/-X: a single 1..1 "target
///   creature" request; on resolve register a
///   <see cref="PumpUntilEndOfTurnEffect"/>(-3, -3) on the target's
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires EOT). When
///   ActiveEffects is null (shape tests) the registration is skipped.
/// - <b>You lose 3 life</b> (CR 119.3) — applied to the caster as part of
///   the same resolution, but ONLY when the target is legal: a single-target
///   spell whose target is illegal at resolution does nothing at all
///   (CR 608.2b), so the life cost does not apply (parity with the
///   Thoughtseize fizzle posture).
/// </summary>
[CardName("Ulcerate")]
public static class UlcerateFactory
{
    public const string CardName = "Ulcerate";
    public const string PrintedManaCost = "{B}";
    public const int LifeLoss = 3;

    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "target creature gets -3/-3 until end of turn; you lose 3
    /// life" SpellDefinition.
    /// </summary>
    /// <param name="caster">Cast-time controller — pays the 3-life cost.</param>
    public static SpellDefinition BuildDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Ulcerate — target creature gets -3/-3 until end of turn; you lose 3 life",
                        () =>
                        {
                            // CR 608.2b — illegal target → the spell does
                            // nothing, including the life cost.
                            if (raw is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            if (target.ActiveEffects != null)
                            {
                                target.ActiveEffects.Register(
                                    new PumpUntilEndOfTurnEffect(target, -LifeLoss, -LifeLoss));
                            }

                            // CR 119.3 — "You lose 3 life."
                            caster.LoseLife(LifeLoss);
                        }),
                };
            });
    }
}
