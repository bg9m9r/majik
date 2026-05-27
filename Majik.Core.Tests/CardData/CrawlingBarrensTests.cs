using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="CrawlingBarrensFactory"/> — manland with a
/// counter-pump ability and a Construct-artifact animate.
/// </summary>
public class CrawlingBarrensTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CrawlingBarrens_IsLand_NoSubtypes_NoSupertypes()
    {
        var land = CrawlingBarrensFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasType(CardType.Artifact).Should().BeFalse();
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Name.Should().Be("Crawling Barrens");
        land.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CrawlingBarrens()
    {
        var card = NamedCardFactory.Create("Crawling Barrens", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Crawling Barrens");
    }

    [Fact]
    public void CrawlingBarrens_TapForColorless()
    {
        var land = CrawlingBarrensFactory.Create(_alice);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();
        var produced = manaAbility.Activate();

        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void CrawlingBarrens_HasTwoActivatedAbilities_Alongside_ManaAbility()
    {
        var land = CrawlingBarrensFactory.Create(_alice);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ToList();

        activated.Should().HaveCount(2,
            "{2}{C} counter pump + {3}{C} animate");
        activated.All(a => a.TargetRequests.Count == 0).Should().BeTrue();
    }

    [Fact]
    public void CounterPumpAbility_AddsTwoPlusOnePlusOneCounters_OnResolve()
    {
        var land = CrawlingBarrensFactory.Create(_alice, effects: null, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // The first activated ability (index 0) is the {2}{C} counter
        // pump per the factory wiring.
        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ToList();
        var counterPump = activated[0];
        counterPump.Resolve();

        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);

        // Activate again — counters stack.
        counterPump.Resolve();
        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(4);
    }

    [Fact]
    public void AnimateAbility_RegistersLayer4AndLayer7b_EotExpiring_WithConstructArtifactReach()
    {
        var effects = new ContinuousEffectsService();
        var land = CrawlingBarrensFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ToList();
        var animate = activated[1]; // {3}{C} animate is index 1
        animate.Resolve();

        var registered = GetRegisteredEffects(effects).ToList();

        var anim = registered.OfType<ManlandCycleAnimateEffect>()
            .SingleOrDefault(e => ReferenceEquals(e.Target, land));
        anim.Should().NotBeNull();
        anim!.Layer.Should().Be(Layer.Type);
        anim.ExpiresAtEndOfTurn.Should().BeTrue();
        anim.Keywords.Should().BeEquivalentTo(new[] { "Reach" });
        anim.Subtypes.Should().BeEquivalentTo(new[] { CardSubtype.Construct });
        anim.ExtraTypes.Should().BeEquivalentTo(new[] { CardType.Artifact });

        var pt = registered.OfType<ManlandCycleBecomesPTEffect>()
            .SingleOrDefault(e => e.AppliesTo(land));
        pt.Should().NotBeNull();
        pt!.NewPower.Should().Be(0);
        pt.NewToughness.Should().Be(0);
        pt.Layer.Should().Be(Layer.PT_SetBase);
        pt.ExpiresAtEndOfTurn.Should().BeTrue();
    }

    [Fact]
    public void Compute_AfterAnimate_GrantsCreature_Artifact_Construct_Reach_KeepsLand()
    {
        var effects = new ContinuousEffectsService();
        var land = CrawlingBarrensFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ElementAt(1);
        animate.Resolve();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Creature);
        chars.Types.Should().Contain(CardType.Artifact);
        chars.Types.Should().Contain(CardType.Land,
            "'It's still a land.' — CR 613.1c additive type grant");
        chars.Subtypes.Should().Contain(CardSubtype.Construct);
        chars.Subtypes.Should().NotContain(CardSubtype.Elemental,
            "Construct override — Elemental is the cycle default, not Crawling Barrens'");
        chars.Keywords.Should().Contain("Reach");
    }

    [Fact]
    public void EndOfTurn_ExpiresAnimateAndPT_ButCountersPersist()
    {
        var effects = new ContinuousEffectsService();
        var land = CrawlingBarrensFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ToList();

        activated[0].Resolve(); // counter pump
        activated[1].Resolve(); // animate

        // CR 514.2 — cleanup lifts EOT-scoped effects. Counters are
        // permanent objects (CR 121.5) so they persist past cleanup.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects).OfType<ManlandCycleAnimateEffect>()
            .Where(e => ReferenceEquals(e.Target, land)).Should().BeEmpty();
        GetRegisteredEffects(effects).OfType<ManlandCycleBecomesPTEffect>()
            .Where(e => e.AppliesTo(land)).Should().BeEmpty();

        // Counters survive.
        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);

        // Compute reverts to plain Land.
        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Types.Should().NotContain(CardType.Artifact);
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
