using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Pays down the artifact-bauble subset of the sacrifice-bus-thread deferral
/// (artifact-bauble-sac-bus-payers). Unlike the fetch-/utility-land payers
/// (which route through the binder chain in production —
/// named-factory-vs-binder-chain), an ARTIFACT bauble DOES get the
/// <c>[CardName]</c> factory instance-swap in the production routed build
/// (<c>BuildDeckCard</c> gates the swap only on <c>!shell.HasType(Land)</c>),
/// so the factory IS the prod path for Traveler's Amulet / Wayfarer's Bauble /
/// Renegade Map.
///
/// <para>Each of these three baubles carries a
/// <see cref="AdditionalCost.Sacrifice"/> on the bauble itself, but the
/// shape-only <c>Create(Player)</c> overload supplied no <see cref="IEventBus"/>
/// and the resolve closure inlined a raw battlefield→graveyard move, so paying
/// the self-sacrifice cost published nothing — "whenever a/an [player]
/// sacrifices …" aristocrat payoffs (Mayhem Devil, It That Betrays) never
/// observed the bauble sacrifice (CR 701.16a).</para>
///
/// <para>The fix mirrors the Festival-Crasher / Expedition Map seam: an
/// effects-aware <c>Create(Player, ContinuousEffectsService?)</c> overload the
/// source generator dispatches in the production routed build threads
/// <c>effects.EventBus</c> into <see cref="AdditionalCost.Sacrifice(Permanent,
/// IEventBus?)"/> so the self-sacrifice cost publishes a
/// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the
/// cost-payer. The single-arg overload stays bus-less (publish-nothing legacy
/// posture) for shape / dispatcher tests.</para>
/// </summary>
public class ArtifactBaubleSacrificeBusTests
{
    private static (EventBus bus, ContinuousEffectsService effects, List<PermanentSacrificedEvent> seen) Wired()
    {
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        return (bus, new ContinuousEffectsService(bus), seen);
    }

    private static AdditionalCost SacCost(Artifact bauble) =>
        bauble.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs)
            .OfType<AdditionalCost>()
            .First(c => c.CostType == AdditionalCostType.Sacrifice);

    public static IEnumerable<object[]> BaubleFactories => new[]
    {
        new object[] { "Traveler's Amulet", (System.Func<Player, ContinuousEffectsService, Artifact>)((o, e) => TravelersAmuletFactory.Create(o, e)) },
        new object[] { "Wayfarer's Bauble", (System.Func<Player, ContinuousEffectsService, Artifact>)((o, e) => WayfarersBaubleFactory.Create(o, e)) },
        new object[] { "Renegade Map",      (System.Func<Player, ContinuousEffectsService, Artifact>)((o, e) => RenegadeMapFactory.Create(o, e)) },
    };

    [Theory]
    [MemberData(nameof(BaubleFactories))]
    public void EffectsAwareOverload_ThreadsBus_SacCostPublishes(
        string name, System.Func<Player, ContinuousEffectsService, Artifact> create)
    {
        var alice = new Player("Alice", 20);
        var (bus, effects, seen) = Wired();

        var bauble = create(alice, effects);
        bauble.Name.Should().Be(name);
        alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        // Pay only the sacrifice cost in isolation (the bus-bearing one).
        SacCost(bauble).Pay(alice);

        bauble.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == bauble
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    [Theory]
    [MemberData(nameof(BaubleFactories))]
    public void ShapeOnlyOverload_StaysBusLess_NoPublish(
        string name, System.Func<Player, ContinuousEffectsService, Artifact> create)
    {
        _ = create; // shape-only path uses the single-arg overload below
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);

        // Single-arg overload — no bus threaded (legacy publish-nothing posture).
        var bauble = (Artifact)Majik.Core.CardData.NamedCardFactory.Create(name, alice);
        alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        SacCost(bauble).Pay(alice);

        bauble.Zone.Should().Be(ZoneType.Graveyard, "the move still happens");
        seen.Should().BeEmpty("no bus was threaded into the single-arg overload");
    }

    // ------------------------------------------------------------------
    // RESOLVE-CLOSURE sac (artifact-bauble-resolve-closure-sac-bus).
    //
    // The central IBusAwareCost seam (#2736) only reaches the *cost* leg —
    // the AdditionalCost.Sacrifice paid by CostPayment during activation. But
    // each of these baubles ALSO performs the self-sacrifice inside its RESOLVE
    // closure (SacrificeSelf), the bus-aware fallback for the resolve-only
    // dispatcher path where the cost was not pre-paid (e.g. a directly-Resolved
    // ability, or a future "resolve the ability without paying costs" effect).
    // The effects-aware Create(Player, ContinuousEffectsService) overload threads
    // effects.EventBus into that closure too, so a resolve-time self-sacrifice
    // publishes PermanentSacrificedEvent (CR 701.16a) crediting the controller —
    // the seam aristocrat payoffs (Mayhem Devil, It That Betrays) read.
    // ------------------------------------------------------------------

    private static Land BasicForest(Player owner)
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(owner);
        owner.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);
        return forest;
    }

    [Theory]
    [MemberData(nameof(BaubleFactories))]
    public void ResolveClosure_WhenBusWired_SacrificesSelf_AndPublishes(
        string name, System.Func<Player, ContinuousEffectsService, Artifact> create)
    {
        var alice = new Player("Alice", 20);
        var (_, effects, seen) = Wired();
        BasicForest(alice); // a candidate so the search half of the closure runs

        var bauble = create(alice, effects);
        bauble.Name.Should().Be(name);
        alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        // Resolve the ability DIRECTLY (costs NOT pre-paid), so the bauble is
        // still on the battlefield when the resolve closure runs — this is the
        // resolve-closure SacrificeSelf path, distinct from the cost path above.
        var tutor = bauble.Abilities.OfType<ActivatedAbility>().Single();
        tutor.Resolve();

        bauble.Zone.Should().Be(ZoneType.Graveyard, "the resolve closure sacrificed the bauble");
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == bauble
                && ev.SacrificingPlayer == alice
                && !ev.WasToken,
            "the resolve-closure self-sacrifice publishes via the threaded bus (CR 701.16a)");
    }

    [Theory]
    [MemberData(nameof(BaubleFactories))]
    public void ResolveClosure_ShapeOnly_StaysBusLess_NoPublish(
        string name, System.Func<Player, ContinuousEffectsService, Artifact> create)
    {
        _ = create; // shape-only path uses the single-arg overload below
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        BasicForest(alice);

        var bauble = (Artifact)Majik.Core.CardData.NamedCardFactory.Create(name, alice);
        alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var tutor = bauble.Abilities.OfType<ActivatedAbility>().Single();
        tutor.Resolve();

        bauble.Zone.Should().Be(ZoneType.Graveyard, "the resolve closure still sacrifices the bauble");
        seen.Should().BeEmpty("no bus was threaded into the single-arg overload");
    }
}
