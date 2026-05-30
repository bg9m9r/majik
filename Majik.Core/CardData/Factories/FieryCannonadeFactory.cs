using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fiery Cannonade (Magic Origins / reprints, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall 2026-05-29):
///   "Fiery Cannonade deals 2 damage to each non-Pirate creature."
///
/// ## Relationship to <see cref="PyroclasmFactory"/>
/// Fiery Cannonade is the instant-speed, Pirate-sparing analogue of Pyroclasm
/// (the sorcery "2 damage to each creature" sweeper). The card shape differs
/// only in the card type (Instant vs Sorcery), and the resolve effect differs
/// only by one predicate: creatures with the Pirate creature type are excluded
/// from the affected set (CR 205.3m — Pirate is a creature type).
///
/// ## Implementation
/// The base Instant shape (name / Instant type / {1}{R} cost) is materialised
/// from the embedded JSON definition (<c>fiery-cannonade.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="EchoingTruthFactory"/>. The on-resolve "2 damage to each
/// non-Pirate creature" sweep is built on demand via
/// <see cref="BuildResolveEffect"/>, mirroring
/// <see cref="PyroclasmFactory.BuildResolveEffect"/> (the JSON ability schema
/// does not express a damage-each-creature sweep, so the resolve body is
/// layered on here).
///
/// ## CR notes
/// - CR 109.5 / CR 700 — "each non-Pirate creature" enumerates every creature
///   on the battlefield regardless of controller, then filters out Pirates.
/// - CR 205.3m — Pirate is a creature type; the exclusion is a static subtype
///   check (<see cref="Card.HasSubtype"/>) evaluated at resolution.
/// - CR 119.2 — non-combat damage; CR 119.3 — damage is recorded by
///   <see cref="Creature.TakeDamage"/>; SBA (CR 704.5g / CreatureDeathCheck)
///   moves lethal-damaged creatures to graveyards on the next SBA pass.
/// - CR 614 — replacement effects on damage (protection, prevention) are the
///   caller's responsibility; this factory deals damage directly to keep the
///   resolve body minimal, same shape as <see cref="PyroclasmFactory"/>.
/// </summary>
[CardName("Fiery Cannonade")]
public static class FieryCannonadeFactory
{
    public const string CardName = "Fiery Cannonade";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 2;

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "fiery-cannonade";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {1}{R}) from the
    /// embedded JSON definition. Resolve behaviour (2 damage to each non-Pirate
    /// creature) is built on demand via <see cref="BuildResolveEffect"/>,
    /// mirroring <see cref="PyroclasmFactory"/> / <see cref="EchoingTruthFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build Fiery Cannonade's resolve effect — 2 damage to every non-Pirate
    /// creature on every supplied player's battlefield. Single
    /// <see cref="IEffect"/> entry so callers can splice it into a
    /// <c>SpellDefinition.EffectFactory</c> result or a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields the sweep should
    /// reach. Typically every player in the game.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"Fiery Cannonade: deal {Damage} damage to each non-Pirate creature.", () =>
            {
                // CR 109.5 / CR 700 — "each non-Pirate creature" reaches every
                // creature on every battlefield, then excludes Pirates
                // (CR 205.3m). Snapshot to a list before applying so any
                // same-step zone-move side effects don't disturb the
                // enumeration; SBAs run on the next priority pass and move
                // lethal-damaged creatures to graveyards.
                var seen = new HashSet<Creature>();
                foreach (var pl in allPlayers)
                {
                    foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                    {
                        // CR 205.3m — Pirates are not in the affected set.
                        if (c.HasSubtype(CardSubtype.Pirate)) continue;
                        if (seen.Add(c)) c.TakeDamage(Damage);
                    }
                }
            }),
        };
    }
}
