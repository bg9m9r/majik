using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="MishrasFactoryFactory"/> — manland with a {1}
/// animate to 2/2 Assembly-Worker artifact creature plus a tap-target
/// pump for other Assembly-Workers.
/// </summary>
public class MishrasFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void MishrasFactory_IsLand_NoSubtypes()
    {
        var land = MishrasFactoryFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasType(CardType.Artifact).Should().BeFalse();
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Name.Should().Be("Mishra's Factory");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MishrasFactory()
    {
        var card = NamedCardFactory.Create("Mishra's Factory", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Mishra's Factory");
    }

    [Fact]
    public void MishrasFactory_HasTwoActivatedAbilities_AlongsideManaAbility()
    {
        var land = MishrasFactoryFactory.Create(_alice);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ToList();

        activated.Should().HaveCount(2);

        // index 0 = {1} animate (no targets)
        activated[0].TargetRequests.Should().BeEmpty();

        // index 1 = {T} pump (one target)
        activated[1].TargetRequests.Should().HaveCount(1);
        activated[1].TargetRequests[0].MinTargets.Should().Be(1);
        activated[1].TargetRequests[0].MaxTargets.Should().Be(1);
        activated[1].TargetRequests[0].Description.Should().Contain("Assembly-Worker");
    }

    [Fact]
    public void Animate_RegistersLayer4AndLayer7b_WithAssemblyWorkerArtifactCreatureBody()
    {
        var effects = new ContinuousEffectsService();
        var land = MishrasFactoryFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ToList();
        var animate = activated[0];
        animate.Resolve();

        var registered = GetRegisteredEffects(effects).ToList();

        var anim = registered.OfType<ManlandCycleAnimateEffect>()
            .SingleOrDefault(e => ReferenceEquals(e.Target, land));
        anim.Should().NotBeNull();
        anim!.Subtypes.Should().BeEquivalentTo(new[] { CardSubtype.AssemblyWorker });
        anim.ExtraTypes.Should().BeEquivalentTo(new[] { CardType.Artifact });
        anim.Keywords.Should().BeEmpty();
        anim.ExpiresAtEndOfTurn.Should().BeTrue();

        var pt = registered.OfType<ManlandCycleBecomesPTEffect>()
            .SingleOrDefault(e => e.AppliesTo(land));
        pt.Should().NotBeNull();
        pt!.NewPower.Should().Be(2);
        pt.NewToughness.Should().Be(2);
        pt.ExpiresAtEndOfTurn.Should().BeTrue();
    }

    [Fact]
    public void Compute_AfterAnimate_AddsArtifactCreatureAssemblyWorker_KeepsLand()
    {
        var effects = new ContinuousEffectsService();
        var land = MishrasFactoryFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .First() // {1} animate
            .Resolve();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Creature);
        chars.Types.Should().Contain(CardType.Artifact);
        chars.Types.Should().Contain(CardType.Land,
            "still a land — CR 613.1c");
        chars.Subtypes.Should().Contain(CardSubtype.AssemblyWorker);
        chars.Subtypes.Should().NotContain(CardSubtype.Elemental);
    }

    [Fact]
    public void Pump_RegistersPlusOnePlusOneUntilEot_OnTargetAssemblyWorker()
    {
        var land = MishrasFactoryFactory.Create(_alice, effects: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Build a Creature with the Assembly-Worker subtype + a live
        // ContinuousEffectsService on its ActiveEffects so the pump
        // effect lands on a real registry.
        var perTargetEffects = new ContinuousEffectsService();
        var worker = new Creature(
            name: "Assembly-Worker Test",
            manaCost: "{3}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.AssemblyWorker });
        worker.SetOwner(_alice);
        worker.SetController(_alice);
        worker.ActiveEffects = perTargetEffects;
        _alice.Zones.Battlefield.AddCard(worker);
        worker.SetZone(ZoneType.Battlefield);

        var pump = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ElementAt(1); // {T} pump

        // Pay the tap cost.
        foreach (var c in pump.Costs) c.Pay(_alice);

        pump.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { worker },
        });
        pump.Resolve();

        var pumps = GetRegisteredEffects(perTargetEffects)
            .OfType<PumpUntilEndOfTurnEffect>()
            .Where(e => e.AppliesTo(worker))
            .ToList();
        pumps.Should().HaveCount(1);
        pumps[0].ExpiresAtEndOfTurn.Should().BeTrue();

        // Cleanup lifts it.
        perTargetEffects.ExpireEndOfTurn();
        GetRegisteredEffects(perTargetEffects)
            .OfType<PumpUntilEndOfTurnEffect>()
            .Where(e => e.AppliesTo(worker))
            .Should().BeEmpty();
    }

    [Fact]
    public void Pump_NonAssemblyWorkerTarget_IsNoOp()
    {
        // CR 608.2b — illegal target → the effect does nothing for that
        // target. Mishra's Factory's pump is restricted by printed
        // subtype.
        var land = MishrasFactoryFactory.Create(_alice, effects: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var perTargetEffects = new ContinuousEffectsService();
        var bear = new Creature(
            name: "Grizzly Bears",
            manaCost: "{1}{G}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.ActiveEffects = perTargetEffects;
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var pump = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ElementAt(1);

        foreach (var c in pump.Costs) c.Pay(_alice);

        pump.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });
        pump.Resolve();

        GetRegisteredEffects(perTargetEffects)
            .OfType<PumpUntilEndOfTurnEffect>()
            .Should().BeEmpty("Grizzly Bears isn't an Assembly-Worker");
    }

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
