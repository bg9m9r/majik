using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Survivors' Encampment (Hour of Devastation).
///
/// Land — Desert, no mana cost. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {T}, Tap an untapped creature you control: Add one mana of any color."
///
/// Survivors' Encampment is the functional twin of Holdout Settlement; the
/// only difference is the Desert land subtype (CR 205.3i). The
/// tap-a-creature any-colour mode is the identical mana-ability shape used
/// by Springleaf Drum, so this factory reuses
/// <see cref="SpringleafDrumFactory.BuildAnyColorAbility"/> for the five
/// WUBRG slots and lets the JSON declare the vanilla {C} mode + identity.
///
/// <para>
/// The Land shell (identity / Desert subtype / owner / controller) and the
/// vanilla <c>{T}: Add {C}</c> mana ability are declared declaratively in
/// <c>Majik.Core/CardData/Cards/survivors-encampment.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, the same posture
/// as <see cref="CityOfBrassFactory"/>. The five any-colour
/// (tap-a-creature) abilities are attached on top in C# because the
/// data-only <see cref="ManaAbilityDefinition"/> schema carries only a
/// <c>Produces</c> string — it can express neither the five-colour
/// any-colour fan-out nor the "tap an untapped creature you control"
/// additional cost. The JSON therefore declares only the {C} ability; this
/// factory adds the rest.
/// </para>
///
/// ## Implemented (v1)
/// - <b>Land — Desert identity</b> — non-basic, single
///   <see cref="Majik.Core.Cards.Types.CardSubtype.Desert"/> subtype,
///   empty mana cost (JSON).
/// - <b>{T}: Add {C}</b> — vanilla mana ability (CR 605.1, no stack)
///   declared in JSON. {C} folds into the generic bucket per
///   <c>ManaCost.Parse</c> (same posture as Rogue's Passage / Crystal
///   Grotto's {C} mode).
/// - <b>{T}, Tap an untapped creature you control: Add one mana of any
///   color</b> — five <see cref="SpringleafDrumManaAbility"/> slots (one per
///   WUBRG). Each pays the land's implicit self-{T} plus the
///   <see cref="Majik.Core.Costs.TapAnotherUntappedCreatureCost"/>
///   additional cost concurrently (CR 605.1; CR 118.12 — the second tap is
///   a tap-as-cost).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for which creature to tap</b> — inherited from
///   <see cref="Majik.Core.Costs.TapAnotherUntappedCreatureCost"/>'s
///   deterministic first-eligible fallback; agents set
///   <see cref="SpringleafDrumManaAbility.TapChoice"/>'s Target to override.
/// - <b>Agent prompt for which colour to add</b> — covered by the per-colour
///   ability shape: the activator picks the colour by picking the matching
///   ability slot, no separate prompt needed.
/// </summary>
[CardName("Survivors' Encampment")]
public static class SurvivorsEncampmentFactory
{
    public const string CardName = "Survivors' Encampment";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("survivors-encampment");

    /// <summary>
    /// Construct Survivors' Encampment owned and controlled by
    /// <paramref name="owner"/> with the {C} mana ability (from JSON) and
    /// all five any-colour (tap-a-creature) mana abilities attached. No live
    /// runtime wiring is required — the card carries no triggers / continuous
    /// effects.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Land — Desert + {T}: Add {C}, materialized from JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}, Tap an untapped creature you control: Add one mana of any
        // color. Five per-colour SpringleafDrumManaAbility slots — the
        // self-{T} and the creature-tap additional cost are paid
        // concurrently (CR 605.1; CR 118.12).
        // ----------------------------------------------------------------
        foreach (var pip in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(SpringleafDrumFactory.BuildAnyColorAbility(land, owner, pip));
        }

        return land;
    }
}
