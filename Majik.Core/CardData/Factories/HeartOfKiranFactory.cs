using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Heart of Kiran (Aether Revolt, {2}).
///
/// Legendary Artifact — Vehicle 4/4. Oracle text:
///   "Flying, vigilance"
///   "Crew 3 (Tap any number of creatures you control with total power 3
///    or greater: This Vehicle becomes an artifact creature until end of
///    turn.)"
///   "You may remove a loyalty counter from a planeswalker you control
///    rather than pay the crew cost for Heart of Kiran." (i.e. alt-crew
///    cost: remove 1 loyalty counter from a PW you control instead of
///    tapping creatures with total power ≥ 3.)
///
/// ## Implementation
///
/// - Shell follows the Vehicle MVP convention (mirrors
///   <see cref="EsikasChariotFactory"/> / <see cref="SmugglersCopterFactory"/>):
///   a <see cref="Creature"/> with <see cref="CardType.Artifact"/>
///   additively stamped (CR 301.1 / 302.1). Base P/T 4/4 — when crewed
///   <see cref="Majik.Core.Effects.VehicleCrewEffect"/> ships it through
///   the layers pipeline (Layer 7b). Legendary supertype + Vehicle subtype.
/// - <b>Flying</b> (CR 702.9) and <b>Vigilance</b> (CR 702.20): both
///   surfaced as <see cref="KeywordAbility"/> markers; combat code
///   consumes them via <see cref="Majik.Core.Combat.CombatAbilities"/>.
/// - <b>Crew 3</b> (CR 702.122): surfaced as <see cref="CrewCost"/> for
///   callers that route through <see cref="CardData.Vehicles.CrewAction.Crew"/>.
/// - <b>Alt-crew cost (planeswalker loyalty)</b>: exposed as
///   <see cref="CrewByRemovingLoyalty"/> — caller picks one
///   <see cref="Planeswalker"/> the controller controls with ≥ 1 loyalty,
///   we strip one loyalty counter via
///   <see cref="Planeswalker.RemoveLoyalty"/> and register the same
///   <see cref="VehicleCrewEffect"/> that <see cref="CardData.Vehicles.CrewAction"/>
///   uses. No tap, no power check — the loyalty removal substitutes for
///   the entire crew cost (printed: "rather than pay the crew cost").
///
/// ## Deferred (v1 gaps)
/// - <b>Crew as an activated ability</b>: structural data only; tests /
///   bots invoke <see cref="CardData.Vehicles.CrewAction.Crew"/> or
///   <see cref="CrewByRemovingLoyalty"/> directly, matching the rest of
///   the Vehicle MVP.
/// - <b>"You may" prompt</b> on the loyalty alt-cost: the alt-cost is
///   explicitly opt-in by being a separate entry point. There is no
///   ambient prompt machinery to surface yet.
/// </summary>
[CardName("Heart of Kiran")]
public static class HeartOfKiranFactory
{
    public const string CardName = "Heart of Kiran";
    public const string PrintedManaCost = "{2}";
    public const int CrewCost = 3;
    public const int VehiclePower = 4;
    public const int VehicleToughness = 4;

    /// <summary>
    /// Construct Heart of Kiran. No ETB / activated triggers to wire — the
    /// keywords are static markers and crew lives off the
    /// <see cref="CardData.Vehicles.CrewAction"/> / <see cref="CrewByRemovingLoyalty"/>
    /// entry points.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: VehiclePower,
            toughness: VehicleToughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Vehicle });

        // CR 301.1 / 302.1 — Heart of Kiran is an Artifact (Vehicle).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 / 702.20 — Flying + Vigilance markers.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        return card;
    }

    /// <summary>
    /// Alt-crew cost: "You may remove a loyalty counter from a planeswalker
    /// you control rather than pay the crew cost for Heart of Kiran."
    ///
    /// On success: one loyalty counter is removed from
    /// <paramref name="planeswalker"/> via
    /// <see cref="Planeswalker.RemoveLoyalty"/> (CR 122.1 / CR 306.5b);
    /// a one-turn <see cref="VehicleCrewEffect"/> (CR 702.122 / Layer 7b)
    /// is registered with <paramref name="effects"/> so the vehicle
    /// becomes a 4/4 artifact creature until end of turn.
    ///
    /// Failure cases (returns <see cref="CardData.Vehicles.CrewAction.CrewResult"/>
    /// with <c>Success = false</c>):
    /// <list type="bullet">
    ///   <item>planeswalker not controlled by the vehicle's controller — the
    ///         alt-cost is scoped to "a planeswalker you control";</item>
    ///   <item>planeswalker has zero loyalty — no counter to remove;</item>
    ///   <item>planeswalker is not actually on the battlefield (defensive
    ///         guard — alt-costs apply only to in-play permanents).</item>
    /// </list>
    /// </summary>
    public static CardData.Vehicles.CrewAction.CrewResult CrewByRemovingLoyalty(
        Creature vehicle,
        Planeswalker planeswalker,
        ContinuousEffectsService effects)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(planeswalker);
        ArgumentNullException.ThrowIfNull(effects);

        if (!ReferenceEquals(planeswalker.Controller, vehicle.Controller))
        {
            return new CardData.Vehicles.CrewAction.CrewResult(
                false, "alt-cost requires a planeswalker you control");
        }

        if (planeswalker.Loyalty <= 0)
        {
            return new CardData.Vehicles.CrewAction.CrewResult(
                false, "planeswalker has no loyalty counter to remove");
        }

        if (planeswalker.Zone != Zones.ZoneType.Battlefield)
        {
            return new CardData.Vehicles.CrewAction.CrewResult(
                false, "alt-cost source must be on the battlefield");
        }

        // CR 122.1 / CR 306.5b — remove one loyalty counter as the alt-cost.
        // CR 704.5i lethal-damage SBA on a 0-loyalty planeswalker still runs
        // through the normal SBA loop; we leave that to the engine.
        planeswalker.RemoveLoyalty(1);

        // CR 702.122 / Layer 7b — promote the vehicle to a 4/4 artifact
        // creature until end of turn. Same VehicleCrewEffect that the
        // normal Crew path registers.
        effects.Register(new VehicleCrewEffect(vehicle, VehiclePower, VehicleToughness));

        return new CardData.Vehicles.CrewAction.CrewResult(true, null);
    }
}
