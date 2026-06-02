using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pendelhaven (Legends / Modern Masters).
///
/// Legendary Land. Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {G}.
///    {T}: Target 1/1 creature gets +1/+2 until end of turn."
///
/// Same base shape as the manland / utility-land JSON wrappers (e.g.
/// <see cref="RestlessSpireFactory"/>): the plain card surface — name,
/// Legendary supertype, Land type, and the <b>{T}: Add {G}</b> mana ability
/// (CR 605.1) — is materialised from the embedded JSON definition
/// (<c>pendelhaven.json</c>) via <see cref="CardDefinitionLoader.FromEmbeddedResource"/>
/// + <see cref="CardDefinitionFactory.Build"/>. The targeted pump ability is
/// layered on here because the JSON <c>AbilityDefinition</c> schema doesn't
/// express a targeted activated pump yet.
///
/// ## Implemented (v1)
/// - <b>Legendary Land identity</b> (Legendary supertype from JSON; the
///   Legend rule, CR 704.5j, is enforced centrally in
///   <see cref="Majik.Core.Rules.StateBasedActions"/> — nothing card-specific
///   needed here).
/// - <b>{T}: Add {G}</b> — vanilla green <see cref="ManaAbility"/> from the
///   JSON definition (CR 605.1, mana abilities don't use the stack).
/// - <b>{T}: Target 1/1 creature gets +1/+2 until end of turn</b> — an
///   <see cref="ActivatedAbility"/> whose only cost is the {T} tap
///   (<see cref="AdditionalCost.Tap"/>) and which requests a single
///   1..1 "target 1/1 creature". On resolution it registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1/+2) on the chosen creature's
///   own continuous-effects service (CR 613.7c Layer 7c pump; expires at
///   cleanup, CR 514.2) — the same +P/+T primitive Blinkmoth Nexus / Berserk
///   use. Without a target or a continuous-effects service on the target the
///   resolution is a documented no-op (CR 608.2b — illegal/absent target →
///   nothing happens).
///
/// ## Deferred (v1 gaps — shared posture with the targeted-pump cards)
/// - <b>Target legality filter "1/1 creature"</b>: the
///   <see cref="TargetRequest"/> records the description, but candidate
///   filtering by current power/toughness is enforced at target-selection
///   time by the agent layer (same posture
///   <see cref="BlinkmothNexusFactory"/> documents for "Blinkmoth creature"
///   and <see cref="ShadowspearFactory"/> for "target creature"). The pump
///   still defends in depth at resolution by gating on
///   <see cref="Majik.Core.Zones.ZoneType.Battlefield"/> + a live effects
///   service. Per CR 608.2b/608.2c the 1/1 restriction is re-checked on
///   resolution in paper; that re-check is deferred to the agent layer here.
/// </summary>
[CardName("Pendelhaven")]
public static class PendelhavenFactory
{
    public const string CardName = "Pendelhaven";
    public const string Slug = "pendelhaven";

    /// <summary>Power granted by the pump ability.</summary>
    public const int PumpPower = 1;

    /// <summary>Toughness granted by the pump ability.</summary>
    public const int PumpToughness = 2;

    /// <summary>
    /// Construct Pendelhaven with no live continuous-effects wiring (the
    /// shape / dispatcher path). The mana ability (from JSON) and the targeted
    /// pump ability are both attached so the card surface is complete; the
    /// pump resolution closure no-ops when no target creature carrying a
    /// <see cref="ContinuousEffectsService"/> is chosen (legal — same
    /// deferred-wiring contract as <see cref="BlinkmothNexusFactory.Create(Player)"/>).
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: name, Legendary
        // supertype, Land type, and the {T}: Add {G} mana ability. The
        // targeted pump ability is layered on below — it isn't expressible in
        // the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}: Target 1/1 creature gets +1/+2 until end of turn.
        // CR 602 activated ability; CR 613.7c Layer-7c pump; CR 514.2 expiry.
        // Tap-only cost; same target-creature activated-ability shape as
        // Blinkmoth Nexus's pump; reuses the PumpUntilEndOfTurnEffect
        // primitive (Berserk / Blinkmoth Nexus).
        // ----------------------------------------------------------------
        ActivatedAbility? pumpAbility = null;
        var pumpEffect = new Effect(
            $"{CardName}: target 1/1 creature gets +{PumpPower}/+{PumpToughness} until end of turn",
            () =>
            {
                if (pumpAbility == null) return;
                if (pumpAbility.ChosenTargets.Count == 0) return;
                if (pumpAbility.ChosenTargets[0].Count == 0) return;
                if (pumpAbility.ChosenTargets[0][0] is not Creature creature) return;

                // CR 608.2b — illegal target on resolution (left the
                // battlefield) → no-op. Defence-in-depth zone check.
                if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return;

                // Register against the target creature's own effects service.
                // Without one (shape-only target) the pump is a documented
                // no-op — the +1/+2 simply isn't tracked.
                if (creature.ActiveEffects == null) return;
                creature.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(creature, p: PumpPower, t: PumpToughness));
            });

        pumpAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { pumpEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target 1/1 creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(pumpAbility);

        return land;
    }

    /// <summary>The {T}: target 1/1 creature gets +1/+2 pump ability.</summary>
    public static ActivatedAbility GetPumpAbility(Land land)
    {
        ArgumentNullException.ThrowIfNull(land);
        return land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);
    }
}
