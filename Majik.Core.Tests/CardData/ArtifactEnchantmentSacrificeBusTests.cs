using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Pays down the artifact/enchantment subset of the sacrifice-bus-thread
/// deferral (spell-copy-new-targets-followups stillMissing): the artifact /
/// enchantment self-sacrifice-cost factories that still constructed a bare
/// <see cref="AdditionalCost.Sacrifice"/>(card) with no <see cref="IEventBus"/>
/// on the production <c>[CardName]</c> factory path.
///
/// <para>Unlike lands (binder-chain in production —
/// named-factory-vs-binder-chain), an ARTIFACT or ENCHANTMENT DOES get the
/// <c>[CardName]</c> factory instance-swap in the production routed build
/// (<c>BuildDeckCard</c> gates the swap only on <c>!shell.HasType(Land)</c>),
/// so the factory IS the prod path. Each now exposes the source-generator-
/// recognised effects-aware <c>Create(Player, ContinuousEffectsService?)</c>
/// overload that threads <c>effects.EventBus</c> into
/// <see cref="AdditionalCost.Sacrifice(Permanent, IEventBus?)"/> so paying the
/// self-sacrifice cost publishes a <see cref="PermanentSacrificedEvent"/>
/// (CR 701.16a) crediting the cost-payer. The single-arg overload stays
/// bus-less (publish-nothing legacy posture) for shape / dispatcher tests.</para>
///
/// <para>Mirrors <see cref="ArtifactBaubleSacrificeBusTests"/> (the bauble
/// subset paid down by #2733).</para>
/// </summary>
public class ArtifactEnchantmentSacrificeBusTests
{
    private static (EventBus bus, ContinuousEffectsService effects, List<PermanentSacrificedEvent> seen) Wired()
    {
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        return (bus, new ContinuousEffectsService(bus), seen);
    }

