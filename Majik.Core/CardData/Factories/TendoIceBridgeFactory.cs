using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tendo Ice Bridge (Champions of Kamigawa).
///
/// Land. Oracle text (Scryfall, verified 2026-06-02):
///   "This land enters with a charge counter on it.
///    {T}: Add {C}.
///    {T}, Remove a charge counter from this land: Add one mana of any
///    color."
///
/// A colour-fixing land with a single charge counter: it taps for {C}
/// indefinitely, but exactly once over its lifetime (one charge counter) it
/// can instead tap to filter into any one colour. Same charge-counter +
/// any-colour shape as <see cref="SphereOfTheSunsFactory"/>, plus an
/// unconditional <c>{T}: Add {C}</c> mana ability.
///
/// ## Implementation
///
/// Card identity (Land) and the <c>{T}: Add {C}</c> mana ability are loaded
/// from <c>Majik.Core/CardData/Cards/tendo-ice-bridge.json</c> through
/// <see cref="CardDefinitionFactory"/> (same JSON-driven posture as
/// <see cref="SphereOfTheSunsFactory"/> / Zagoth Triome). <c>{C}</c> folds
/// into the generic bucket — the engine has no dedicated colourless mana
/// channel today (same posture as <see cref="CrumblingVestigeFactory"/>).
///
/// ## Enters with a charge counter (CR 122 / CR 614.1d)
///
/// "enters with a charge counter on it" is modelled as an ETB
/// <see cref="TriggeredAbility"/> placing one <see cref="CounterType.Charge"/>
/// counter at battlefield entry — same shape <see cref="SphereOfTheSunsFactory"/>
/// uses. The strict CR 614.1d "enters with N counters" replacement only
/// carries the +1/+1 channel today
/// (<see cref="Majik.Core.Effects.EntersWithCountersReplacement"/>), so the
/// trigger-shape is used for charge counters (same posture as Blast Zone /
/// Sphere of the Suns). The trigger registers with a live
/// <see cref="TriggerManager"/> when one is supplied.
///
/// ## {T}, Remove a charge counter: Add one mana of any color (CR 605.1)
///
/// Five <see cref="ManaAbility"/> instances (one per WUBRG) — the same modal
/// colour shape as <see cref="SphereOfTheSunsFactory"/> / Chromatic Star; the
/// activator picks a colour by picking the matching ability slot, so no
/// separate colour prompt is needed (CR 605.1 — mana abilities don't use the
/// stack). The cost is {T} PLUS "remove a charge counter", so the standard
/// tap-as-cost overload is used (<c>tapsAsCost</c> defaults to true). Each
/// slot is gated on:
///   (1) the land is still on the battlefield, AND
///   (2) the land is untapped (the printed {T} cost), AND
///   (3) the land has at least one charge counter to remove
///       (CR 605.3a — the cost must be payable).
/// The <c>additionalCostPayer</c> removes one charge counter inline
/// (CR 121.5 / CR 602.1 — paid up front in the same atomic step as mana
/// production).
///
/// The colourless <c>{T}: Add {C}</c> ability (loaded from JSON) carries no
/// charge-counter cost, so it stays activatable for the land's whole life;
/// only the colour abilities consume the single charge counter.
/// </summary>
[CardName("Tendo Ice Bridge")]
public static class TendoIceBridgeFactory
{
    public const string CardName = "Tendo Ice Bridge";
    public const int EntersWithChargeCounters = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("tendo-ice-bridge");

    /// <summary>
    /// Construct Tendo Ice Bridge with no live trigger-manager wiring. The
    /// ETB "a charge counter" trigger is attached for shape observability; the
    /// {C} ability (from JSON) and five WUBRG colour abilities are attached.
    /// Suitable for shape / <see cref="NamedCardFactory"/> dispatch tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Tendo Ice Bridge. When <paramref name="triggers"/> is
    /// supplied, the ETB "enters with a charge counter" trigger is registered
    /// so the centralised ETB event queues it automatically.
    /// </summary>
    public static Land Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB — "enters with a charge counter on it." (CR 122 / CR 614.1d.)
        // Modelled as an ETB TriggeredAbility because
        // EntersWithCountersReplacement only covers +1/+1 today — same
        // posture as Sphere of the Suns / Blast Zone.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enter with a charge counter",
            () =>
            {
                if (land.Zone != ZoneType.Battlefield) return;
                land.Counters.Add(CounterType.Charge, EntersWithChargeCounters);
            });

        var etbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {T}, Remove a charge counter from this land: Add one mana of any
        // color. (CR 605.1 — mana ability; CR 605.3b — doesn't use the
        // stack.)
        //
        // Five ManaAbility instances (one per WUBRG) — same modal-colour
        // shape as Sphere of the Suns / Chromatic Star. The activation cost
        // is {T} PLUS "remove a charge counter", so the tap-as-cost overload
        // is used (tapsAsCost defaults to true — the engine taps in
        // ManaAbility.Activate). Each is gated on:
        //   (1) the land is still on the battlefield, AND
        //   (2) the land is untapped (so {T} is payable), AND
        //   (3) the land has at least one charge counter to remove
        //       (CR 605.3a — the cost must be payable).
        // The additionalCostPayer removes one charge counter inline.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => land.Zone == ZoneType.Battlefield
                                        && !land.IsTapped
                                        && land.Counters.Count(CounterType.Charge) > 0,
                additionalCostPayer: _ => RemoveOneChargeCounter(land)));
        }

        return land;
    }

    /// <summary>
    /// CR 121.5 / CR 602.1 — pay part of the colour ability's activation cost
    /// by removing one charge counter from the land. Defensive against an
    /// empty pool (the canActivateCheck gate makes that unreachable in
    /// practice).
    /// </summary>
    private static void RemoveOneChargeCounter(Land land)
    {
        if (land.Counters.Count(CounterType.Charge) <= 0) return;
        land.Counters.Remove(CounterType.Charge, 1);
    }
}
