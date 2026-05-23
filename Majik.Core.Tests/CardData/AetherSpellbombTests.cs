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
/// Tests for <see cref="AetherSpellbombFactory"/> — Artifact {1} with two
/// sacrifice-self activated abilities:
///   "{U}, Sacrifice this artifact: Return target creature to its owner's hand."
///   "{1}, Sacrifice this artifact: Draw a card."
///
/// Covers:
/// - Card identity (Artifact, {1}, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Ability shape: two <see cref="ActivatedAbility"/>s with the correct
///   costs and (for the bounce) one <see cref="Majik.Core.Players.Agents.TargetRequest"/>.
/// - Bounce-mode resolution: target creature → owner's hand, spellbomb
///   sacrificed.
/// - Cantrip-mode resolution: controller draws 1, spellbomb sacrificed.
/// - Bouncing a non-creature target → resolution-time no-op (CR 608.2b)
///   but the sacrifice still resolves (cost was paid).
/// </summary>
public class AetherSpellbombTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherSpellbomb_IsArtifact_WithOneManaCost()
    {
        var bomb = AetherSpellbombFactory.Create(_alice);

        bomb.HasType(CardType.Artifact).Should().BeTrue();
        bomb.Name.Should().Be("Aether Spellbomb");
        bomb.ManaCost.Should().Be("{1}");
        bomb.Owner.Should().BeSameAs(_alice);
        bomb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AetherSpellbomb()
    {
        var card = NamedCardFactory.Create("Aether Spellbomb", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Aether Spellbomb");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherSpellbomb_HasTwoActivatedAbilities()
    {
        var bomb = AetherSpellbombFactory.Create(_alice);

        bomb.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void BounceAbility_HasU_AndSacrifice_AndOneCreatureTarget()
    {
        var bomb = AetherSpellbombFactory.Create(_alice);

        var bounce = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        bounce.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("U"),
                "the bounce mode costs {U}");
        bounce.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the bounce mode sacrifices the spellbomb");

        bounce.TargetRequests[0].MinTargets.Should().Be(1);
        bounce.TargetRequests[0].MaxTargets.Should().Be(1);
        bounce.TargetRequests[0].Description.Should().Contain("creature");
    }

    [Fact]
    public void DrawAbility_Has1Generic_AndSacrifice_AndNoTargets()
    {
        var bomb = AetherSpellbombFactory.Create(_alice);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        draw.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("1"),
                "the cantrip mode costs {1}");
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the cantrip mode sacrifices the spellbomb");
    }

    // -----------------------------------------------------------------------
    // {U}, sac: bounce target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Bounce_ReturnsTargetCreatureToOwnersHand_AndSacrificesSpellbomb()
    {
        // Bob controls Grizzly Bears; Alice activates {U}, sac to bounce.
        var bears = new Creature(
            name: "Grizzly Bears",
            manaCost: "{1}{G}",
            power: 2,
            toughness: 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var bomb = AetherSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var bounce = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        bounce.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        bounce.Resolve();

        // Bear is now in Bob's hand.
        _bob.Zones.Hand.GetCards().Should().Contain(bears);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);
        bears.Zone.Should().Be(ZoneType.Hand);

        // Spellbomb has been sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Bounce_OwnCreature_Works()
    {
        // Alice can bounce her own creature (e.g. to save it from removal
        // or to re-trigger an ETB). The bounce is not owner-restricted.
        var goyf = new Creature(
            name: "Tarmogoyf",
            manaCost: "{1}{G}",
            power: 0,
            toughness: 1);
        goyf.SetOwner(_alice);
        goyf.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(goyf);
        goyf.SetZone(ZoneType.Battlefield);

        var bomb = AetherSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var bounce = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        bounce.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { goyf },
        });

        bounce.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(goyf);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(goyf);
        goyf.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Activate_Bounce_NonCreatureTarget_IsNoOpOnBounce_ButStillSacrifices()
    {
        // Fed a non-creature pick (e.g. an artifact), the resolution-time
        // guard makes the bounce a no-op (CR 608.2b). The sacrifice cost
        // is still paid — modelled here as the effect closure moving the
        // bomb to GY regardless.
        var rando = new Artifact("Random Artifact", "{0}");
        rando.SetOwner(_bob);
        rando.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(rando);
        rando.SetZone(ZoneType.Battlefield);

        var bomb = AetherSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var bounce = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        bounce.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { rando },
        });

        bounce.Resolve();

        // Artifact stays put.
        _bob.Zones.Battlefield.GetCards().Should().Contain(rando);
        _bob.Zones.Hand.GetCards().Should().NotContain(rando);

        // Spellbomb still sacrificed (cost paid).
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
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

        var bomb = AetherSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

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
        // Empty library — draw is a silent no-op (SBAs handle the loss
        // condition elsewhere). The sacrifice still occurs.
        var bomb = AetherSpellbombFactory.Create(_alice);
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
