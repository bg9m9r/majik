using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AvatarOfWoeFactory"/>.
///
/// Avatar of Woe (Prophecy, {6}{B}{B}):
///   Creature — Avatar 6/5.
///   "Fear ... {T}: Destroy target creature. It can't be regenerated."
///
/// Covers:
///   - Identity (Avatar 6/5, {6}{B}{B}, owner/controller, Fear keyword stamped).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Activated ability shape: single {T} cost + one target-creature request
///     (CR 602). No mana cost on the ability.
///   - Resolution: the chosen creature is destroyed (moved to graveyard).
///   - "It can't be regenerated.": a regeneration shield on the target is NOT
///     consumed (DestroyNoRegeneration — CR 701.15).
/// </summary>
[Trait("Color", "B")]
public class AvatarOfWoeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ActivatedAbility DestroyAbility(Creature avatar)
        => avatar.Abilities.OfType<ActivatedAbility>().Single();

    [Fact]
    public void AvatarOfWoe_Identity()
    {
        var a = AvatarOfWoeFactory.Create(_alice);

        a.Name.Should().Be("Avatar of Woe");
        a.ManaCost.Should().Be("{6}{B}{B}");
        a.HasType(CardType.Creature).Should().BeTrue();
        a.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
        a.BasePower.Should().Be(6);
        a.BaseToughness.Should().Be(5);
        a.Owner.Should().BeSameAs(_alice);
        a.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AvatarOfWoe_HasFearKeyword()
    {
        var a = AvatarOfWoeFactory.Create(_alice);

        a.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Fear",
                "Avatar of Woe has Fear (CR 702.36) — stamped even though evasion is a v1 gap");
    }

    [Fact]
    public void AvatarOfWoe_DestroyAbility_HasSingleTapCostAndTargetCreature()
    {
        var a = AvatarOfWoeFactory.Create(_alice);
        var ability = DestroyAbility(a);

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the only cost is the {T} symbol");
        ability.Costs.OfType<ManaCostCost>().Should().BeEmpty("the ability has no mana cost");
        ability.TargetRequests.Should().ContainSingle();
        ability.TargetRequests[0].Description.Should().Contain("target creature");
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.RebindSafe.Should().BeTrue(
            "the destroy ability references only the chosen target, so it re-homes via Agatha");
    }

    [Fact]
    public void AvatarOfWoe_Destroy_MovesChosenCreatureToGraveyard()
    {
        var a = AvatarOfWoeFactory.Create(_alice);
        a.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(a);

        var victim = new Creature("Victim Bear", "1G", 2, 2);
        victim.SetOwner(_alice);
        victim.ChangeController(_alice);
        _alice.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var ability = DestroyAbility(a);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { victim } });
        ability.Resolve();

        _alice.Zones.Graveyard.GetCards().Should().Contain(victim,
            "the destroy moves the chosen creature to its owner's graveyard (CR 701.7)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(victim);
    }

    [Fact]
    public void AvatarOfWoe_Destroy_CannotBeRegenerated_ShieldNotConsumed()
    {
        var a = AvatarOfWoeFactory.Create(_alice);
        a.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(a);

        var victim = new Creature("Shielded Bear", "1G", 2, 2);
        victim.SetOwner(_alice);
        victim.ChangeController(_alice);
        _alice.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);
        victim.AddRegenerationShield();

        var ability = DestroyAbility(a);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { victim } });
        ability.Resolve();

        _alice.Zones.Graveyard.GetCards().Should().Contain(victim,
            "'It can't be regenerated.' suppresses CR 701.15 — the shield does not save the creature");
        victim.HasRegenerationShield.Should().BeTrue(
            "DestroyNoRegeneration bypasses regeneration, so the shield is left unconsumed");
    }
}
