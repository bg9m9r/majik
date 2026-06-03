using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Corpse Knight (Modern Horizons, {W}{B}).
///
/// Creature — Zombie Knight 2/2. Oracle text (verified against Scryfall):
///   "Whenever another creature you control enters, each opponent loses 1 life."
///
/// The card's base shape (name, Creature, Zombie Knight subtypes, {W}{B}, 2/2)
/// and the "another creature you control enters → each opponent loses 1 life"
/// trigger are materialised entirely from the embedded JSON definition
/// (<c>corpse-knight.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — no code-side wiring is needed.
///
/// ## Implemented (v1)
/// - 2/2 Zombie Knight at printed cost {W}{B} (mana value 2).
/// - <b>Triggered ability (CR 603.6e / CR 109.5)</b>: a
///   <c>whenever_another_creature_enters</c> trigger scoped to the controller's
///   OTHER creatures (<c>youControlOnly</c>, <c>includeSelf</c> default false —
///   "another creature you control"). On resolution the new
///   <c>lose_life_each_opponent</c> verb drains 1 life from every player OTHER
///   than the controller (CR 109.5 "each opponent"), routed through the shared
///   <c>Fx.LoseLife</c> primitive. Untargeted — a group effect (CR 608.2), so no
///   target is announced and nothing can be removed in response. This is the
///   declarative pay-down of the opponent-scoped life-loss rider shape.
/// </summary>
[CardName("Corpse Knight")]
public static class CorpseKnightFactory
{
    public const string CardName = "Corpse Knight";
    public const string Slug = "corpse-knight";

    /// <summary>
    /// Construct Corpse Knight owned and controlled by <paramref name="owner"/>.
    /// Base shape + the each-opponent-loses-1-life trigger come from the embedded
    /// JSON definition.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
