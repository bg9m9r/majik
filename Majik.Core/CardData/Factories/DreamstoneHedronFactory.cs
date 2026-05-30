using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dreamstone Hedron (Rise of the Eldrazi, {6}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{T}: Add {C}{C}{C}.
///    {3}, {T}, Sacrifice this artifact: Draw three cards."
///
/// Same two-mode mana-rock shape as <see cref="HedronArchiveFactory"/>
/// (tap-for-colourless mana ability + "{cost}, {T}, Sacrifice: draw" cantrip),
/// scaled to three colourless / {3} / draw three. Unlike the hand-rolled
/// Hedron Archive, the whole card is expressible in the data-only schema, so
/// the body lives entirely in <c>dreamstone-hedron.json</c> and this factory
/// is a thin wrapper over
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> — no inline closures.
///
/// Ability shapes (both already supported by the engine):
/// - <b>{T}: Add {C}{C}{C}</b> — a <see cref="Majik.Core.Abilities.ManaAbility"/>
///   (CR 605.1, mana abilities don't use the stack). {C}{C}{C} folds into the
///   generic bucket via <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>
///   (CR 107.4c) → three colourless.
/// - <b>{3}, {T}, Sacrifice this artifact: Draw three cards</b> — an
///   <see cref="Majik.Core.Abilities.ActivatedAbility"/> (CR 602) with three
///   costs (mana pip, tap, sacrifice self) and a <c>draw_card</c> effect.
///   Empty library is a silent no-op for the unavailable draws; the SBA loss
///   flag (CR 704.5b) is handled by the draw path, not here.
/// </summary>
[CardName("Dreamstone Hedron")]
public static class DreamstoneHedronFactory
{
    public const string CardName = "Dreamstone Hedron";
    public const string Slug = "dreamstone-hedron";
    public const string PrintedManaCost = "{6}";

    /// <summary>Construct Dreamstone Hedron owned and controlled by
    /// <paramref name="owner"/> from the embedded JSON definition.</summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Artifact)CardDefinitionFactory.Build(def, owner);
    }
}
