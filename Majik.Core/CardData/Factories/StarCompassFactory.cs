using System;
using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Star Compass (Prophecy, {2}).
///
/// Artifact. Oracle text:
///   "This artifact enters tapped.
///    {T}: Add one mana of any color that a basic land you control could
///    produce."
///
/// ## Implementation
///
/// Card identity (Artifact, {2}) is loaded from
/// <c>Majik.Core/CardData/Cards/star-compass.json</c> through
/// <see cref="CardDefinitionFactory"/>, matching the JSON-driven card cycle.
///
/// The mana clause is modelled as five colour-specific <see cref="ManaAbility"/>
/// slots (one per WUBRG) — same sibling shape as Springleaf Drum / Chromatic
/// Star / Mox Opal: the activator picks a colour by picking the matching
/// ability slot, so no separate colour prompt is needed (CR 605.1 — mana
/// abilities don't use the stack).
///
/// "...of any color that a <b>basic land you control</b> could produce" is a
/// dynamic gate (CR 305.6 — each basic-land subtype produces a fixed colour:
/// Plains→W, Island→U, Swamp→B, Mountain→R, Forest→G). Each colour slot's
/// <c>canActivateCheck</c> scans the controller's battlefield for a basic land
/// carrying the matching subtype and is only active while at least one such
/// land is present, in addition to the standard "Star Compass is untapped"
/// gate. Wastes (→{C}) is intentionally excluded: {C} is colourless, not a
/// colour (CR 105.1 / 105.2a), so it can never satisfy "any color".
///
/// ## Enters tapped
///
/// "This artifact enters tapped." is an unconditional ETB-tapped clause
/// (CR 614.1c) applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> off the seed's oracle
/// text — same posture as Commercial District and the Bloomburrow tap lands.
/// This factory builds the artifact untapped for test convenience (callers
/// that need the live ETB-tapped behaviour drive it through the binder chain).
/// </summary>
[CardName("Star Compass")]
public static class StarCompassFactory
{
    public const string CardName = "Star Compass";
    public const string PrintedManaCost = "{2}";

    // CR 305.6 — basic-land subtype → the single colour its intrinsic mana
    // ability produces. Wastes (→{C}) is omitted: {C} is colourless, not a
    // colour, so it never satisfies "any color" (CR 105.1).
    private static readonly (string Pip, CardSubtype Subtype)[] ColorSubtypes =
    {
        ("W", CardSubtype.Plains),
        ("U", CardSubtype.Island),
        ("B", CardSubtype.Swamp),
        ("R", CardSubtype.Mountain),
        ("G", CardSubtype.Forest),
    };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("star-compass");

    /// <summary>
    /// Construct Star Compass owned and controlled by <paramref name="owner"/>,
    /// with all five colour-specific mana abilities attached.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var compass = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        foreach (var (pip, subtype) in ColorSubtypes)
        {
            compass.AddAbility(BuildColorAbility(compass, owner, pip, subtype));
        }

        return compass;
    }

    /// <summary>
    /// Build one colour's <see cref="ManaAbility"/> slot. The slot is only
    /// activatable while Star Compass is untapped <i>and</i> the controller
    /// controls a basic land of <paramref name="subtype"/> (CR 305.6 /
    /// "could produce").
    /// </summary>
    private static ManaAbility BuildColorAbility(
        Permanent source, Player controller, string colorPip, CardSubtype subtype)
        => new(
            source: source,
            controller: controller,
            manaGenerated: ManaCost.Parse(colorPip),
            canActivateCheck: () => !source.IsTapped
                                    && ControlsBasicLandSubtype(controller, subtype));

    /// <summary>
    /// True iff <paramref name="controller"/> controls at least one battlefield
    /// permanent that is a land carrying basic-land subtype
    /// <paramref name="subtype"/> (CR 305.6 — a Mountain "could produce" {R},
    /// etc.).
    /// </summary>
    private static bool ControlsBasicLandSubtype(Player controller, CardSubtype subtype)
        => controller.Zones.Battlefield.GetCards()
            .Any(c => c.HasType(CardType.Land) && c.HasSubtype(subtype));

    /// <summary>
    /// Test/agent helper: return the colour slot for <paramref name="colorPip"/>
    /// (one of W/U/B/R/G) on <paramref name="compass"/>.
    /// </summary>
    public static ManaAbility AbilityForColor(Artifact compass, string colorPip)
    {
        ArgumentNullException.ThrowIfNull(compass);

        var target = ManaCost.Parse(colorPip);
        return compass.Abilities
            .OfType<ManaAbility>()
            .Single(ma => ma.ManaGenerated.Equals(target));
    }
}
