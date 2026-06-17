using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ba Sing Se (Avatar: The Last Airbender).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped unless you control a basic land.
///    {T}: Add {G}.
///    {2}{G}, {T}: Earthbend 2. Activate only as a sorcery. (Target land you
///    control becomes a 0/0 creature with haste that's still a land. Put two
///    +1/+1 counters on it. When it dies or is exiled, return it to the
///    battlefield tapped.)"
///
/// ## Why it gets its own factory
/// Closes the v1 deferral <c>ba-sing-se-earthbend-target-land-animate</c>.
/// Earthbend is the one land-animate shape <see cref="ManlandBinder"/> cannot
/// reach (its AnimateLine keys on "this land becomes …", but Earthbend targets
/// ANOTHER land you control). No NEW engine mechanic is needed: the Earthbend
/// keyword action is already a built primitive
/// (<see cref="EarthbendAction.Apply(Permanent, Player, int, ContinuousEffectsService?)"/>,
/// used by Badgermole Cub + Dai Li Indoctrination) — it animates the target
/// land to a 0/0 creature with haste that's still a land (no creature subtype),
/// puts N +1/+1 counters on it (so an N/N), and attaches the one-shot "when it
/// dies or is exiled, return it tapped" delayed trigger (CR 603.7a). This card
/// composes that primitive behind a "target land you control" TargetRequest.
///
/// ## Production path
/// Lands are NEVER routed through their <c>[CardName]</c> factory in prod
/// (GameFacade gates the factory instance-swap on
/// <c>!shell.HasType(CardType.Land)</c>) — the binder chain is the only live
/// path. The Earthbend activated ability + the {T}: Add {G} mana ability + the
/// "enters tapped unless you control a basic land" replacement all bind off the
/// real oracle text via <see cref="LandActivatedAbilityBinder"/> /
/// <see cref="OracleManaBinder"/> / <see cref="ConditionalEntersTappedBinder"/>.
/// This factory exists for the test-only <c>[CardName]</c> dispatch + the
/// <c>IsImplemented</c> derivation (and mirrors the activated ability so the
/// factory-built card behaves identically to the binder-built one).
///
/// ## Implemented (v1)
/// - <b>Land</b> identity from the embedded JSON definition
///   (<c>ba-sing-se.json</c>) plus owner / controller wiring.
/// - <b>{T}: Add {G}</b> — single <see cref="ManaAbility"/> (CR 605.1),
///   declared in the JSON.
/// - <b>{2}{G}, {T}: Earthbend 2. Activate only as a sorcery.</b> — an
///   <see cref="ActivatedAbility"/> (CR 602) with the sorcery-speed rider
///   (CR 117.1a / 307.5), a "target land you control" <see cref="TargetRequest"/>
///   (1..1 over the controller's battlefield lands — the source land is itself a
///   legal target), and a resolution body that CR-608.2b-rechecks the chosen
///   land then routes to <see cref="EarthbendAction.Apply(Permanent, Player, int, ContinuousEffectsService?)"/>.
///
/// ## Rules notes
/// - Earthbend's animate has no duration (CR 701.59) — it persists while the
///   land is on the battlefield (the continuous effect self-prunes on leave).
/// - The "enters tapped unless you control a basic land" rider is a
///   <see cref="ConditionalEntersTappedBinder"/> replacement off oracle text in
///   prod; it is not layered here (shape-only build).
/// </summary>
[CardName("Ba Sing Se")]
public static class BaSingSeFactory
{
    public const string CardName = "Ba Sing Se";
    public const string Slug = "ba-sing-se";

    /// <summary>CR 701.59 — Earthbend <b>2</b>.</summary>
    public const int EarthbendAmount = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Ba Sing Se with no live
    /// <see cref="ContinuousEffectsService"/>. The Earthbend animate falls
    /// through to the target land's own <c>ActiveEffects</c> (or is skipped when
    /// none is wired); the +1/+1 counters + return trigger still apply.</summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Ba Sing Se. The {T}: Add {G} mana ability comes from the
    /// embedded JSON; the Earthbend activated ability is layered on structurally.
    /// When <paramref name="effects"/> is supplied the Earthbend animate
    /// continuous effect is registered against it (so the target's P/T surfaces
    /// through Compute).
    /// </summary>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Land, {T}: Add {G}) from the embedded JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        // {2}{G}, {T}: Earthbend 2. Activate only as a sorcery.
        ActivatedAbility? ability = null;
        var earthbend = new Effect(
            $"{CardName}: Earthbend {EarthbendAmount} (animate target land you control)",
            () =>
            {
                var you = land.Controller ?? owner;
                var chosen = FirstChosen(ability);
                if (chosen is not Land target) return;
                // CR 608.2b — resolve-time legality recheck.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Land)) return;
                if (!ReferenceEquals(target.Controller, you)) return;
                EarthbendAction.Apply(target, you, EarthbendAmount, effects);
            });

        ability = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}{G}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { earthbend },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target land you control",
                    MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    CandidateGatherer: _ => GatherControllerLands(land, owner)),
            },
            sorcerySpeed: true);

        land.AddAbility(ability);

        return land;
    }

    private static object? FirstChosen(ActivatedAbility? ability)
    {
        if (ability is null) return null;
        var chosen = ability.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return null;
        return chosen[0][0];
    }

    private static IReadOnlyList<object> GatherControllerLands(Land land, Player owner)
    {
        var ctrl = land.Controller ?? owner;
        return ctrl.Zones.Battlefield.GetCards()
            .OfType<Land>()
            .Cast<object>()
            .ToList();
    }
}
