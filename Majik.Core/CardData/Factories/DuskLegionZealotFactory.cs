using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dusk Legion Zealot (Rivals of Ixalan, {1}{B}).
///
/// Creature — Vampire Soldier 1/1. Oracle text (verified against Scryfall):
///   "When this creature enters, you draw a card and you lose 1 life."
///
/// The card's base shape (name, Creature, Vampire Soldier subtypes, {1}{B},
/// 1/1) and the ETB "draw a card and lose 1 life" trigger are materialised
/// entirely from the embedded JSON definition
/// (<c>dusk-legion-zealot.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — the JSON schema already
/// expresses the <c>etb_self</c> trigger with an ordered effect list, the
/// <c>draw_card</c> verb (same path as <see cref="ThoughtMonitorFactory"/>'s
/// ETB draw) and the <c>lose_life_self</c> verb (the untargeted,
/// controller-scoped life-loss mirror of <see cref="SkyclaveClericFactory"/>'s
/// ETB gain-life). No code-side wiring is needed.
///
/// ## Implemented (v1)
/// - 1/1 Vampire Soldier at printed cost {1}{B} (mana value 2).
/// - <b>ETB triggered ability (CR 603.6)</b>: a single <c>etb_self</c>
///   trigger whose two ordered effects resolve in printed order —
///   <c>draw_card</c> (amount 1) then <c>lose_life_self</c> (amount 1).
///   The draw removes the top card of the controller's library to hand;
///   the life loss routes through the shared <c>Fx.LoseLife</c> primitive
///   (CR 119.3). An empty library at resolution is a no-op at the effect
///   level — the SBA-driven draw-from-empty loss (CR 704.5c) is handled by
///   the engine's state-based-action pass elsewhere.
/// </summary>
[CardName("Dusk Legion Zealot")]
public static class DuskLegionZealotFactory
{
    public const string CardName = "Dusk Legion Zealot";
    public const string Slug = "dusk-legion-zealot";

    /// <summary>
    /// Construct Dusk Legion Zealot owned and controlled by
    /// <paramref name="owner"/>. Base shape + the ETB draw-a-card-and-lose-
    /// 1-life trigger come from the embedded JSON definition.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
