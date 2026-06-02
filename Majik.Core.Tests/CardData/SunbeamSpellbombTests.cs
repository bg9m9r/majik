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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="SunbeamSpellbombFactory"/> — Artifact {1} with two
/// sacrifice-self activated abilities (mirrors Aether Spellbomb's shape, but
/// neither mode targets):
///   "{W}, Sacrifice this artifact: You gain 5 life."
///   "{1}, Sacrifice this artifact: Draw a card."
///
/// Covers:
/// - Card identity (Artifact, {1}, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Ability shape: two <see cref="ActivatedAbility"/>s with the correct costs
///   and no targets.
/// - Lifegain-mode resolution: controller gains 5 life, spellbomb sacrificed.
/// - Cantrip-mode resolution: controller draws 1, spellbomb sacrificed.
/// </summary>
public class SunbeamSpellbombTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SunbeamSpellbomb_IsArtifact_WithOneManaCost()
    {
        var bomb = SunbeamSpellbombFactory.Create(_alice);

        bomb.HasType(CardType.Artifact).Should().BeTrue();
        bomb.Name.Should().Be("Sunbeam Spellbomb");
        bomb.ManaCost.Should().Be("{1}");
        bomb.Owner.Should().BeSameAs(_alice);
        bomb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SunbeamSpellbomb()
    {
        var card = NamedCardFactory.Create("Sunbeam Spellbomb", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Sunbeam Spellbomb");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SunbeamSpellbomb_HasTwoActivatedAbilities()
    {
        var bomb = SunbeamSpellbombFactory.Create(_alice);

        bomb.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void LifegainAbility_HasW_AndSacrifice_AndNoTargets()
    {
        var bomb = SunbeamSpellbombFactory.Create(_alice);

        // The two abilities are distinguished by their mana cost; both have
        // zero targets.
        var lifegain = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Description.Contains("W")));

        lifegain.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("W"),
                "the lifegain mode costs {W}");
        lifegain.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the lifegain mode sacrifices the spellbomb");
        lifegain.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void DrawAbility_Has1Generic_AndSacrifice_AndNoTargets()
    {
        var bomb = SunbeamSpellbombFactory.Create(_alice);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Description.Contains("1")));

        draw.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("1"),
                "the cantrip mode costs {1}");
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the cantrip mode sacrifices the spellbomb");
        draw.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {W}, sac: you gain 5 life
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Lifegain_GainsFiveLife_AndSacrificesSpellbomb()
    {
        var bomb = SunbeamSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var lifegain = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Description.Contains("W")));

        lifegain.Resolve();

        // Controller gained 5 life (CR 119.3).
        _alice.LifeTotal.Should().Be(25);

        // Spellbomb has been sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // {1}, sac: draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Cantrip_DrawsACard_AndSacrificesSpellbomb()
    {
        var top = new Card("Top of library", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var bomb = SunbeamSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Description.Contains("1")));

        draw.Resolve();

        // Drew the top card.
        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);

        // Spellbomb sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Cantrip_EmptyLibrary_NoDraw_ButStillSacrifices()
    {
        var bomb = SunbeamSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Description.Contains("1")));

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }
}
