using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boltwave (Foundations / many reprints, {R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Boltwave deals 3 damage to each opponent."
///
/// ## Implementation
///
/// - <b>Sorcery</b> shape, mana cost {R}. Card shape comes from the embedded
///   JSON (<c>boltwave.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> — the same JSON-backed posture as
///   <see cref="AlchemistsGreetingFactory"/>.
/// - <b>No targets</b> are chosen (CR 114.1 — "each opponent" is not a
///   target). Boltwave is the {R} player-damage analogue of
///   <see cref="SizzleFactory"/> (the same effect at {2}{R}).
/// - The on-resolve effect is built on demand via
///   <see cref="BuildResolveEffect"/>; it iterates every player who is NOT
///   the caster and deals <see cref="Damage"/> (3) via
///   <see cref="Fx.DealDamage"/> — implementing "each opponent" (CR 800.4 —
///   "opponent" means a player other than the controller at resolution).
///
/// ## Production cast path
///
/// The live cast path resolves Boltwave's body through the oracle-text spell
/// template <c>DealsNDamageEachOpponentTemplate</c> (which names Boltwave as
/// its canonical example) → <c>DamageSpellFactory.EachOpponentLosesLifeSpell</c>.
/// This factory supplies the card shape (so <c>IsImplemented</c> flips on) and
/// a structurally identical deterministic resolve helper for tests.
///
/// ## CR notes
/// - CR 800.4 — "each opponent" means every other player in the game who is
///   not the caster.
/// - CR 119.2 / 119.3 — non-combat damage to a player reduces their life
///   total by the damage amount.
/// - CR 119.8 — damage to a player is dealt as life loss, handled by
///   <see cref="Fx.DealDamage"/> → <see cref="Player.LoseLife"/>.
/// </summary>
[CardName("Boltwave")]
public static class BoltwaveFactory
{
    public const string CardName = "Boltwave";
    public const string Slug = "boltwave";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 3;

    /// <summary>Build the card shape from the embedded JSON definition.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build Boltwave's resolve effect — 3 damage to every player in
    /// <paramref name="allPlayers"/> who is not <paramref name="caster"/>.
    ///
    /// The opponent snapshot is taken at call time (before the effect
    /// executes), so any zone-change side effects during iteration do not
    /// disturb the player enumeration; SBAs run on the next priority pass.
    /// Mirrors <see cref="SizzleFactory.BuildResolveEffect"/>.
    /// </summary>
    /// <param name="caster">The player who cast Boltwave; they are excluded
    /// from the damage sweep (CR 800.4).</param>
    /// <param name="allPlayers">All players in the game, in any order.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        var opponents = allPlayers
            .Where(p => !ReferenceEquals(p, caster))
            .ToList();

        return new IEffect[]
        {
            new Effect($"Boltwave: deal {Damage} damage to each opponent.", () =>
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
