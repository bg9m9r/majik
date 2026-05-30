using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ur-Golem's Eye (Mirrodin, {4}).
///
/// Artifact mana rock. Oracle text (verified against Scryfall):
///   "{T}: Add {C}{C}."
///
/// ## Implemented (v1)
/// - Artifact identity, printed mana cost {4}, owner/controller wiring.
/// - <b>{T}: Add {C}{C}</b> — a single <see cref="Majik.Core.Abilities.ManaAbility"/>
///   (CR 605.1 — mana abilities don't use the stack). CR 107.4c — the two
///   {C} pips fold into the generic bucket via
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> ("CC" yields
///   <c>Generic == 2</c>), the same colourless-rock shape as Worn Powerstone /
///   Mana Crypt / Eldrazi Temple.
///
/// Strictly simpler than Worn Powerstone: there is no "enters tapped" clause
/// (CR 614.1c does not apply), so there is no <see cref="EntersTappedBinder"/>
/// involvement — the rock enters the battlefield untapped on every load path.
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/ur-golems-eye.json</c> and builds through
/// <see cref="CardDefinitionFactory"/>.
/// </summary>
[CardName("Ur-Golem's Eye")]
public static class UrGolemsEyeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("ur-golems-eye");

    /// <summary>Construct Ur-Golem's Eye owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
