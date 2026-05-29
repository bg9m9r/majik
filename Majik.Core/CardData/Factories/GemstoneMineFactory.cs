using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gemstone Mine (Weatherlight / reprints).
///
/// Land. Oracle text (verified against the embedded Modern seed
/// 2026-05-29):
///   "This land enters with three mining counters on it.
///    {T}, Remove a mining counter from this land: Add one mana of any
///    color. If there are no mining counters on this land, sacrifice it."
///
/// ## Implemented (v1)
/// - Land with correct identity / owner / controller (nonbasic, no
///   printed subtype/supertype).
/// - <b>ETB trigger</b> (CR 603.6a) — "This land enters with three mining
///   counters on it." Modelled as a self-ETB <see cref="TriggeredAbility"/>
///   over <see cref="Triggers.OnEnterBattlefieldSelf"/>; the resolution
///   body adds three <see cref="CounterType.Mining"/> counters. Same
///   posture as <see cref="BlastZoneFactory"/> / <see cref="AetherHubFactory"/>:
///   a true CR 614.1d "enters with N counters" replacement only handles
///   +1/+1 counters today (<see cref="Majik.Core.Effects.EntersWithCountersReplacement"/>),
///   so an ETB-trigger shape is used for non-+1/+1 counter types.
/// - <b>{T}, Remove a mining counter from this land: Add one mana of any
///   color</b> — five <see cref="ManaAbility"/> instances (one per WUBRG),
///   the same "any color" pattern as <see cref="GlimmervoidFactory"/> /
///   <see cref="AetherHubFactory"/> / <see cref="ChromaticStarFactory"/>.
///   Each ability uses the (source, controller, manaGenerated,
///   canActivateCheck, additionalCostPayer) overload:
///     - <c>canActivateCheck</c> = untapped AND on the battlefield AND at
///       least one mining counter present (CR 119.4 — you can't pay a cost
///       you can't afford; removing a mining counter is part of the
///       activation cost).
///     - <c>additionalCostPayer</c> removes one mining counter, then —
///       per the printed "If there are no mining counters on this land,
///       sacrifice it" rider — sacrifices Gemstone Mine (CR 701.16) when
///       none remain. The bot's source-picker chooses whichever colour a
///       cost needs at payment time.
///
///   The "If there are no mining counters … sacrifice it" clause is part
///   of the same mana-ability resolution (CR 605.1 — mana abilities do not
///   use the stack), so the removal + the no-counters self-sacrifice both
///   happen atomically when the ability is activated. This mirrors
///   Chromatic Star's inline sacrifice in the <c>additionalCostPayer</c>.
///
/// ## Deferred (v1 gaps)
/// - <b>Single modal-colour mana ability</b>: "Add one mana of any color"
///   is bound as five separate <see cref="ManaAbility"/> instances — same
///   posture as Glimmervoid / Aether Hub / City of Brass. A single
///   choose-at-activation modal-colour ability is not yet in the engine.
/// - <b>Live TriggerManager wiring</b>: the single-arg factory attaches
///   the ETB trigger to the card but does NOT register it with a
///   <see cref="TriggerManager"/>. Tests fire the effect directly. The
///   (owner, triggers) overload registers it so bus-driven firing works
///   end-to-end (mirrors Aether Hub / Chromatic Star's two-arg pattern).
/// </summary>
[CardName("Gemstone Mine")]
public static class GemstoneMineFactory
{
    public const string CardName = "Gemstone Mine";

    private const int EntersWithMiningCounters = 3;

    /// <summary>
    /// Construct Gemstone Mine with no live trigger-manager wiring. The
    /// ETB mining-counter trigger is attached for shape inspection; tests
    /// fire it by invoking the effect directly. Suitable for dispatcher /
    /// shape tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Gemstone Mine with optional trigger-manager wiring. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so the bus surfaces it automatically (mirrors Aether
    /// Hub / Chromatic Star's two-arg pattern).
    /// </summary>
    public static Land Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Nonbasic land, no supertype, no printed subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB: "This land enters with three mining counters on it."
        // CR 603.6a / CR 614.1d in spirit. Modelled as an ETB triggered
        // ability on self (non-+1/+1 counter type → trigger-shape, same
        // posture as Blast Zone / Aether Hub).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enters with {EntersWithMiningCounters} mining counters",
            () =>
            {
                if (land.Zone != ZoneType.Battlefield) return;
                land.Counters.Add(CounterType.Mining, EntersWithMiningCounters);
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
        // {T}, Remove a mining counter from this land: Add one mana of any
        // color. If there are no mining counters on this land, sacrifice it.
        //
        // CR 605.1 — mana ability, no stack. Five ManaAbility instances
        // (one per WUBRG); the source-picker chooses the colour at payment
        // time. Each:
        //   - canActivateCheck: untapped + on battlefield + ≥1 mining
        //     counter (the remove-a-mining-counter cost must be payable —
        //     CR 119.4).
        //   - additionalCostPayer: remove one mining counter, then if none
        //     remain, sacrifice Gemstone Mine (CR 701.16). Both happen in
        //     the same atomic activation step.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => !land.IsTapped
                                        && land.Zone == ZoneType.Battlefield
                                        && land.Counters.Count(CounterType.Mining) >= 1,
                additionalCostPayer: _ => PayRemoveMiningCounterAndMaybeSacrifice(land, owner)));
        }

        return land;
    }

    /// <summary>
    /// Pay the "Remove a mining counter from this land" activation cost,
    /// then enforce the "If there are no mining counters on this land,
    /// sacrifice it" rider (CR 701.16). Idempotent against the zone guard.
    /// </summary>
    private static void PayRemoveMiningCounterAndMaybeSacrifice(Land land, Player owner)
    {
        // Remove one mining counter (the printed activation cost).
        land.Counters.Remove(CounterType.Mining, 1);

        // "If there are no mining counters on this land, sacrifice it."
        if (land.Counters.Count(CounterType.Mining) > 0) return;

        if (land.Zone != ZoneType.Battlefield) return;

        // CR 701.16 — sacrifice: controller's battlefield → owner's
        // graveyard (same inline-move posture as Chromatic Star / Lotus
        // Petal; the engine's generic sacrifice path is a no-op stub).
        var controller = land.Controller ?? owner;
        var graveyardOwner = land.Owner ?? owner;
        controller.Zones.Battlefield.RemoveCard(land);
        graveyardOwner.Zones.Graveyard.AddCard(land);
        land.SetZone(ZoneType.Graveyard);
    }
}
