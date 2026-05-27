using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sizzle (Onslaught, {2}{R}).
///
/// Sorcery. Oracle text:
///   "Sizzle deals 3 damage to each opponent."
///
/// ## Implementation
///
/// Card shape at the dispatcher; the on-resolve effect is built on demand
/// via <see cref="BuildResolveEffect"/>. The effect iterates every player
/// in the supplied <c>allPlayers</c> list and deals <see cref="Damage"/>
/// (3) via <see cref="Fx.DealDamage"/> to any player who is NOT the
/// caster — implementing "each opponent" (CR 800.4 — "opponent" means a
/// player other than the controller of the ability at resolution time).
///
/// No targets are chosen (CR 114.1 — a spell or ability that requires no
/// target does not use the stack's target-choice layer). Sizzle is the
/// player-damage analogue of Pyroclasm for creatures.
///
/// ## Wiring
///
/// - <see cref="Create(Player)"/> — card shape only; use for dispatcher /
///   structural tests.
/// - <see cref="BuildResolveEffect"/> — supply <c>caster</c> + the full
///   player list at resolution time (matches
///   <see cref="PyroclasmFactory.BuildResolveEffect"/>'s shape).
///
/// ## CR notes
/// - CR 800.4 — "opponent" / "each opponent" means every other player in
///   the game who is not the caster.
/// - CR 109.5 / CR 700 — "each" without a controlling-player restriction
///   resolves to every qualifying object in the game.
/// - CR 119.2 — non-combat damage to a player; CR 119.3 — damage reduces
///   the player's life total by the damage amount.
/// - CR 119.8 — damage to a player is dealt as life loss, handled by
///   <see cref="Fx.DealDamage"/> → <see cref="Player.LoseLife"/>.
/// </summary>
[CardName("Sizzle")]
public static class SizzleFactory
{
    public const string CardName = "Sizzle";
    public const string PrintedManaCost = "{2}{R}";
    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape only.
    /// <see cref="BuildResolveEffect"/> supplies the "3 damage to each
    /// opponent" sweep.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    /// <summary>Create a Sizzle sorcery owned by <paramref name="owner"/>.
    /// Card shape only — resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Sorcery)CardDefRuntime.Build(Define(), owner);
    }

    /// <summary>
    /// Build Sizzle's resolve effect — 3 damage to every player in
    /// <paramref name="allPlayers"/> who is not <paramref name="caster"/>.
    ///
    /// The snapshot is taken at call time (before the effect executes), so
    /// any zone-change side effects during iteration do not disturb the
    /// player enumeration; SBAs run on the next priority pass.
    /// </summary>
    /// <param name="caster">The player who cast Sizzle; they are excluded
    /// from the damage sweep (CR 800.4).</param>
    /// <param name="allPlayers">All players in the game, in any order.
    /// Typically every seat at the table.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        // Snapshot the opponent list before returning so the closure
        // captures a stable enumerable even if the caller mutates the
        // source list between build and execute.
        var opponents = allPlayers
            .Where(p => !ReferenceEquals(p, caster))
            .ToList();

        return new IEffect[]
        {
            new Effect($"Sizzle: deal {Damage} damage to each opponent.", () =>
            {
                // CR 800.4 — iterate every opponent and deal 3 damage.
                // CR 119.3 — damage to a player reduces their life total.
                // Fx.DealDamage routes Player → Player.LoseLife (CR 119.8).
                foreach (var opp in opponents)
                {
                    Fx.DealDamage(opp, Damage);
                }
            }),
        };
    }
}
