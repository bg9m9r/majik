using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Effects;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Uncharted Haven (Bloomburrow — Land). Oracle text
/// (verified against Scryfall 2026-06-23):
///   "This land enters tapped. As it enters, choose a color.
///    {T}: Add one mana of the chosen color."
///
/// <para>
/// The colourless choose-a-colour tapland — the same shape as
/// <see cref="ShimmerdriftValeFactory"/> but a bare <c>Land</c> (no Snow
/// supertype): the unconditional enters-tapped half plus the "as it enters,
/// choose a color" up-front-resolution posture (CR 614.12 / 614.10). The bare
/// Land shell is declared in
/// <c>Majik.Core/CardData/Cards/uncharted-haven.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>. The {T} mana ability's colour is not
/// known until the choose-a-colour decision is made, so it is wired in the
/// factory once the chosen colour is supplied, not declared in JSON.
/// </para>
///
/// <para>
/// ## Choose a color (CR 614.12 — "as it enters" replacement)
/// "As it enters, choose a color." is resolved up front: the chosen
/// <see cref="ManaColor"/> is supplied to the full overload and the {T} mana
/// ability then produces exactly that colour (CR 605.1a — mana abilities don't
/// use the stack). On the live prod path lands route through the binder chain
/// (<see cref="OracleManaBinder"/> + <see cref="ChooseColorLandBinder"/>), which
/// prompts the controller's agent for the colour; this factory path takes the
/// already-chosen colour up front (same posture as
/// <see cref="ShimmerdriftValeFactory"/> / <see cref="TempleOfTheDragonQueenFactory"/>).
/// </para>
///
/// <para>
/// ## Enters tapped (CR 614.1c)
/// "This land enters tapped" is an unconditional <see cref="EntersTappedReplacement"/>
/// registered when a <see cref="ReplacementBus"/> is supplied (matched off the
/// printed oracle text by <see cref="EntersTappedBinder"/> on the prod path).
/// </para>
///
/// <para>
/// The shape-only single-arg dispatcher path constructs identity only: no colour
/// is known, so no mana ability is attached and no ETB-tapped replacement is
/// wired (matching every other ETB-replacement factory's single-arg posture).
/// </para>
/// </summary>
[CardName("Uncharted Haven")]
public static class UnchartedHavenFactory
{
    public const string Slug = "uncharted-haven";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Uncharted Haven owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no chosen colour, no mana
    /// ability, no ETB-tapped replacement wired). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }

    /// <summary>
    /// Construct a fully-wired Uncharted Haven.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenColor">The colour chosen "as it enters"
    /// (CR 614.12). Must be one of W/U/B/R/G — the {T} ability adds one mana of
    /// that colour.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> for the
    /// unconditional "enters tapped" wiring (CR 614.1c). When <c>null</c>, only
    /// the mana ability is attached.</param>
    public static Land Create(
        Player owner,
        ManaColor chosenColor,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = Create(owner);

        // {T}: Add one mana of the chosen color (CR 605.1a). One pip of the
        // up-front-chosen colour; throws for a non-W/U/B/R/G choice.
        var produced = ManaCostForColor(chosenColor);
        land.AddAbility(new ManaAbility(land, owner, produced));

        // "This land enters tapped" — CR 614.1c, unconditional.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }

    /// <summary>Single-pip <see cref="ManaCost"/> for a chosen colour.</summary>
    private static ManaCost ManaCostForColor(ManaColor color) => color switch
    {
        ManaColor.White => ManaCost.Parse("W"),
        ManaColor.Blue => ManaCost.Parse("U"),
        ManaColor.Black => ManaCost.Parse("B"),
        ManaColor.Red => ManaCost.Parse("R"),
        ManaColor.Green => ManaCost.Parse("G"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(color), color,
            "Uncharted Haven's chosen color must be one of W/U/B/R/G (CR 105.1)."),
    };
}
