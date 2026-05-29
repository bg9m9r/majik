using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sweltering Suns (Amonkhet, {1}{R}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Sweltering Suns deals 3 damage to each creature.
///    Cycling {3} ({3}, Discard this card: Draw a card.)"
///
/// ## Implementation
///
/// Two independent halves:
///
/// 1. <b>Sweep</b> — "deals 3 damage to each creature". Same shape as
///    <see cref="PyroclasmFactory.BuildResolveEffect"/> /
///    <see cref="AngerOfTheGodsFactory.BuildResolveEffect"/>, only the
///    damage is 3. The on-resolve effect iterates every creature on every
///    supplied player's battlefield and deals 3 damage through
///    <see cref="Creature.TakeDamage"/> (CR 109.5 — "each" without a
///    controller restriction reaches every creature on the battlefield).
///    Built on demand via <see cref="BuildResolveEffect"/>; the dispatcher
///    only stamps the card shape + cycling.
///
/// 2. <b>Cycling {3}</b> (CR 702.32) — wired through the shared
///    <see cref="CyclingFactory.Build"/> primitive (as used by the
///    Onslaught cycling lands / Tranquil Thicket). The cycle cost is
///    <see cref="ManaCostCost"/>(<c>{3}</c>); the primitive appends the
///    <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.32a) and, when
///    a bus is supplied, publishes <see cref="CardCycledEvent"/> after the
///    draw (CR 702.32d "Whenever a player cycles a card").
///
/// ## CR notes
/// - CR 109.5 / CR 700 — "each creature" enumerates every creature on the
///   battlefield regardless of controller.
/// - CR 119.2 — non-combat damage; CR 119.3 — damage recorded by
///   <see cref="Creature.TakeDamage"/>; SBA (CR 704.5f) moves lethal-
///   damaged creatures to graveyards on the next SBA pass.
/// - CR 702.32a — Cycling is an activated ability that functions only
///   while the card is in a player's hand; the DiscardSelfCost provides
///   that hand-zone gate.
///
/// ## v1 simplifications
/// - The sweep deals damage directly (no <see cref="ReplacementBus"/>
///   plumbing for prevention / protection), matching
///   <see cref="PyroclasmFactory"/>'s posture. Unlike Anger of the Gods
///   there is no "would die → exile" rider on this card, so no replacement
///   registration is needed.
/// </summary>
[CardName("Sweltering Suns")]
public static class SwelteringSunsFactory
{
    public const string CardName = "Sweltering Suns";
    public const string PrintedManaCost = "{1}{R}{R}";
    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape only. <see cref="BuildResolveEffect"/>
    /// supplies the resolve-time "3 damage to each creature" sweep; cycling
    /// is attached in <see cref="Create"/> via the shared primitive.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    /// <summary>
    /// Build a Sweltering Suns sorcery owned by <paramref name="owner"/>,
    /// with Cycling {3} attached. Shape-only path — cycling does not
    /// publish <see cref="CardCycledEvent"/> (no bus).
    /// </summary>
    public static Sorcery Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Build a Sweltering Suns sorcery with Cycling {3}. When
    /// <paramref name="eventBus"/> is supplied the cycling resolve publishes
    /// <see cref="CardCycledEvent"/> (CR 702.32d).
    /// </summary>
    public static Sorcery Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Sorcery)CardDefRuntime.Build(Define(), owner);

        // CR 702.32 — Cycling {3}. Owner is already wired by
        // CardDefRuntime.Build, which the CyclingFactory primitive requires
        // (its resolve body draws for the card's owner).
        CyclingFactory.Build(card, new ManaCostCost("{3}"), eventBus);

        return card;
    }

    /// <summary>
    /// Build Sweltering Suns's resolve effect — 3 damage to every creature
    /// on every supplied player's battlefield (CR 109.5). Single
    /// <see cref="IEffect"/> entry so callers can splice it into a
    /// <c>SpellDefinition.EffectFactory</c> result or a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields the sweep
    /// should reach. Typically every player in the game.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"Sweltering Suns: deal {Damage} damage to each creature.", () =>
            {
                // CR 109.5 / CR 700 — "each creature" reaches every creature
                // on every battlefield. Snapshot to a list before applying
                // so any same-step zone-move side effects don't disturb the
                // enumeration; SBAs run on the next priority pass and move
                // lethal-damaged creatures to graveyards.
                var seen = new HashSet<Creature>();
                foreach (var pl in allPlayers)
                {
                    foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                    {
                        if (seen.Add(c)) c.TakeDamage(Damage);
                    }
                }
            }),
        };
    }
}
