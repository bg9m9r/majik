using Majik.Core.Cards;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced card
/// "Turntimber Symbiosis // Turntimber, Serpentine Wood" ({4}{G}{G}{G}).
///
/// The two single faces each already dispatch under their own printed name:
/// <list type="bullet">
///   <item><see cref="TurntimberSymbiosisFactory"/> — front face (Sorcery,
///     "Look at the top seven cards of your library…").</item>
///   <item><see cref="TurntimberSerpentineWoodFactory"/> — back face (Land,
///     "As this land enters, you may pay 3 life…"; "{T}: Add {G}.").</item>
/// </list>
///
/// Scryfall (and therefore the embedded Modern seed) ALSO keys MDFCs under the
/// combined "Front // Back" name, so the combined name must dispatch too. Per
/// CR 712.3 / 712.4 (cast-either-face — no transform happens; the controller
/// chooses which face to use at cast / play time and only that face exists),
/// the combined-name object is built as the FRONT face: a castable
/// <see cref="Sorcery"/> carrying the same <see cref="MdfcState"/> back-face
/// LAND descriptor that <see cref="TurntimberSymbiosisFactory.Create"/>
/// attaches. <see cref="MdfcCastFlow"/> reads that descriptor to offer the
/// controller a face choice and, when the back face is chosen, materializes a
/// fresh Turntimber, Serpentine Wood land (with its "pay 3 life or enter
/// tapped" ETB).
///
/// Identity (name / type / printed cost) is loaded from the embedded JSON
/// definition (<c>turntimber-symbiosis-turntimber-serpentine-wood.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — identical shape to
/// <see cref="TurntimberSymbiosisFactory"/>, whose
/// <see cref="TurntimberSymbiosisFactory.BuildSpellDefinition"/> /
/// <see cref="TurntimberSymbiosisFactory.ResolveAsync"/> still own the
/// resolve-time look-at-top-seven dig behaviour for the front face.
/// </summary>
[CardName("Turntimber Symbiosis // Turntimber, Serpentine Wood")]
public static class TurntimberSymbiosisCombinedFactory
{
    public const string CombinedName =
        "Turntimber Symbiosis // Turntimber, Serpentine Wood";
    public const string FrontName = TurntimberSymbiosisFactory.CardName;
    public const string BackName = TurntimberSymbiosisFactory.BackName;
    public const string Slug = "turntimber-symbiosis-turntimber-serpentine-wood";

    /// <summary>
    /// Construct the combined-name card as its castable FRONT face — a
    /// <see cref="Sorcery"/> (identity from the combined-slug JSON) with the
    /// <see cref="MdfcState"/> back-face LAND descriptor wired exactly as the
    /// standalone front-face factory does (CR 712.3 / 712.4).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from the combined-slug JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Sorcery)CardDefinitionFactory.Build(definition, owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor. Same back-face-land wiring as
        // TurntimberSymbiosisFactory.Create: the back face (Turntimber,
        // Serpentine Wood) is a LAND played with no stack; MdfcCastFlow
        // materializes a fresh land instance (wired to the live ReplacementBus
        // so its "pay 3 life or enter tapped" ETB fires) when chosen.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                TurntimberSerpentineWoodFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(FrontName, BackName, backFace);

        return card;
    }
}
