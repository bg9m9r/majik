using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ArdentRecruitFactory"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 1/1, Human + Soldier subtypes,
///   mana cost {W}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Metalcraft self-pump (Layer 7c):
///   - 0 artifacts → 1/1.
///   - 2 artifacts → 1/1 (below threshold).
///   - 3 artifacts → 3/3 (threshold reached).
///   - 5 artifacts → 3/3 (no additional bonus stack).
///   - Threshold dynamically re-evaluates as artifacts ETB / LTB.
///   - Only the controller's artifacts count.
/// - Helper predicates (CountArtifactsControlled, MetalcraftActive).
/// </summary>
public class ArdentRecruitFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Artifact NewArtifact(Player owner, string name = "Bauble")
    {
        var a = new Artifact(name, "");
        a.SetOwner(owner);
        a.SetController(owner);
        a.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(a);
        return a;
    }

    [Fact]
    public void ArdentRecruit_Identity()
    {
        var ar = ArdentRecruitFactory.Create(_alice);

        ar.Name.Should().Be("Ardent Recruit");
        ar.ManaCost.Should().Be("{W}");
        ar.HasType(CardType.Creature).Should().BeTrue();
        ar.HasSubtype(CardSubtype.Human).Should().BeTrue();
        ar.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        ar.BasePower.Should().Be(1);
        ar.BaseToughness.Should().Be(1);
        ar.Owner.Should().BeSameAs(_alice);
        ar.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ArdentRecruit_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Ardent Recruit", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ardent Recruit");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    private (Creature ar, ContinuousEffectsService effects) NewRecruitOnBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var ar = ArdentRecruitFactory.Create(_alice, effects, bus);
        zones.MoveCard(ar, ZoneType.Library, ZoneType.Battlefield, _alice);
        ar.ActiveEffects = effects;
        return (ar, effects);
    }

    [Fact]
    public void Metalcraft_ZeroArtifacts_StaysOneOne()
    {
        var (ar, _) = NewRecruitOnBattlefield();
        ar.Power.Should().Be(1);
        ar.Toughness.Should().Be(1);
    }

    [Fact]
    public void Metalcraft_TwoArtifacts_BelowThreshold_StaysOneOne()
    {
        var (ar, _) = NewRecruitOnBattlefield();
        NewArtifact(_alice, "A1");
        NewArtifact(_alice, "A2");

        ar.Power.Should().Be(1);
        ar.Toughness.Should().Be(1);
    }

    [Fact]
    public void Metalcraft_ThreeArtifacts_ActivatesBonus_ThreeThree()
    {
        var (ar, _) = NewRecruitOnBattlefield();
        NewArtifact(_alice, "A1");
        NewArtifact(_alice, "A2");
        NewArtifact(_alice, "A3");

        ar.Power.Should().Be(3, "1 + 2 Metalcraft bonus");
        ar.Toughness.Should().Be(3);
    }

    [Fact]
    public void Metalcraft_FiveArtifacts_NoExtraStacking_ThreeThree()
    {
        var (ar, _) = NewRecruitOnBattlefield();
        for (int i = 0; i < 5; i++) NewArtifact(_alice, $"A{i}");

        ar.Power.Should().Be(3, "+2 is a flat bonus, not per-artifact");
        ar.Toughness.Should().Be(3);
    }

    [Fact]
    public void Metalcraft_DynamicallyReevaluates_OnArtifactComingAndGoing()
    {
        var (ar, _) = NewRecruitOnBattlefield();
        var a1 = NewArtifact(_alice, "A1");
        var a2 = NewArtifact(_alice, "A2");

        // Below threshold.
        ar.Power.Should().Be(1);

        // Third artifact arrives → Metalcraft flips on.
        var a3 = NewArtifact(_alice, "A3");
        ar.Power.Should().Be(3);
        ar.Toughness.Should().Be(3);

        // Remove the third → Metalcraft flips off.
        _alice.Zones.Battlefield.RemoveCard(a3);
        a3.SetZone(ZoneType.Graveyard);
        ar.Power.Should().Be(1);
        ar.Toughness.Should().Be(1);
    }

    [Fact]
    public void Metalcraft_OpponentsArtifactsDoNotCount()
    {
        var (ar, _) = NewRecruitOnBattlefield();
        NewArtifact(_bob, "B1");
        NewArtifact(_bob, "B2");
        NewArtifact(_bob, "B3");

        ar.Power.Should().Be(1,
            "Metalcraft reads 'artifacts YOU control', not opponent's");
        ar.Toughness.Should().Be(1);
    }

    [Fact]
    public void Metalcraft_HelperPredicates()
    {
        ArdentRecruitFactory.MetalcraftActive(_alice).Should().BeFalse();
        ArdentRecruitFactory.CountArtifactsControlled(_alice).Should().Be(0);

        NewArtifact(_alice, "A1");
        NewArtifact(_alice, "A2");
        ArdentRecruitFactory.CountArtifactsControlled(_alice).Should().Be(2);
        ArdentRecruitFactory.MetalcraftActive(_alice).Should().BeFalse();

        NewArtifact(_alice, "A3");
        ArdentRecruitFactory.CountArtifactsControlled(_alice).Should().Be(3);
        ArdentRecruitFactory.MetalcraftActive(_alice).Should().BeTrue();
    }
}
