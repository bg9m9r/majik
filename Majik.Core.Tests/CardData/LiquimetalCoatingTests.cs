using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="LiquimetalCoatingFactory"/> — Artifact {3}
/// (Mirrodin block):
///   "{T}: Target permanent becomes an artifact in addition to its other
///    types until end of turn."
///
/// Covers:
/// - Identity (Artifact, {3}, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Ability shape: one activated ability with tap + 1..1 target permanent.
/// - Resolution: Layer 4 type-add registered against the
///   <see cref="ContinuousEffectsService"/>, target's effective types
///   include Artifact, printed types preserved.
/// - End-of-turn expiration removes the effect.
/// - Off-battlefield / non-permanent target → resolution-time no-op.
/// </summary>
public class LiquimetalCoatingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void LiquimetalCoating_IsArtifact_AtCost3()
    {
        var coating = LiquimetalCoatingFactory.Create(_alice);

        coating.Name.Should().Be("Liquimetal Coating");
        coating.ManaCost.Should().Be("{3}");
        coating.HasType(CardType.Artifact).Should().BeTrue();
        coating.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        coating.Owner.Should().BeSameAs(_alice);
        coating.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LiquimetalCoating()
    {
        var card = NamedCardFactory.Create("Liquimetal Coating", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Liquimetal Coating");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void LiquimetalCoating_AbilityShape_TapAndTargetPermanent()
    {
        var coating = LiquimetalCoatingFactory.Create(_alice);

        var ability = coating.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "the activation has no mana component — just {T}");
        ability.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Tap)
            .Should().Be(1);

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("permanent");
    }

    [Fact]
    public void Activate_AgainstNonArtifactCreature_AddsArtifactTypeUntilEot()
    {
        // Bob controls Grizzly Bears — a non-artifact creature. After
        // resolving Liquimetal Coating's ability targeting the bear, the
        // bear's effective types should include both Creature (printed)
        // and Artifact (Layer 4 ADD until EOT).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        var coating = LiquimetalCoatingFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(coating);
        coating.SetZone(ZoneType.Battlefield);

        var ability = coating.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        foreach (var e in ability.Effects) e.Execute();

        // Layer 4 ADD applied — bear is now also an Artifact.
        var chars = effects.Compute(bear);
        chars.Types.Should().Contain(CardType.Artifact,
            "the Layer 4 ADD makes the bear an artifact");
        chars.Types.Should().Contain(CardType.Creature,
            "the printed Creature type is preserved (ADD, not replace)");
    }

    [Fact]
    public void Activate_AgainstLand_AddsArtifactType()
    {
        // Liquimetal Coating targets ANY permanent — including a land.
        var forest = (Permanent)NamedCardFactory.Create("Forest", _alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        var coating = LiquimetalCoatingFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(coating);
        coating.SetZone(ZoneType.Battlefield);

        var ability = coating.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { forest },
        });

        foreach (var e in ability.Effects) e.Execute();

        var chars = effects.Compute(forest);
        chars.Types.Should().Contain(CardType.Artifact);
        chars.Types.Should().Contain(CardType.Land,
            "the printed Land type is preserved");
    }

    [Fact]
    public void Activate_EndOfTurn_RemovesArtifactType()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        var coating = LiquimetalCoatingFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(coating);
        coating.SetZone(ZoneType.Battlefield);

        var ability = coating.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        foreach (var e in ability.Effects) e.Execute();
        effects.Compute(bear).Types.Should().Contain(CardType.Artifact);

        effects.ExpireEndOfTurn();

        effects.Compute(bear).Types.Should().NotContain(CardType.Artifact,
            "the effect carried ExpiresAtEndOfTurn=true");
    }

    [Fact]
    public void Activate_IllegalTarget_NoEffectRegistered()
    {
        // Card target that isn't a Permanent → CR 608.2b no-op.
        var effects = new ContinuousEffectsService();
        var coating = LiquimetalCoatingFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(coating);
        coating.SetZone(ZoneType.Battlefield);

        var ability = coating.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob }, // a Player, not a Permanent
        });

        foreach (var e in ability.Effects) e.Execute();

        GetRegisteredEffects(effects)
            .OfType<LiquimetalCoatingAddArtifactEffect>()
            .Should().BeEmpty();
    }

    [Fact]
    public void Activate_NoEffectsService_NoOp()
    {
        // Shape-only path — no continuous-effects service wired.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var coating = LiquimetalCoatingFactory.Create(_alice); // effects=null
        _alice.Zones.Battlefield.AddCard(coating);
        coating.SetZone(ZoneType.Battlefield);

        var ability = coating.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        // Should not throw.
        var act = () =>
        {
            foreach (var e in ability.Effects) e.Execute();
        };
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
