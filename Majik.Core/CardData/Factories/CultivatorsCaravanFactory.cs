using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cultivator's Caravan (Kaladesh, {3}).
///
/// Artifact — Vehicle 5/5. Oracle text (verified against Scryfall):
///   "{T}: Add one mana of any color."
///   "Crew 3 (Tap any number of creatures you control with total power 3 or
///    more: This Vehicle becomes an artifact creature until end of turn.)"
///
/// ## Implemented (v1)
/// - Shell follows the Vehicle MVP convention (mirrors
///   <see cref="SmugglersCopterFactory"/> / <see cref="EsikasChariotFactory"/>):
///   a <see cref="Creature"/> with <see cref="Cards.Types.CardType.Artifact"/>
///   additively stamped (CR 301.1 / 302.1) and the
///   <see cref="Cards.Types.CardSubtype.Vehicle"/> subtype. Base P/T 5/5 —
///   <see cref="CardData.Vehicles.CrewAction"/> ships this through
///   <see cref="Majik.Core.Effects.VehicleCrewEffect"/> when crewed. Not
///   legendary on its printed face. The <c>["Creature", "Artifact"]</c>
///   type order in the JSON makes
///   <see cref="CardDefinitionFactory"/> build a <see cref="Creature"/>
///   primary class with Artifact stamped on top — the same multi-type shape
///   the imperative vehicle factories produce.
/// - <b>{T}: Add one mana of any color</b> (CR 605.1): five free
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances (one per
///   WUBRG), each with no additional cost — the "Add one mana of any color"
///   filter posture used by Chromatic Star / Springleaf Drum. Unlike
///   <see cref="PrismaticLensFactory"/> there is NO {1} additional cost on
///   the caravan's mana ability, so each colour ability is free; the bot's
///   source-picker selects the colour at payment time. CR 605.1 — mana
///   abilities never use the stack.
///
/// ## Crew 3 (CR 702.122)
/// Surfaced via <see cref="CrewCost"/> so callers route through
/// <see cref="CardData.Vehicles.CrewAction.Crew"/> exactly as Smuggler's
/// Copter / Esika's Chariot do. Crew is structural data on this factory (no
/// activated-ability surface yet — the engine's <c>CrewAction</c> is invoked
/// directly by tests / bots, same shape as the rest of the Vehicle MVP).
///
/// ## Deferred (v1 gaps — shared with the rest of the Vehicle MVP)
/// - <b>Vehicle-as-non-creature off the battlefield</b>: the shell is a
///   <see cref="Creature"/>, so non-battlefield zone inspections see a
///   "creature card" type the printed face doesn't have until crewed. Same
///   v1 simplification used by every Vehicle modelled today (see
///   <see cref="CardData.Vehicles.CrewActionTests"/>).
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/cultivators-caravan.json</c> and builds
/// through <see cref="CardDefinitionFactory"/>. The only engine shapes used
/// are a multi-type Creature shell and vanilla (no-extra-cost) mana
/// abilities — both already supported.
/// </summary>
[CardName("Cultivator's Caravan")]
public static class CultivatorsCaravanFactory
{
    public const string CardName = "Cultivator's Caravan";

    /// <summary>Crew cost (CR 702.122) — total power 3 or more.</summary>
    public const int CrewCost = 3;

    /// <summary>Vehicle base power, shipped through
    /// <see cref="Majik.Core.Effects.VehicleCrewEffect"/> when crewed.</summary>
    public const int VehiclePower = 5;

    /// <summary>Vehicle base toughness.</summary>
    public const int VehicleToughness = 5;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("cultivators-caravan");

    /// <summary>Construct Cultivator's Caravan owned and controlled by
    /// <paramref name="owner"/>. The five colour mana abilities are attached
    /// for shape inspection / activation; crewing is driven via
    /// <see cref="CardData.Vehicles.CrewAction.Crew"/> by the caller.</summary>
    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
