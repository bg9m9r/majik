using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WeldingJarFactory"/>.
///
/// Card: Welding Jar (Mirrodin, {0}). Oracle text:
///   "Sacrifice this artifact: Regenerate target artifact."
///
/// Covers:
/// - Identity (Artifact, {0}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Ability shape: one <see cref="ActivatedAbility"/> with a Sacrifice
///   additional cost + 1..1 "target artifact" request.
/// - Resolution: chosen artifact target gets a regeneration shield; jar
///   is sacrificed to its owner's graveyard.
/// - Resolve-time illegal target (non-artifact, off-battlefield) is a
///   silent no-op but the jar is still sacrificed.
/// </summary>
[Trait("Color", "C")]
public class WeldingJarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WeldingJar_Identity()
    {
        var jar = WeldingJarFactory.Create(_alice);

        jar.Name.Should().Be("Welding Jar");
        jar.ManaCost.Should().Be("{0}");
        jar.HasType(CardType.Artifact).Should().BeTrue();
        jar.Owner.Should().BeSameAs(_alice);
        jar.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void WeldingJar_HasOneActivatedAbility_WithSacAndTargetArtifact()
    {
        var jar = WeldingJarFactory.Create(_alice);

        var ability = jar.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "Welding Jar's sole cost is to sacrifice itself");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("artifact");
    }

    [Fact]
    public void Activate_GivesRegenerationShield_ToTargetArtifact_AndSacrificesJar()
    {
        var jar = WeldingJarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jar);
        jar.SetZone(ZoneType.Battlefield);

        // Another artifact on the battlefield to target.
        var bauble = new Artifact("Bauble", "{0}");
        bauble.SetOwner(_alice);
        bauble.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        bauble.RegenerationShieldCount.Should().Be(0,
            "no shield placed yet");

        var ability = jar.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bauble },
        });

        ability.Resolve();

        // Regeneration shield placed on the target artifact (CR 701.18 /
        // 701.15a).
        bauble.RegenerationShieldCount.Should().Be(1,
            "Welding Jar created one regeneration shield on the target artifact");

        // Welding Jar sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(jar);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(jar);
        jar.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_TargetIsNotArtifact_NoShieldButJarStillSacrificed()
    {
        // Choose-time filtering is deferred; a non-artifact target slips
        // through, and the resolve-time recheck (CR 608.2b) filters it
        // out. The cost was paid so Welding Jar still hits the graveyard.
        var jar = WeldingJarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jar);
        jar.SetZone(ZoneType.Battlefield);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var ability = jar.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        ability.Resolve();

        bears.RegenerationShieldCount.Should().Be(0,
            "Grizzly Bears is not an artifact — resolve-time recheck filters it");
        _alice.Zones.Graveyard.GetCards().Should().Contain(jar,
            "the sacrifice cost was paid regardless of resolve-time recheck");
        jar.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_TargetOffBattlefield_NoShieldButJarStillSacrificed()
    {
        var jar = WeldingJarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jar);
        jar.SetZone(ZoneType.Battlefield);

        // A target artifact that's in the graveyard at resolution time —
        // CR 608.2b rejects it.
        var bauble = new Artifact("Bauble", "{0}");
        bauble.SetOwner(_alice);
        bauble.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(bauble);
        bauble.SetZone(ZoneType.Graveyard);

        var ability = jar.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bauble },
        });

        ability.Resolve();

        bauble.RegenerationShieldCount.Should().Be(0,
            "target is no longer on the battlefield — illegal target");
        _alice.Zones.Graveyard.GetCards().Should().Contain(jar);
    }
}
