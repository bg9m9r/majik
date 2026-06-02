using Majik.Core.Cards;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Modern Horizons 3
/// modal double-faced card
/// "Vastwood Fortification // Vastwood Thicket" ({G}).
///
/// The two single faces each already dispatch under their own printed name:
/// <list type="bullet">
///   <item><see cref="VastwoodFortificationFactory"/> — front face (Instant
///     {G}, "Put a +1/+1 counter on target creature.").</item>
///   <item><see cref="VastwoodThicketFactory"/> — back face (Land,
///     "This land enters tapped."; "{T}: Add {G}.").</item>
/// </list>
///
/// Scryfall (and therefore the embedded Modern seed) ALSO keys MDFCs under the
/// combined "Front // Back" name, so the combined name must dispatch too. Per
/// CR 712.3 / 712.4 (cast-either-face — no transform happens; the controller
/// chooses which face to use at cast / play time and only that face exists),
/// the combined-name object is built as the FRONT face: a castable
/// <see cref="Instant"/> carrying the same <see cref="MdfcState"/> back-face
/// LAND descriptor that <see cref="VastwoodFortificationFactory.Create"/>
/// attaches. <see cref="MdfcCastFlow"/> reads that descriptor to offer the
/// controller a face choice and, when the back face is chosen, materializes a
/// fresh Vastwood Thicket land (with its "enters tapped" ETB).
///
/// Identity (name / type / printed cost) is loaded from the embedded JSON
/// definition (<c>vastwood-fortification-vastwood-thicket.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — identical shape to
/// <see cref="TurntimberSymbiosisCombinedFactory"/> (combined-name MDFC; the
/// only difference is the front face is an Instant rather than a Sorcery).
/// <see cref="VastwoodFortificationFactory.BuildDefinition"/> still owns the
/// resolve-time +1/+1-counter behaviour for the front face.
/// </summary>
[CardName("Vastwood Fortification // Vastwood Thicket")]
public static class VastwoodFortificationCombinedFactory
{
    public const string CombinedName =
        "Vastwood Fortification // Vastwood Thicket";
    public const string FrontName = VastwoodFortificationFactory.CardName;
    public const string BackName = VastwoodFortificationFactory.BackName;
    public const string Slug = "vastwood-fortification-vastwood-thicket";

    /// <summary>
    /// Construct the combined-name card as its castable FRONT face — an
    /// <see cref="Instant"/> (identity from the combined-slug JSON) with the
    /// <see cref="MdfcState"/> back-face LAND descriptor wired exactly as the
    /// standalone front-face factory does (CR 712.3 / 712.4).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from the combined-slug JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(definition, owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor. Same back-face-land wiring as
        // VastwoodFortificationFactory.Create: the back face (Vastwood Thicket)
        // is a LAND played with no stack; MdfcCastFlow materializes a fresh
        // land instance (wired to the live ReplacementBus so its "enters
        // tapped" ETB fires) when chosen.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                VastwoodThicketFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(FrontName, BackName, backFace);

        return card;
    }
}
