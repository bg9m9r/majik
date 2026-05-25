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
/// Tests for <see cref="PyriteSpellbombFactory"/> — Artifact {1} with two
/// sacrifice-self activated abilities:
///   "{T}, Sacrifice ~: ~ deals 2 damage to any target."
///   "{R}, Sacrifice ~: Draw a card."
///
/// Covers:
/// - Identity (Artifact, {1}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Ability shape: two <see cref="ActivatedAbility"/>s with the correct
///   cost shapes; damage mode has 1..1 target request.
/// - Damage-mode resolution: 2 damage to player / creature target via
///   <see cref="Majik.Core.Primitives.Fx.DealDamageAny"/>; spellbomb
///   sacrificed.
/// - Damage-mode planeswalker target → loyalty removal (CR 306.7).
/// - Cantrip-mode resolution: controller draws 1, spellbomb sacrificed.
/// </summary>
public class PyriteSpellbombTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PyriteSpellbomb_IsArtifact_WithOneManaCost()
    {
        var bomb = PyriteSpellbombFactory.Create(_alice);

        bomb.HasType(CardType.Artifact).Should().BeTrue();
        bomb.Name.Should().Be("Pyrite Spellbomb");
        bomb.ManaCost.Should().Be("{1}");
        bomb.Owner.Should().BeSameAs(_alice);
        bomb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PyriteSpellbomb()
    {
        var card = NamedCardFactory.Create("Pyrite Spellbomb", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Pyrite Spellbomb");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PyriteSpellbomb_HasTwoActivatedAbilities()
    {
        var bomb = PyriteSpellbombFactory.Create(_alice);
        bomb.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void DamageAbility_HasTap_AndSacrifice_AndOneAnyTarget()
    {
        var bomb = PyriteSpellbombFactory.Create(_alice);

        var dmg = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        dmg.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the damage mode costs {T}");
        dmg.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the damage mode sacrifices the spellbomb");

        dmg.TargetRequests[0].MinTargets.Should().Be(1);
        dmg.TargetRequests[0].MaxTargets.Should().Be(1);
        dmg.TargetRequests[0].Description.Should().Contain("any target");
    }

    [Fact]
    public void DrawAbility_HasR_AndSacrifice_AndNoTargets()
    {
        var bomb = PyriteSpellbombFactory.Create(_alice);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        draw.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("R"),
                "the cantrip mode costs {R}");
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the cantrip mode sacrifices the spellbomb");
    }

    // -----------------------------------------------------------------------
    // {T}, sac: 2 damage to any target
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Damage_DealsTwoToPlayerTarget_AndSacrificesSpellbomb()
    {
        var bomb = PyriteSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var dmg = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        dmg.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        dmg.Resolve();

        _bob.LifeTotal.Should().Be(18, "2 damage to Bob");
        _bob.LifeLostThisTurn.Should().Be(2);

        // Spellbomb sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Damage_DealsTwoToCreatureTarget()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var bomb = PyriteSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var dmg = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        dmg.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        dmg.Resolve();

        bears.Damage.Should().Be(2, "2 marked damage on the bears");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
    }

    [Fact]
    public void Activate_Damage_PlaneswalkerTarget_RoutesToLoyaltyRemoval()
    {
        // CR 306.7 — damage to a planeswalker removes that many loyalty
        // counters. Fx.DealDamageAny routes Planeswalker → RemoveLoyalty.
        var pw = new Planeswalker("Test Walker", "{3}", startingLoyalty: 4,
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var bomb = PyriteSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var dmg = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        dmg.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        dmg.Resolve();

        pw.Loyalty.Should().Be(2, "2 loyalty counters removed (4 - 2)");
    }

    // -----------------------------------------------------------------------
    // {R}, sac: draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Cantrip_DrawsACard_AndSacrificesSpellbomb()
    {
        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var bomb = PyriteSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);

        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Cantrip_EmptyLibrary_FlagsAndStillSacrifices()
    {
        var bomb = PyriteSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }
}
