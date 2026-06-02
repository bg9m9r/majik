using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phyrexian Rager (Mirrodin Besieged, {2}{B}).
///
/// Creature — Phyrexian Horror 2/2. Oracle text (verified against Scryfall):
///   "When this creature enters, you draw a card and you lose 1 life."
///
/// Identical ETB primitive to <see cref="DuskLegionZealotFactory"/> — the
/// base shape (name, Creature, Phyrexian Horror subtypes, {2}{B}, 2/2) and
/// the ETB "draw a card and lose 1 life" trigger are materialised entirely
/// from the embedded JSON definition (<c>phyrexian-rager.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON schema already
/// expresses the <c>etb_self</c> trigger with an ordered effect list, the
/// <c>draw_card</c> verb and the <c>lose_life_self</c> verb, so no code-side
/// wiring is needed.
///
/// ## Implemented (v1)
/// - 2/2 Phyrexian Horror at printed cost {2}{B} (mana value 3).
/// - <b>ETB triggered ability (CR 603.6)</b>: a single <c>etb_self</c>
///   trigger whose two ordered effects resolve in printed order —
///   <c>draw_card</c> (amount 1) then <c>lose_life_self</c> (amount 1).
///   The draw removes the top card of the controller's library to hand
///   (CR 120.2); the life loss routes through the shared <c>Fx.LoseLife</c>
///   primitive (CR 119.3). An empty library at resolution is a no-op at the
///   effect level — the SBA-driven draw-from-empty loss (CR 704.5c) is
///   handled by the engine's state-based-action pass elsewhere.
/// </summary>
[CardName("Phyrexian Rager")]
public static class PhyrexianRagerFactory
{
    public const string CardName = "Phyrexian Rager";
    public const string Slug = "phyrexian-rager";

    /// <summary>
    /// Construct Phyrexian Rager owned and controlled by
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
