using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sphere of the Suns (New Phyrexia / Mirrodin
/// Besieged, {2}).
///
/// Artifact. Oracle text (Scryfall, verified 2026-05-29):
///   "This artifact enters tapped and with three charge counters on it.
///    {T}, Remove a charge counter from this artifact: Add one mana of any
///    color."
///
/// The charge-counter-gated five-colour fixing rock — Star Compass /
/// Pentad Prism's WUBRG mana suite combined with Reckoner Bankbuster's
/// "enters with three charge counters" ETB. A {T} mana rock that can only
/// produce three coloured pips over its lifetime (one charge counter spent
/// per activation, plus the {T}).
///
/// ## Implementation
///
/// Card identity (Artifact, {2}) is loaded from
/// <c>Majik.Core/CardData/Cards/sphere-of-the-suns.json</c> through
/// <see cref="CardDefinitionFactory"/>, matching the JSON-driven card cycle
/// (same posture as <see cref="StarCompassFactory"/>).
///
/// ## Enters with three charge counters (CR 122 / CR 614.1d)
///
/// "enters ... with three charge counters on it" is modelled as an ETB
/// <see cref="TriggeredAbility"/> placing three <see cref="CounterType.Charge"/>
/// counters at battlefield entry — same shape <see cref="ReckonerBankbusterFactory"/>
/// uses. The strict CR 614.1d "enters with N counters" replacement only
/// carries the +1/+1 channel today
/// (<see cref="Majik.Core.Effects.EntersWithCountersReplacement"/>), so the
/// trigger-shape is used for charge counters (same posture as Blast Zone /
/// Reckoner Bankbuster). The trigger registers with a live
/// <see cref="TriggerManager"/> when one is supplied.
///
/// ## {T}, Remove a charge counter: Add one mana of any color (CR 605.1)
///
/// Five <see cref="ManaAbility"/> instances (one per WUBRG) — the same modal
/// colour shape as <see cref="PentadPrismFactory"/> / Chromatic Star / Mox
/// Opal; the activator picks a colour by picking the matching ability slot,
/// so no separate colour prompt is needed (CR 605.1 — mana abilities don't
/// use the stack). Unlike Pentad Prism, Sphere's cost DOES include {T}, so
/// the standard tap-as-cost overload is used (<c>tapsAsCost</c> defaults to
/// true). Each slot is gated on:
///   (1) the sphere is still on the battlefield, AND
///   (2) the sphere is untapped (the printed {T} cost), AND
///   (3) the sphere has at least one charge counter to remove
///       (CR 605.3a — the cost must be payable).
/// The <c>additionalCostPayer</c> removes one charge counter inline
/// (CR 121.5 / CR 602.1 — paid up front in the same atomic step as mana
/// production). Because {T} taps the sphere, only one coloured pip can be
/// produced per untap step regardless of remaining charge counters — the
/// three-counter lifetime is spent one per turn-cycle in practice.
///
/// ## Enters tapped (CR 614.1c)
///
/// "This artifact enters tapped." is an unconditional ETB-tapped clause
/// applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> off the seed's
/// oracle text — same posture as <see cref="StarCompassFactory"/> /
/// Commercial District / the Bloomburrow tap lands. This factory builds the
/// artifact untapped for test convenience (callers that need the live
/// ETB-tapped behaviour drive it through the binder chain).
/// </summary>
[CardName("Sphere of the Suns")]
public static class SphereOfTheSunsFactory
{
    public const string CardName = "Sphere of the Suns";
    public const string PrintedManaCost = "{2}";
    public const int StartingChargeCounters = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sphere-of-the-suns");

    /// <summary>
    /// Construct Sphere of the Suns with no live trigger-manager wiring. The
    /// ETB "three charge counters" trigger is attached for shape
    /// observability; the five WUBRG mana abilities are attached. Suitable
    /// for shape / <see cref="NamedCardFactory"/> dispatch tests.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Sphere of the Suns. When <paramref name="triggers"/> is
    /// supplied, the ETB "enters with three charge counters" trigger is
    /// registered so the centralised ETB event queues it automatically.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var sphere = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB — "enters ... with three charge counters on it." (CR 122 /
        // CR 614.1d.) Modelled as an ETB TriggeredAbility because
        // EntersWithCountersReplacement only covers +1/+1 today — same
        // posture as Reckoner Bankbuster / Blast Zone.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enter with {StartingChargeCounters} charge counters",
            () =>
            {
                if (sphere.Zone != ZoneType.Battlefield) return;
                sphere.Counters.Add(CounterType.Charge, StartingChargeCounters);
            });

        var etbTrigger = new TriggeredAbility(
            source: sphere,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(sphere),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        sphere.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {T}, Remove a charge counter from this artifact: Add one mana of
        // any color. (CR 605.1 — mana ability; CR 605.3b — doesn't use the
        // stack.)
        //
        // Five ManaAbility instances (one per WUBRG) — same modal-colour
        // shape as Pentad Prism / Chromatic Star / Mox Opal. The activation
        // cost is {T} PLUS "remove a charge counter", so the standard
        // tap-as-cost overload is used (tapsAsCost defaults to true — the
        // engine taps in ManaAbility.Activate). Each is gated on:
        //   (1) the sphere is still on the battlefield, AND
        //   (2) the sphere is untapped (so {T} is payable), AND
        //   (3) the sphere has at least one charge counter to remove
        //       (CR 605.3a — the cost must be payable).
        // The additionalCostPayer removes one charge counter inline.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            sphere.AddAbility(new ManaAbility(
                source: sphere,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => sphere.Zone == ZoneType.Battlefield
                                        && !sphere.IsTapped
                                        && sphere.Counters.Count(CounterType.Charge) > 0,
                additionalCostPayer: _ => RemoveOneChargeCounter(sphere)));
        }

        return sphere;
    }

    /// <summary>
    /// CR 121.5 / CR 602.1 — pay part of the activation cost by removing one
    /// charge counter from the sphere. Defensive against an empty pool (the
    /// canActivateCheck gate makes that unreachable in practice).
    /// </summary>
    private static void RemoveOneChargeCounter(Artifact sphere)
    {
        if (sphere.Counters.Count(CounterType.Charge) <= 0) return;
        sphere.Counters.Remove(CounterType.Charge, 1);
    }
}
