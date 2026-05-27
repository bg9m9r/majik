using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Magma Spray (Amonkhet / Scourge / Ixalan, {R}).
///
/// Instant. Oracle text:
///   "Magma Spray deals 2 damage to target creature. If that creature
///    would die this turn, exile it instead."
///
/// ## Implementation
///
/// Near-identical shape to <see cref="AngerOfTheGodsFactory"/> but scoped
/// to a single targeted creature rather than a sweep:
///
/// 1. <b>Damage</b>: deal <see cref="Damage"/> (2) to the targeted creature
///    via <see cref="Fx.DealDamage"/> (CR 119.2 — non-combat damage is
///    marked; SBA at CR 704.5f moves lethal-damaged creatures to
///    graveyards on the next pass).
/// 2. <b>Exile rider</b>: if a <see cref="ReplacementBus"/> is supplied,
///    register an <see cref="AngerOfTheGodsExileInsteadReplacement"/>
///    scoped to a single-element <see cref="HashSet{Creature}"/> containing
///    only the targeted creature (CR 700.3 — "that creature" refers back
///    to the specific creature this spell targeted). The replacement is
///    <see cref="IEndOfTurnExpirable"/>; the bus's
///    <see cref="ReplacementBus.ExpireEndOfTurn"/> sweep drops it at
///    end of turn (CR 514.2).
///
/// The factory reuses <see cref="AngerOfTheGodsExileInsteadReplacement"/>
/// directly — the replacement is parameterised by a
/// <see cref="HashSet{Creature}"/>, so scoping it to a single creature
/// (Magma Spray) or a full sweep set (Anger of the Gods) requires no new
/// class.
///
/// ## CR notes
/// - CR 700.3 — "that creature" / "dealt damage this way" is back-reference
///   to the specific event of this spell's resolution.
/// - CR 119.2 — non-combat damage is marked on the permanent.
/// - CR 704.5f — SBAs move creatures with lethal damage to the graveyard;
///   the registered replacement catches those ZoneMoveIntents.
/// - CR 514.2 — end-of-turn cleanup expires the IEndOfTurnExpirable rider.
/// </summary>
[CardName("Magma Spray")]
public static class MagmaSprayFactory
{
    public const string CardName = "Magma Spray";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 2;

    /// <summary>CardDef DSL — card shape only. Damage body is supplied at
    /// cast time via <see cref="BuildSpellDefinition"/> (the runtime needs
    /// the caller's target resolver, which lives on the
    /// <see cref="GameContext"/>).</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Magma Spray is cast.
    ///
    /// Single 1..1 "target creature" request; on resolution:
    ///   1. Deals <see cref="Damage"/> (2) damage to the targeted creature.
    ///   2. If <paramref name="replacements"/> is non-null, registers an
    ///      EOT-expirable <see cref="AngerOfTheGodsExileInsteadReplacement"/>
    ///      scoped to the single targeted creature so its
    ///      battlefield→graveyard move is rewritten to exile this turn
    ///      (CR 700.3 / CR 514.2).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game object).</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> on
    /// which to register the exile-instead rider. When <c>null</c>, the
    /// rider is skipped (damage still applies — useful for simple shape
    /// tests).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var rawTarget = resolver(chosen.Targets[0][0]);
                var target = rawTarget as Creature
                    ?? throw new InvalidOperationException(
                        $"Magma Spray expected a Creature target but got {rawTarget?.GetType().Name ?? "null"}.");

                return new IEffect[]
                {
                    new Effect(
                        $"Magma Spray: {Damage} damage to target creature; if it would die this turn, exile it instead.",
                        () =>
                        {
                            // Step 1 — deal 2 damage to the targeted creature
                            // (CR 119.2 — non-combat damage marked on the
                            // permanent; SBA at CR 704.5f handles lethal).
                            Fx.DealDamage(target, Damage);

                            // Step 2 — exile rider. Scope the shared
                            // AngerOfTheGodsExileInsteadReplacement to just
                            // this one creature (CR 700.3 — "that creature"
                            // = the single targeted creature). EOT-expirable
                            // per CR 514.2.
                            if (replacements != null)
                            {
                                var damaged = new HashSet<Creature> { target };
                                replacements.Register<ZoneMoveIntent>(
                                    new AngerOfTheGodsExileInsteadReplacement(damaged));
                            }
                        }),
                };
            });
    }
}
