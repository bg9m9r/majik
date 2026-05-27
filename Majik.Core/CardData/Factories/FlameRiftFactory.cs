using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flame Rift (Nemesis / various reprints, {1}{R}).
///
/// Sorcery. Oracle text:
///   "Flame Rift deals 4 damage to each player."
///
/// ## Implementation
///
/// Card shape only at the dispatcher; the on-resolve effect is built on
/// demand via <see cref="BuildResolveEffect"/>. The effect iterates every
/// supplied player and deals 4 damage through
/// <see cref="Fx.DealDamageAny"/> (CR 119.3 — damage to a player reduces
/// that player's life). This is the player-facing sibling of
/// <see cref="PyroclasmFactory.BuildResolveEffect"/>'s "2 damage to each
/// creature" sweep, sharing the same positional <c>allPlayers</c> shape so
/// callers can fire the symmetric burn at every player in one call.
///
/// ## Why a named factory
/// Flame Rift is symmetrical — it hits the caster too. Production cast paths
/// through <c>SpellCastFlow</c> can plumb
/// <see cref="Majik.Core.Game.ChosenSpellParams.AllPlayers"/> through the
/// effect factory, but several tests and bot probes construct the spell
/// directly without that plumbing. The named factory exposes a single
/// resolve effect that takes <c>allPlayers</c> as a positional argument,
/// matching <see cref="PyroclasmFactory.BuildResolveEffect"/>.
///
/// ## CR notes
/// - CR 109.5 / CR 700 — "each player" enumerates every player in the game.
/// - CR 119.3 — damage dealt to a player causes that player to lose that
///   much life; <see cref="Fx.DealDamageAny"/> routes a Player target to
///   <see cref="Player.LoseLife"/>.
/// </summary>
[CardName("Flame Rift")]
public static class FlameRiftFactory
{
    public const string CardName = "Flame Rift";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 4;

    /// <summary>CardDef DSL — card shape only. <see cref="BuildResolveEffect"/>
    /// supplies the resolve-time "4 damage to each player" sweep.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Flame Rift's resolve effect — 4 damage to every supplied
    /// player. Single <see cref="IEffect"/> entry so callers can splice it
    /// into a <c>SpellDefinition.EffectFactory</c> result or a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// </summary>
    /// <param name="allPlayers">All players the symmetric burn should reach.
    /// Typically every player in the game (including the caster).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"Flame Rift: deal {Damage} damage to each player.", () =>
            {
                // CR 109.5 / CR 700 — "each player" reaches every player.
                // Snapshot to a list before applying so any same-step side
                // effects don't disturb the enumeration.
                foreach (var pl in allPlayers.ToList())
                {
                    Fx.DealDamageAny(pl, Damage);
                }
            }),
        };
    }
}
