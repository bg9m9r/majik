using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aether Hub (Kaladesh).
///
/// Land. Oracle text:
///   "Aether Hub enters with an energy counter on it.
///    {T}: Add {C}.
///    {T}, Pay {E}: Add one mana of any color."
///
/// ## Implemented (v1)
/// - Land with correct identity / owner / controller.
/// - <b>ETB trigger</b> (CR 603.6a) — "enters with an energy counter on
///   it." Wired as a self-ETB <see cref="TriggeredAbility"/>. The
///   resolution body grants the controller one energy via
///   <see cref="Player.GainEnergy"/> (CR 106.13 — energy is a
///   player-scoped resource) AND also stamps a
///   <see cref="CounterType.Energy"/> marker onto the land's
///   <see cref="Permanent.Counters"/> bag for shape inspection. The
///   printed wording uses "counter on it" oracle phrasing, but the
///   modern reading (CR 106.13b) treats energy as the
///   player-scoped resource the {E} pip pays out of — both paths are
///   surfaced so callers can observe either invariant. Strict CR 122.1g
///   "enters with N counters" replacement (Murktide / Chalice shape)
///   is NOT used here — Aether Hub's ETB is a triggered ability per
///   the printed Oracle text update, not an entering-with-counters
///   replacement, so the single-energy-on-ETB ledgers through the
///   normal ETB-trigger path.
/// - <b>{T}: Add {C}</b> — first <see cref="ManaAbility"/> wired. {C}
///   currently rolls into the generic bucket per
///   <see cref="ManaCost.Parse"/> (see ManaCost.cs:170).
/// - <b>{T}, Pay {E}: Add one mana of any color</b> — five
///   <see cref="ManaAbility"/> instances, one per WUBRG (same shape as
///   Cavern of Souls / Delighted Halfling). Each carries the
///   3-arg-plus-cost overload of <see cref="ManaAbility"/>: the
///   <c>canActivateCheck</c> requires <c>controller.EnergyCounters &gt;= 1</c>
///   (CR 119.4 — you can't pay a resource you don't have) AND the
///   land to be untapped (paid by the printed {T}); the
///   <c>additionalCostPayer</c> performs the energy spend via
///   <see cref="Player.PayEnergy(int)"/> after the land taps. Player /
///   bot picks whichever colour is needed when paying mana costs (the
///   source-picker already scans abilities by produced colour).
///
/// ## Deferred (v1 gaps)
/// - <b>Live TriggerManager wiring</b>: the single-arg factory attaches
///   the ETB trigger to the card but does NOT register it with a
///   <see cref="TriggerManager"/>. Tests fire the trigger manually or
///   invoke the effect directly. The (owner, eventBus, triggers)
///   overload registers the trigger so bus-driven firing works
///   end-to-end (mirrors The One Ring's two-arg pattern).
/// - <b>"Pay {E}" cost as a "you may" prompt</b>: in MTG the player
///   chooses whether to activate; the engine doesn't auto-decide here.
///   The activation gate (<c>EnergyCounters &gt;= 1</c>) only enforces
///   legality, not willingness. Bot's source-picker treats the
///   energy-paying abilities like any other mana ability — when it
///   picks this source to pay a coloured pip, the energy spend
///   happens silently (same posture as Fiery Islet's "Pay 1 life"
///   gate).
/// - <b>Symbolic on-card energy counter cleanup</b>: the
///   <see cref="CounterType.Energy"/> marker stamped at ETB is never
///   removed when the controller spends energy (the player-scoped
///   ledger is the source of truth). Removing the marker on spend
///   would require threading the land reference into PayEnergy or
///   adding a per-card energy-tracker; intentionally skipped — the
///   marker is bookkeeping only.
/// </summary>
[CardName("Aether Hub")]
public static class AetherHubFactory
{
    public const string CardName = "Aether Hub";

    /// <summary>
    /// Construct Aether Hub with no live bus / trigger-manager wiring.
    /// The ETB trigger is attached for shape inspection; tests fire it
    /// by invoking the effect directly. Suitable for dispatcher /
    /// shape tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Aether Hub with optional event bus + trigger manager.
    /// When <paramref name="triggers"/> is supplied, the ETB trigger
    /// is registered so the bus surfaces it automatically.
    /// </summary>
    public static Land Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "Aether Hub enters with an energy counter on it."
        // Resolution: grant the controller one energy (CR 106.13 — the
        // player-scoped resource the {E} pip pays out of) and also
        // stamp an Energy CounterType on the land for shape inspection.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Aether Hub: enters with an energy counter (player gains {E})",
            () =>
            {
                var controller = land.Controller ?? owner;
                controller.GainEnergy(1);
                land.Counters.Add(CounterType.Energy);
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
        // {T}: Add {C}
        // CR 605.1 — mana abilities don't use the stack. {C} lands as
        // +1 generic via ManaCost.Parse (see ManaCost.cs:170).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}, Pay {E}: Add one mana of any color
        //   Modelled as 5 ManaAbility instances (one per WUBRG) — same
        //   pattern as Cavern of Souls / Delighted Halfling. Each
        //   carries:
        //     - canActivateCheck: untapped AND controller has ≥1 energy
        //       (CR 119.4 — you can't pay a resource you don't have)
        //     - additionalCostPayer: spend one energy
        //       (Player.PayEnergy(1)) after the tap pays {T}
        //   The mana picker chooses whichever colour is needed when
        //   paying spell costs.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            var mana = ManaCost.Parse(color);
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: mana,
                canActivateCheck: () =>
                {
                    if (land.IsTapped) return false;
                    var controller = land.Controller ?? owner;
                    return controller.EnergyCounters >= 1;
                },
                additionalCostPayer: controller => controller.PayEnergy(1)));
        }

        return land;
    }
}
