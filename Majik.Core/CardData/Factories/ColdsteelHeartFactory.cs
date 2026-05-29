using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Coldsteel Heart (Coldsnap).
///
/// Snow Artifact, {2}. Oracle text (verified against Scryfall):
///   "This artifact enters tapped.
///    As this artifact enters, choose a color.
///    {T}: Add one mana of the chosen color."
///
/// <para>
/// The Snow Artifact identity ({2}, Snow supertype, owner / controller
/// wiring) is declared in
/// <c>Majik.Core/CardData/Cards/coldsteel-heart.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>. This is the artifact mana-rock analogue
/// of <see cref="TempleOfTheDragonQueenFactory"/>'s "choose a color as it
/// enters" land shape — only the {T} ability's produced color isn't known
/// until the "as this artifact enters, choose a color" decision is made, so
/// the mana ability is wired in the factory once the chosen color is supplied,
/// not declared in JSON.
/// </para>
///
/// <para>
/// ## Choose a color (CR 614.12 — "as this enters" replacement)
/// "As this artifact enters, choose a color." is resolved up front: the chosen
/// <see cref="ManaColor"/> is supplied to the full overload. A live agent
/// prompt for the choice is deferred engine-wide (same posture as
/// <see cref="TempleOfTheDragonQueenFactory"/> / <see cref="UtopiaSprawlFactory"/>);
/// callers / tests pass the already-chosen color. The {T} mana ability then
/// produces exactly that color (CR 605.1a — mana abilities don't use the stack).
/// </para>
///
/// <para>
/// ## Enters tapped (CR 614.1c)
/// "This artifact enters tapped." is an unconditional ETB-tapped clause. On the
/// production load path it is registered automatically by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (the seed oracle text
/// matches its sentence pattern with no conditional qualifier). When a
/// <see cref="ReplacementBus"/> is supplied to the full overload here, an
/// <see cref="EntersTappedReplacement"/> is registered directly so the
/// behaviour is exercisable in isolation (mirrors the ETB-tapped wiring in
/// <see cref="TempleOfTheDragonQueenFactory"/>, minus the conditional predicate).
/// </para>
///
/// <para>
/// The shape-only single-arg dispatcher path constructs identity only: no color
/// is known, so no mana ability is attached and no ETB-tapped replacement is
/// wired (matching every other ETB-replacement factory's single-arg posture).
/// </para>
/// </summary>
[CardName("Coldsteel Heart")]
public static class ColdsteelHeartFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("coldsteel-heart");

    /// <summary>Construct Coldsteel Heart owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no chosen color, no mana
    /// ability, no ETB-tapped replacement wired).</summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }

    /// <summary>
    /// Construct a fully-wired Coldsteel Heart.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenColor">The color chosen "as this artifact enters"
    /// (CR 614.12). Must be one of W/U/B/R/G — the {T} ability adds one mana of
    /// that color.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> for the
    /// unconditional "enters tapped" wiring (CR 614.1c). When <c>null</c>, only
    /// the mana ability is attached (the production load path wires ETB-tapped
    /// via <see cref="Majik.Core.CardData.EntersTappedBinder"/> instead).</param>
    public static Artifact Create(
        Player owner,
        ManaColor chosenColor,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var artifact = Create(owner);

        // {T}: Add one mana of the chosen color (CR 605.1a). One pip of the
        // up-front-chosen color; throws for a non-W/U/B/R/G choice.
        var produced = ManaCostForColor(chosenColor);
        artifact.AddAbility(new ManaAbility(artifact, owner, produced));

        // "This artifact enters tapped." — unconditional ETB-tapped (CR 614.1c).
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(artifact));
        }

        return artifact;
    }

    /// <summary>Single-pip <see cref="ManaCost"/> for a chosen color.</summary>
    private static ManaCost ManaCostForColor(ManaColor color) => color switch
    {
        ManaColor.White => ManaCost.Parse("W"),
        ManaColor.Blue => ManaCost.Parse("U"),
        ManaColor.Black => ManaCost.Parse("B"),
        ManaColor.Red => ManaCost.Parse("R"),
        ManaColor.Green => ManaCost.Parse("G"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(color), color,
            "Coldsteel Heart's chosen color must be one of W/U/B/R/G (CR 105.1)."),
    };
}
