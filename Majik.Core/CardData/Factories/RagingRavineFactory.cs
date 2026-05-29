using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Raging Ravine (Worldwake creature-land cycle).
///
/// Land. Oracle text (verified against Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {R} or {G}.
///    {2}{R}{G}: Until end of turn, this land becomes a 3/3 red and green
///    Elemental creature with \"Whenever this creature attacks, put a
///    +1/+1 counter on it.\" It's still a land."
///
/// Shares the Worldwake / BFZ / OGW manland shape used by
/// <see cref="StirringWildwoodFactory"/> / <see cref="NeedleSpiresFactory"/>:
/// unconditional ETB-tapped (CR 614.1c), two mana abilities (one per
/// colour, CR 605.1), and a {cost}: animate-until-EOT
/// <see cref="ActivatedAbility"/> whose resolution registers a
/// <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — add Creature +
/// Elemental subtype) and a <see cref="ManlandCycleBecomesPTEffect"/>
/// (Layer 7b — set base P/T 3/3), both flagged
/// <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
/// <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
/// step) lifts the animation.
///
/// ## What is unique to Raging Ravine vs the rest of the cycle
/// The animated body has an intrinsic triggered ability — "Whenever this
/// creature attacks, put a +1/+1 counter on it." (CR 508.1f). v1 wires it
/// as a <see cref="TriggeredAbility"/> built from
/// <see cref="Triggers.OnAttackSelf"/> whose effect adds one
/// <see cref="CounterType.PlusOnePlusOne"/> counter to the land. This
/// mirrors the Reckoner Bankbuster / Territorial Kavu attack-trigger shape:
/// the ability is attached to the land and registered with the supplied
/// <see cref="TriggerManager"/> at animate-resolution time.
///
/// ## v1 posture (shared with the manland cycle)
/// - <b>Red/green colour identity of the animated form</b> — the engine has
///   no Layer-5 colour-set primitive yet (same gap as Creeping Tar Pit /
///   Needle Spires). Recorded only in the effect-name string.
/// - <b>Combat math through Compute</b> — same gap as every other manland:
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
///   <see cref="PermanentCharacteristics"/> row for a Land runtime instance,
///   so the 3/3 is recorded for inspection but doesn't surface for combat
///   resolution yet.
/// - <b>Granted attack trigger lifecycle</b> — v1 attaches the trigger at
///   animate-resolution and (matching Reckoner Bankbuster / Territorial
///   Kavu) leaves it attached. Because the trigger only fires on a
///   <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/> for
///   this source — which can only occur while the land is the animated
///   creature — it is observationally equivalent to a trigger that exists
///   only "until end of turn"; a land can't attack once the animation
///   expires (CR 508.1a — only creatures attack).
/// </summary>
[CardName("Raging Ravine")]
public static class RagingRavineFactory
{
    public const string CardName = "Raging Ravine";

    /// <summary>
    /// Construct Raging Ravine with no <see cref="ContinuousEffectsService"/>,
    /// <see cref="ReplacementBus"/>, or <see cref="TriggerManager"/> wired.
    /// The mana abilities + the animate ability are attached so the card
    /// surface is complete; the layer effects + granted attack trigger are
    /// not registered, and the ETB-tapped replacement is omitted (single-arg
    /// shape-only path, matching the rest of the cycle).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null, triggers: null);

    /// <summary>
    /// Construct Raging Ravine.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 + Layer
    /// 7b registration of the animate ability. May be null — the ability
    /// still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// enters-tapped rider (CR 614.1c). May be null.</param>
    /// <param name="triggers">Trigger manager the granted "Whenever this
    /// creature attacks, put a +1/+1 counter on it" ability is registered
    /// with at animate-resolution. May be null — the trigger is still
    /// attached to the land's ability list but won't fire from the event
    /// bus.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB-tapped restriction (CR 614.1c) — "This land enters tapped."
        // Unconditional; no gate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {R}  /  {T}: Add {G}
        // CR 605.1 — mana abilities do not use the stack. Modelled as two
        // distinct mana abilities (same pattern as Lavaclaw Reaches /
        // Creeping Tar Pit).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));

        // ----------------------------------------------------------------
        // {2}{R}{G}: Until end of turn, this land becomes a 3/3 red and
        // green Elemental creature with "Whenever this creature attacks,
        // put a +1/+1 counter on it." It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Resolution
        // registers Layer 4 + Layer 7b continuous effects flagged
        // ExpiresAtEndOfTurn (CR 514.2), and grants the attack trigger.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 3/3 red and green Elemental creature with " +
            "\"Whenever this creature attacks, put a +1/+1 counter on it.\" until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Elemental subtype. Printed
                // Land type stays ("it's still a land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land, keywords: Array.Empty<string>()));

                // Layer 7b — set base P/T 3/3.
                effects.Register(new ManlandCycleBecomesPTEffect(land, 3, 3));

                // CR 508.1f — granted attack trigger: "Whenever this
                // creature attacks, put a +1/+1 counter on it." Same shape
                // as Reckoner Bankbuster / Territorial Kavu.
                var counterEffect = new Effect(
                    $"{CardName}: put a +1/+1 counter on itself",
                    () =>
                    {
                        if (land.Zone != ZoneType.Battlefield) return;
                        land.Counters.Add(CounterType.PlusOnePlusOne, 1);
                    });

                var attackTrigger = new TriggeredAbility(
                    source: land,
                    controller: owner,
                    condition: Triggers.OnAttackSelf(land),
                    effects: new IEffect[] { counterEffect },
                    activeZones: new[] { ZoneType.Battlefield });

                land.AddAbility(attackTrigger);
                triggers?.RegisterTriggeredAbility(attackTrigger);
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}{R}{G}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
