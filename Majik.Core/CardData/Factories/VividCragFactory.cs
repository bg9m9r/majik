using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vivid Crag (Lorwyn "Vivid" land cycle).
///
/// Land. Oracle text (Scryfall, verified 2026-06-02):
///   "This land enters tapped with two charge counters on it.
///    {T}: Add {R}.
///    {T}, Remove a charge counter from this land: Add one mana of any
///    color."
///
/// The charge-counter-fuelled five-colour fixing tapland — Sphere of the
/// Suns' "remove a charge counter: any colour" suite (one charge counter
/// spent per off-colour activation), but printed on a land that ALSO taps
/// for its base colour ({R}) with no counter cost. Two charge counters means
/// at most two off-colour pips over its lifetime; after that it remains a
/// mono-red tapland.
///
/// ## Implementation
///
/// Card identity (Land, no supertype / subtype) is loaded from
/// <c>Majik.Core/CardData/Cards/vivid-crag.json</c> through
/// <see cref="CardDefinitionFactory"/>, matching the JSON-driven card cycle
/// (same posture as <see cref="SphereOfTheSunsFactory"/>).
///
/// ## Enters with two charge counters (CR 122 / CR 614.1d)
///
/// "enters ... with two charge counters on it" is modelled as an ETB
/// <see cref="TriggeredAbility"/> placing two <see cref="CounterType.Charge"/>
/// counters at battlefield entry — same shape <see cref="SphereOfTheSunsFactory"/>
/// / Reckoner Bankbuster use. The strict CR 614.1d "enters with N counters"
/// replacement only carries the +1/+1 channel today
/// (<see cref="Majik.Core.Effects.EntersWithCountersReplacement"/>), so the
/// trigger-shape is used for charge counters (same posture as Blast Zone /
/// Sphere of the Suns). The trigger registers with a live
/// <see cref="TriggerManager"/> when one is supplied.
///
/// ## {T}: Add {R} (CR 605.1)
///
/// One plain <see cref="ManaAbility"/> producing {R} with the standard
/// tap-as-cost overload (no counter cost). Gated on untapped + on the
/// battlefield. This is the base colour; it does NOT consume a charge
/// counter (distinguishing Vivid Crag from Sphere of the Suns, whose only
/// mana ability is the counter-gated any-colour one).
///
/// ## {T}, Remove a charge counter: Add one mana of any color (CR 605.1)
///
/// Five <see cref="ManaAbility"/> instances (one per WUBRG) — the same modal
/// colour shape as Sphere of the Suns / Pentad Prism / Chromatic Star; the
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
/// production). Because {T} taps the land, all six mana abilities share the
/// single tap, so only ONE mana of any kind can be produced per untap step.
///
/// ## Enters tapped (CR 614.1c)
///
/// "This land enters tapped ..." is an unconditional ETB-tapped clause
/// applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> off the seed's
/// oracle text — same posture as <see cref="SphereOfTheSunsFactory"/> /
/// the Refuge / Temple cycle. This factory builds the land untapped for test
/// convenience (callers that need the live ETB-tapped behaviour drive it
/// through the binder chain).
/// </summary>
[CardName("Vivid Crag")]
public static class VividCragFactory
{
    public const string CardName = "Vivid Crag";
    public const int StartingChargeCounters = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("vivid-crag");

    /// <summary>
    /// Construct Vivid Crag with no live trigger-manager wiring. The ETB
    /// "two charge counters" trigger is attached for shape observability; the
    /// base {R} mana ability and the five WUBRG any-colour mana abilities are
    /// attached. Suitable for shape / <see cref="NamedCardFactory"/> dispatch
    /// tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Vivid Crag. When <paramref name="triggers"/> is supplied,
    /// the ETB "enters with two charge counters" trigger is registered so the
    /// centralised ETB event queues it automatically.
    /// </summary>
    public static Land Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB — "enters ... with two charge counters on it." (CR 122 /
        // CR 614.1d.) Modelled as an ETB TriggeredAbility because
        // EntersWithCountersReplacement only covers +1/+1 today — same
        // posture as Sphere of the Suns / Blast Zone.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enter with {StartingChargeCounters} charge counters",
            () =>
            {
                if (land.Zone != ZoneType.Battlefield) return;
                land.Counters.Add(CounterType.Charge, StartingChargeCounters);
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
        // {T}: Add {R}. (CR 605.1 — mana ability, no stack.)
        //
        // The base colour. Standard tap-as-cost overload; NO charge counter
        // is consumed. Gated on untapped + on the battlefield.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("R"),
            canActivateCheck: () => land.Zone == ZoneType.Battlefield
                                    && !land.IsTapped));

        // ----------------------------------------------------------------
        // {T}, Remove a charge counter from this land: Add one mana of any
        // color. (CR 605.1 — mana ability; CR 605.3b — doesn't use the
        // stack.)
        //
        // Five ManaAbility instances (one per WUBRG) — same modal-colour
        // shape as Sphere of the Suns / Pentad Prism / Chromatic Star. The
        // activation cost is {T} PLUS "remove a charge counter", so the
        // standard tap-as-cost overload is used (tapsAsCost defaults to true).
        // Each is gated on:
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
    /// CR 121.5 / CR 602.1 — pay part of the activation cost by removing one
    /// charge counter from the land. Defensive against an empty pool (the
    /// canActivateCheck gate makes that unreachable in practice).
    /// </summary>
    private static void RemoveOneChargeCounter(Land land)
    {
        if (land.Counters.Count(CounterType.Charge) <= 0) return;
        land.Counters.Remove(CounterType.Charge, 1);
    }
}
