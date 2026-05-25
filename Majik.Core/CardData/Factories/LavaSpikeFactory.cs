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
/// Named-card factory for Lava Spike (Champions of Kamigawa, {R}).
///
/// Sorcery — Arcane. Oracle text:
///   "Lava Spike deals 3 damage to target player or planeswalker."
///
/// ## Implementation
///
/// - <b>Sorcery</b> shape, mana cost {R} (CardDef DSL).
/// - <b>Arcane subtype</b> (CR 205.3k) stamped via
///   <see cref="CardDefBuilder.WithSubtype"/> so the Splice onto Arcane
///   primitive (<see cref="Majik.Core.Costs.SpliceOntoArcaneCost"/>) can
///   target Lava Spike when an Arcane spell is being cast — same posture
///   as <see cref="DesperateRitualFactory"/>.
/// - Single 1..1 target request, "target player or planeswalker"
///   (CR 115.1 — Lava Spike specifically excludes creature targets).
///   On resolution deals <see cref="Damage"/> (3) damage to the chosen
///   target via <see cref="Fx.DealDamageAny"/>, which routes
///   planeswalker damage as loyalty removal (CR 306.7).
///
/// V1 note: the engine's TargetRequest layer uses the description string
/// as a candidate-pool label (see <see cref="TargetRequest"/>); the
/// "no creatures" constraint is enforced at the agent/caller level (same
/// posture as Searing Blaze's player-or-planeswalker target).
/// </summary>
[CardName("Lava Spike")]
public static class LavaSpikeFactory
{
    public const string CardName = "Lava Spike";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape only (Sorcery, Arcane subtype).
    /// Damage body is supplied at cast time via
    /// <see cref="BuildSpellDefinition"/> (the runtime needs the caller's
    /// target resolver from the <see cref="GameContext"/>).</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost)
        .WithSubtype(CardSubtype.Arcane);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Lava Spike is cast.
    /// Single 1..1 "target player or planeswalker" request; on resolution
    /// deals <see cref="Damage"/> (3) damage through
    /// <see cref="Fx.DealDamageAny"/>.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player or planeswalker", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Lava Spike: 3 damage to player or planeswalker", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
