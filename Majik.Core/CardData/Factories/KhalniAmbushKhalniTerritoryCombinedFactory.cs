using Majik.Core.Cards;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced card "Khalni Ambush // Khalni Territory" ({2}{G}).
///
/// The two single faces each already dispatch under their own printed name:
/// <list type="bullet">
///   <item><see cref="KhalniAmbushFactory"/> — front face (Instant {2}{G},
///     "Target creature you control fights target creature you don't
///     control.").</item>
///   <item><see cref="KhalniTerritoryFactory"/> — back face (Land,
///     "This land enters tapped." / "{T}: Add {G}.").</item>
/// </list>
///
/// Scryfall (and therefore the embedded Modern seed) ALSO keys MDFCs under the
/// combined "Front // Back" name, so the combined name must dispatch too. Per
/// CR 712.3 / 712.4 (cast-either-face — no transform happens; the controller
/// chooses which face to use at cast / play time and only that face exists),
/// the combined-name object is built as the FRONT face: a castable
/// <see cref="Instant"/> carrying the same <see cref="MdfcState"/> back-face
/// LAND descriptor that <see cref="KhalniAmbushFactory.Create"/> attaches.
/// <see cref="MdfcCastFlow"/> reads that descriptor to offer the controller a
/// face choice and, when the back face is chosen, materializes a fresh Khalni
/// Territory land (with its "enters tapped" ETB).
///
/// Identity (name / type / printed cost) is loaded from the embedded JSON
/// definition (<c>khalni-ambush-khalni-territory.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — identical shape to
/// <see cref="TurntimberSymbiosisCombinedFactory"/>. The front face's
/// resolve-time fight <see cref="SpellDefinition"/> (CR 701.13) is still owned
/// by <see cref="KhalniAmbushFactory.BuildDefinition"/>.
/// </summary>
[CardName("Khalni Ambush // Khalni Territory")]
public static class KhalniAmbushKhalniTerritoryCombinedFactory
{
    public const string CombinedName = "Khalni Ambush // Khalni Territory";
    public const string FrontName = KhalniAmbushFactory.CardName;
    public const string BackName = KhalniAmbushFactory.BackName;
    public const string Slug = "khalni-ambush-khalni-territory";

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
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor. Same back-face-land wiring as
        // KhalniAmbushFactory.Create: the back face (Khalni Territory) is a
        // LAND played with no stack; MdfcCastFlow materializes a fresh land
        // instance (wired to the live ReplacementBus so its "enters tapped"
        // ETB fires) when chosen. No transform happens — only the chosen face
        // exists.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                KhalniTerritoryFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(FrontName, BackName, backFace);

        return card;
    }
}
