using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temple of the Dragon Queen (Tarkir: Dragonstorm).
///
/// Land. Oracle text:
///   "As this land enters, you may reveal a Dragon card from your hand. This
///    land enters tapped unless you revealed a Dragon card this way or you
///    control a Dragon.
///    As this land enters, choose a color.
///    {T}: Add one mana of the chosen color."
///
/// <para>
/// The Land identity is declared in
/// <c>Majik.Core/CardData/Cards/temple-of-the-dragon-queen.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>. Unlike a fixed-output
/// dual, the {T} mana ability's color is not known until the "as this enters,
/// choose a color" decision is made, so the mana ability is wired in the
/// factory once the chosen color is supplied — not declared in JSON.
/// </para>
///
/// <para>
/// ## Choose a color (CR 614.12 / 614.10 — "as this enters" replacement)
/// "As this land enters, choose a color." is resolved up front: the chosen
/// <see cref="ManaColor"/> is supplied to the full overload. A live agent
/// prompt for the choice is deferred engine-wide (same posture as
/// <see cref="UtopiaSprawlFactory"/>); callers/tests pass the already-chosen
/// color. The {T} mana ability then produces exactly that color (CR 605.1a —
/// mana abilities don't use the stack).
/// </para>
///
/// <para>
/// ## Enters tapped unless … (CR 614.1c)
/// "This land enters tapped unless you revealed a Dragon card this way or you
/// control a Dragon" is a <see cref="ConditionalEntersTappedReplacement"/>
/// registered when a <see cref="ReplacementBus"/> is supplied. The "revealed a
/// Dragon card this way" half is itself an "as this enters" decision
/// (CR 614.10) — resolved up front and passed as <c>revealedDragon</c>, same
/// deferred-prompt posture as the chosen color. The "you control a Dragon"
/// half counts the controller's battlefield permanents carrying the Dragon
/// subtype (CR 205.3 — Dragon is a creature type), excluding this land itself
/// by reference equality. The land enters untapped iff either half holds.
/// </para>
///
/// <para>
/// The shape-only single-arg dispatcher path constructs identity only: no
/// color is known, so no mana ability is attached and no ETB-tapped
/// replacement is wired (matching every other ETB-replacement factory's
/// single-arg posture).
/// </para>
/// </summary>
[CardName("Temple of the Dragon Queen")]
public static class TempleOfTheDragonQueenFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("temple-of-the-dragon-queen");

    /// <summary>Construct Temple of the Dragon Queen owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no chosen color, no mana
    /// ability, no ETB-tapped replacement wired).</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }

    /// <summary>
    /// Construct a fully-wired Temple of the Dragon Queen.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenColor">The color chosen "as this land enters"
    /// (CR 614.12). Must be one of W/U/B/R/G — the {T} ability adds one mana of
    /// that color.</param>
    /// <param name="revealedDragon"><c>true</c> if the controller revealed a
    /// Dragon card from hand "this way" as the land entered (CR 614.10); lets
    /// the land enter untapped.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> for full
    /// "enters tapped unless …" wiring (CR 614.1c). When <c>null</c>, only the
    /// mana ability is attached.</param>
    public static Land Create(
        Player owner,
        ManaColor chosenColor,
        bool revealedDragon,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = Create(owner);

        // {T}: Add one mana of the chosen color (CR 605.1a). One pip of the
        // up-front-chosen color; throws for a non-W/U/B/R/G choice.
        var produced = ManaCostForColor(chosenColor);
        land.AddAbility(new ManaAbility(land, owner, produced));

        // Enters tapped unless you revealed a Dragon card this way OR you
        // control a Dragon (CR 614.1c). Predicate true => enters untapped.
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    revealedDragon || ControllerControlsDragon(controller, self)));
        }

        return land;
    }

    private static bool ControllerControlsDragon(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Dragon));

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
            "Temple of the Dragon Queen's chosen color must be one of W/U/B/R/G (CR 105.1)."),
    };
}
