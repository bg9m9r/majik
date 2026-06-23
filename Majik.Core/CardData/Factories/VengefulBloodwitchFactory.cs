using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vengeful Bloodwitch (Duskmourn: House of Horror,
/// {1}{B}).
///
/// Creature — Vampire Warlock 1/1. Oracle text (verified against Scryfall):
///   "Whenever this creature or another creature you control dies, target
///    opponent loses 1 life and you gain 1 life."
///
/// The card's base shape (name, Creature, Vampire Warlock subtypes, {1}{B},
/// 1/1) and the aristocrat death trigger are materialised entirely from the
/// embedded JSON definition (<c>vengeful-bloodwitch.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — no code-side wiring is needed.
///
/// ## Implemented (v1)
/// - 1/1 Vampire Warlock at printed cost {1}{B} (mana value 2).
/// - <b>Triggered ability (CR 603.6c / CR 109.5 / CR 700.4)</b>: a
///   <c>whenever_another_creature_dies</c> trigger scoped to the controller's
///   creatures (<c>youControlOnly</c> with <c>includeSelf</c> — "this creature
///   OR another creature you control"). The <c>includeSelf</c> flag keeps the
///   trigger active in the Graveyard so the Bloodwitch's OWN death still fires
///   it (CR 603.6c — a self-naming dies trigger reads last-known information
///   just before leaving the battlefield). On resolution the
///   <c>lose_life_target</c> verb in <c>subject: "target"</c> mode declares a
///   single <c>opponent</c> target slot (CR 102.2 / 115.1 — "target opponent")
///   and drains it 1 life, and the <c>gain_life_self</c> verb gains the
///   controller 1 life (CR 119.3).
/// - The life loss and the lifegain are SEPARATE life-change events (CR 119.3 —
///   no lifelink); each is visible to lifegain-payoff / life-loss-matters
///   observers downstream.
///
/// ## Notes
/// - <b>"target opponent" filter</b>: the <c>opponent</c>
///   <see cref="TargetFilters"/> filter scopes the player target to every
///   player OTHER than the resolving controller (CR 102.2), so — unlike the
///   plain <c>player</c> filter — the controller cannot choose themselves. CR
///   608.2b — if no legal opponent exists at resolution the targeted drain
///   fizzles; the lifegain side, being untargeted, still resolves.
/// </summary>
[CardName("Vengeful Bloodwitch")]
public static class VengefulBloodwitchFactory
{
    public const string CardName = "Vengeful Bloodwitch";
    public const string Slug = "vengeful-bloodwitch";

    /// <summary>
    /// Construct Vengeful Bloodwitch owned and controlled by
    /// <paramref name="owner"/>. Base shape + the this-or-another-creature-you-
    /// control dies drain trigger come from the embedded JSON definition.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
