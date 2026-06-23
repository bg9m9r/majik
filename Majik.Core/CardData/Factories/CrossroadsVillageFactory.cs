using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Effects;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crossroads Village (Edge of Eternities — Land — Town).
/// Oracle text (verified against the embedded Modern seed / Scryfall):
///   "This land enters tapped.
///    As it enters, choose a color.
///    {T}: Add one mana of the chosen color."
///
/// <para>
/// The "Town" choose-a-colour Vivid-style tapland: mechanically identical to
/// <see cref="ShimmerdriftValeFactory"/> (the Snow member of the same
/// choose-a-colour tapland family) — only the printed land subtype differs:
/// <c>Town</c> here (CR 205.3m, same subtype as <see cref="BaronAirshipKingdomFactory"/>)
/// vs Shimmerdrift Vale's Snow supertype. The bare Land shell + the printed
/// <c>Town</c> subtype are declared in
/// <c>Majik.Core/CardData/Cards/crossroads-village.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>. The {T} mana ability's colour is not
/// known until the choose-a-colour decision is made, so — like Temple of the
/// Dragon Queen / Shimmerdrift Vale — it is wired in the factory once the chosen
/// colour is supplied, not declared in JSON.
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
[CardName("Crossroads Village")]
public static class CrossroadsVillageFactory
{
    public const string Slug = "crossroads-village";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Crossroads Village owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no chosen colour, no mana
    /// ability, no ETB-tapped replacement wired). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }

    /// <summary>
    /// Construct a fully-wired Crossroads Village.
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
            "Crossroads Village's chosen color must be one of W/U/B/R/G (CR 105.1)."),
    };
}
