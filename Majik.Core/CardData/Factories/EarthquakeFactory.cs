using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Earthquake (various reprints, {X}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Earthquake deals X damage to each creature without flying and each
///    player."
///
/// ## Implementation
///
/// Card shape comes from the embedded JSON (<c>earthquake.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The on-resolve effect is built on
/// demand via <see cref="BuildResolveEffect"/>, which combines two existing,
/// already-shipping sweep shapes:
///   1. The non-flying creature sweep — mirrors
///      <see cref="FlameSweepFactory.BuildResolveEffect"/>'s flying filter,
///      but inverted: Earthquake skips EVERY creature with flying regardless
///      of controller (CR 109.5 — the "without flying" clause carves flyers
///      out of the otherwise-unrestricted "each creature" set; there is no
///      "you control" restriction). Damage is dealt via
///      <see cref="Creature.TakeDamage(int)"/>.
///   2. The symmetric "each player" burn — mirrors
///      <see cref="FlameRiftFactory.BuildResolveEffect"/>: every supplied
///      player (including the caster) takes X damage via
///      <see cref="Fx.DealDamageAny(object,int)"/> (CR 119.3 — damage to a
///      player reduces life).
///
/// X is supplied to <see cref="BuildResolveEffect"/> as a positional
/// argument. In production cast paths X is the value chosen at cast time and
/// stamped on the card (<see cref="Card.PendingCastX"/> by
/// <c>SpellCastFlow</c>); the JSON declares the {X} in the mana cost so the
/// cast flow prompts for it. The {X}{R} cost makes the card's
/// <see cref="SpellDefinition.HasVariableX"/> true (see
/// <see cref="BuildSpellDefinition"/>).
///
/// Flying is read via <see cref="CombatAbilities.HasFlying(Permanent)"/>,
/// the engine's single source of truth for the Flying keyword marker
/// (CR 702.9).
///
/// ## CR notes
/// - CR 109.5 / CR 700 — "each creature without flying" enumerates every
///   non-flying creature on every battlefield regardless of controller;
///   "each player" enumerates every player.
/// - CR 119.2 — non-combat damage; CR 119.3 — damage to a creature is
///   recorded by <see cref="Creature.TakeDamage(int)"/>, damage to a player
///   reduces that player's life. SBA (CR 704.5g — lethal-damage check) moves
///   lethal-damaged creatures to graveyards on the next SBA pass.
/// - CR 614 — replacement effects on damage (protection, prevention) are
///   honoured by callers who route damage through the replacement bus; this
///   factory deals damage directly to keep the resolve body minimal, same
///   shape as <see cref="FlameSweepFactory"/> / <see cref="FlameRiftFactory"/>.
/// </summary>
[CardName("Earthquake")]
public static class EarthquakeFactory
{
    public const string CardName = "Earthquake";
    public const string Slug = "earthquake";
    public const string PrintedManaCost = "{X}{R}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Earthquake — a variable-X
    /// spell with no modes and no targets (the sweep hits "each" creature and
    /// player, not chosen targets). The resolve body reads the chosen X off
    /// the supplied <paramref name="source"/> card's
    /// <see cref="Card.PendingCastX"/> (stamped by the cast flow after the X
    /// prompt).
    /// </summary>
    /// <param name="source">The Earthquake card being cast; its
    /// <see cref="Card.PendingCastX"/> supplies X at resolution.</param>
    /// <param name="allPlayers">Every player in the game — the sweep is
    /// symmetric and reaches all of them plus every non-flying creature.</param>
    public static SpellDefinition BuildSpellDefinition(
        Card source,
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(allPlayers, source.PendingCastX ?? 0));
    }

    /// <summary>
    /// Build Earthquake's resolve effect — <paramref name="x"/> damage to each
    /// creature WITHOUT flying on every supplied player's battlefield, plus
    /// <paramref name="x"/> damage to each supplied player. Single
    /// <see cref="IEffect"/> entry so callers can splice it into a
    /// <c>SpellDefinition.EffectFactory</c> result or a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields the creature
    /// sweep reaches and who each take X damage. Typically every player in the
    /// game (the burn is symmetric — it hits the caster too).</param>
    /// <param name="x">The chosen X — damage dealt to each non-flying creature
    /// and each player.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers, int x)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"Earthquake: deal {x} damage to each creature without flying and each player.", () =>
            {
                if (x <= 0) return;

                // CR 109.5 / CR 700 — "each creature without flying" reaches
                // every non-flying creature on every battlefield regardless of
                // controller. Snapshot to a list before applying so any
                // same-step zone-move side effects don't disturb the
                // enumeration; SBAs run on the next priority pass and move
                // lethal-damaged creatures to graveyards.
                var seen = new HashSet<Creature>();
                foreach (var pl in allPlayers)
                {
                    foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                    {
                        if (!seen.Add(c)) continue;

                        // CR 702.9 — creatures WITH flying are exempt (no
                        // controller restriction; opponent flyers are spared
                        // too).
                        if (CombatAbilities.HasFlying(c)) continue;

                        c.TakeDamage(x);
                    }
                }

                // CR 119.3 — "each player" takes X damage (life loss). The
                // burn is symmetric, so the caster is included.
                foreach (var pl in allPlayers.ToList())
                {
                    Fx.DealDamageAny(pl, x);
                }
            }),
        };
    }
}