    private static AdditionalCost SacCost(Permanent permanent) =>
        permanent.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs)
            .OfType<AdditionalCost>()
            .First(c => c.CostType == AdditionalCostType.Sacrifice);

    public static IEnumerable<object[]> SelfSacFactories => new[]
    {
        new object[] { "Sterling Grove",          (Func<Player, ContinuousEffectsService, Permanent>)((o, e) => SterlingGroveFactory.Create(o, e)) },
        new object[] { "Seal of Fire",            (Func<Player, ContinuousEffectsService, Permanent>)((o, e) => SealOfFireFactory.Create(o, e)) },
        new object[] { "Aura of Silence",         (Func<Player, ContinuousEffectsService, Permanent>)((o, e) => AuraOfSilenceFactory.Create(o, e)) },
        new object[] { "Seal of Cleansing",       (Func<Player, ContinuousEffectsService, Permanent>)((o, e) => SealOfCleansingFactory.Create(o, e)) },
        new object[] { "Cindervines",             (Func<Player, ContinuousEffectsService, Permanent>)((o, e) => CindervinesFactory.Create(o, e)) },
        new object[] { "Roiling Vortex",          (Func<Player, ContinuousEffectsService, Permanent>)((o, e) => RoilingVortexFactory.Create(o, e)) },
        new object[] { "Omen of the Sea",         (Func<Player, ContinuousEffectsService, Permanent>)((o, e) => OmenOfTheSeaFactory.Create(o, e)) },
        new object[] { "The Underworld Cookbook", (Func<Player, ContinuousEffectsService, Permanent>)((o, e) => TheUnderworldCookbookFactory.Create(o, e)) },
        new object[] { "Mindslaver",              (Func<Player, ContinuousEffectsService, Permanent>)((o, e) => MindslaverFactory.Create(o, e)) },
        new object[] { "Lantern of Insight",      (Func<Player, ContinuousEffectsService, Permanent>)((o, e) => LanternOfInsightFactory.Create(o, e)) },
    };

    [Theory]
    [MemberData(nameof(SelfSacFactories))]
    public void EffectsAwareOverload_ThreadsBus_SacCostPublishes(
        string name, Func<Player, ContinuousEffectsService, Permanent> create)
    {
        var alice = new Player("Alice", 20);
        var (_, effects, seen) = Wired();

        var perm = create(alice, effects);
        perm.Name.Should().Be(name);
        alice.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);

        // Pay only the sacrifice cost in isolation (the bus-bearing one).
        SacCost(perm).Pay(alice);

        perm.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == perm
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    [Theory]
    [MemberData(nameof(SelfSacFactories))]
    public void ShapeOnlyOverload_StaysBusLess_NoPublish(
        string name, Func<Player, ContinuousEffectsService, Permanent> create)
    {
        _ = create; // shape-only path uses the single-arg dispatcher below
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);

        // Single-arg overload — no bus threaded (legacy publish-nothing posture).
        var perm = (Permanent)NamedCardFactory.Create(name, alice);
        alice.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);

        SacCost(perm).Pay(alice);

        perm.Zone.Should().Be(ZoneType.Graveyard, "the move still happens");
        seen.Should().BeEmpty("no bus was threaded into the single-arg overload");
    }

    /// <summary>
    /// Proves the SOURCE-GENERATOR prod dispatch routes to the new bus-threading
    /// effects-aware overload: the production <c>GameFacade</c> routed build calls
    /// <c>NamedCardFactory.Create(name, owner, effects)</c>, which dispatches to
    /// the factory's <c>Create(Player, ContinuousEffectsService)</c> overload (an
    /// artifact / enchantment gets the <c>[CardName]</c> instance-swap). The sac
    /// cost must therefore carry the bus and publish on the prod path.
    /// </summary>
    [Theory]
    [MemberData(nameof(SelfSacFactories))]
    public void ProdEffectsDispatch_ThreadsBus_SacCostPublishes(
        string name, Func<Player, ContinuousEffectsService, Permanent> create)
    {
        _ = create; // prod path goes through the generated effects dispatcher
        var alice = new Player("Alice", 20);
        var (_, effects, seen) = Wired();

        var perm = (Permanent)NamedCardFactory.Create(name, alice, effects);
        perm.Name.Should().Be(name);
        alice.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);

        SacCost(perm).Pay(alice);

        perm.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.SacrificedCard.Should().Be(perm);
    }

    /// <summary>
    /// Goblin Engineer's "{R}, {T}, Sacrifice an artifact" cost sacrifices a
    /// DIFFERENT (chosen) artifact — not the source — and has no structural
    /// <see cref="AdditionalCost.Sacrifice"/>; the sacrifice runs in the
    /// resolution closure. With the bus threaded (effects-aware overload) the
    /// sacrificed artifact's battlefield→graveyard move now publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a).
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task GoblinEngineer_SacrificesChosenArtifact_PublishesViaBus()
    {
        var alice = new Player("Alice", 20);
        var (_, effects, seen) = Wired();

        var engineer = GoblinEngineerFactory.Create(alice, effects);
        engineer.Name.Should().Be("Goblin Engineer");
        alice.Zones.Battlefield.AddCard(engineer);
        engineer.SetZone(ZoneType.Battlefield);

        // A spare artifact on the battlefield to sacrifice, and a creature in
        // the graveyard to reanimate (so the closure doesn't early-bail).
        var spare = new Artifact("Ornithopter", "{0}");
        spare.SetOwner(alice);
        spare.SetController(alice);
        alice.Zones.Battlefield.AddCard(spare);
        spare.SetZone(ZoneType.Battlefield);

        var graveArtifact = new Artifact("Bottle Gnomes", "{3}");
        graveArtifact.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(graveArtifact);
        graveArtifact.SetZone(ZoneType.Graveyard);

        var ability = engineer.Abilities.OfType<ActivatedAbility>().First();
        var ctx = new ResolutionContext(
            Controller: alice,
            Agent: null,
            Game: null,
            ChosenTargets: Array.Empty<IReadOnlyList<object>>());
        foreach (var effect in ability.Effects)
        {
            await effect.ExecuteAsync(ctx);
        }

        spare.Zone.Should().Be(ZoneType.Graveyard, "the chosen artifact was sacrificed");
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == spare
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }
}
