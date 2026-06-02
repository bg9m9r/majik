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
/// Unit tests for <see cref="PiaAndKiranNalaarFactory"/> (Magic Origins).
///
/// Oracle (Scryfall, verified):
///   "When Pia and Kiran Nalaar enters, create two 1/1 colorless Thopter
///    artifact creature tokens with flying.
///    {2}{R}, Sacrifice an artifact: Pia and Kiran Nalaar deals 2 damage to
///    any target."
///
/// 2/2 Legendary Creature — Human Artificer, {2}{R}{R}.
///
/// Covers:
/// - Identity ({2}{R}{R}, 2/2, Legendary Human Artificer).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB trigger mints TWO 1/1 colourless flying Thopter artifact creature
///   tokens (mirrors Pia Nalaar's single-Thopter ETB, doubled).
/// - {2}{R}, Sacrifice an artifact: 2 damage to any target — ability shape
///   (mana + SacrificeAnArtifactCost, one any-target request).
/// - Sacrifice-cost wiring (can't pay without an artifact / can with one).
/// </summary>
public class PiaAndKiranNalaarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PiaAndKiranNalaar_Identity_LegendaryHumanArtificer()
    {
        var card = PiaAndKiranNalaarFactory.Create(_alice);

        card.Name.Should().Be("Pia and Kiran Nalaar");
        card.ManaCost.ToString().Should().Be("{2}{R}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PiaAndKiranNalaar_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Pia and Kiran Nalaar", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Pia and Kiran Nalaar");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability set
    // -----------------------------------------------------------------------

    [Fact]
    public void PiaAndKiranNalaar_HasExactlyOneEtbTrigger()
    {
        var card = PiaAndKiranNalaarFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"create two Thopters\" trigger");
    }

    [Fact]
    public void PiaAndKiranNalaar_HasOneActivatedAbility()
    {
        var card = PiaAndKiranNalaarFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {2}{R},Sac-an-artifact 2-damage ability");
    }

    [Fact]
    public void PiaAndKiranNalaar_DamageAbility_HasManaPlusSacrificeArtifactCost()
    {
        var card = PiaAndKiranNalaarFactory.Create(_alice);
        var dmg = card.Abilities.OfType<ActivatedAbility>().Single();

        dmg.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the {2}{R} mana portion");
        dmg.Costs.OfType<SacrificeAnArtifactCost>().Should().ContainSingle(
            "Sacrifice an artifact (CR 118.5)");
        dmg.TargetRequests.Should().HaveCount(1);
        dmg.TargetRequests[0].MinTargets.Should().Be(1);
        dmg.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB trigger — create two 1/1 flying Thopter artifact creature tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void PiaAndKiranNalaar_EtbEffect_MintsTwoFlyingThopterTokens()
    {
        var alice = new Player("Alice", 20);
        var card = PiaAndKiranNalaarFactory.Create(alice);
        // Put the card on the battlefield so the ETB source-zone check
        // (CR 603.6c) passes.
        card.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(card);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var thopters = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Thopter")
            .ToList();

        thopters.Should().HaveCount(2, "the ETB creates two Thopter tokens");
        foreach (var thopter in thopters)
        {
            thopter.IsToken.Should().BeTrue("CR 111.1 — minted as a token");
            thopter.BasePower.Should().Be(1);
            thopter.BaseToughness.Should().Be(1);
            thopter.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
            thopter.HasType(CardType.Creature).Should().BeTrue();
            thopter.HasType(CardType.Artifact).Should().BeTrue(
                "Thopter token is an Artifact Creature (CR 111.1)");
            thopter.Abilities.OfType<KeywordAbility>()
                .Should().Contain(k => k.Keyword == "Flying",
                    "the printed Thopter token has flying (CR 702.9)");
        }
    }

    [Fact]
    public void PiaAndKiranNalaar_EtbEffect_NoOpWhenNotOnBattlefield()
    {
        // CR 603.6c — the ETB resolution short-circuits when the source isn't
        // on the battlefield.
        var alice = new Player("Alice", 20);
        var card = PiaAndKiranNalaarFactory.Create(alice);
        card.SetZone(ZoneType.Hand);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Thopter")
            .Should().BeEmpty("no tokens when the source isn't on the battlefield");
    }

    // -----------------------------------------------------------------------
    // {2}{R}, Sacrifice an artifact: 2 damage to any target
    // -----------------------------------------------------------------------

    [Fact]
    public void PiaAndKiranNalaar_DamageEffect_DealsTwoDamageToTargetCreature()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var card = PiaAndKiranNalaarFactory.Create(alice);

        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);

        var dmg = card.Abilities.OfType<ActivatedAbility>().Single();
        dmg.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        foreach (var effect in dmg.Effects) effect.Execute();

        target.Damage.Should().Be(2, "deals 2 damage to any target");
    }

    [Fact]
    public void PiaAndKiranNalaar_DamageEffect_DealsTwoDamageToPlayer()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var card = PiaAndKiranNalaarFactory.Create(alice);

        var dmg = card.Abilities.OfType<ActivatedAbility>().Single();
        dmg.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        foreach (var effect in dmg.Effects) effect.Execute();

        bob.LifeTotal.Should().Be(18, "20 - 2 = 18");
    }

    // -----------------------------------------------------------------------
    // Sacrifice-an-artifact cost wiring
    // -----------------------------------------------------------------------

    [Fact]
    public void PiaAndKiranNalaar_DamageCost_CannotPayWithoutAnArtifact()
    {
        var alice = new Player("Alice", 20);
        var card = PiaAndKiranNalaarFactory.Create(alice);
        var sac = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<SacrificeAnArtifactCost>().Single();

        sac.CanPay(alice).Should().BeFalse(
            "no artifact on the battlefield to sacrifice (Pia and Kiran is not an artifact)");
    }

    [Fact]
    public void PiaAndKiranNalaar_DamageCost_CanPayWithAnArtifact()
    {
        var alice = new Player("Alice", 20);
        var card = PiaAndKiranNalaarFactory.Create(alice);

        var fodder = new Artifact("Ornithopter", "0");
        fodder.SetOwner(alice);
        fodder.SetController(alice);
        fodder.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(fodder);

        var sac = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<SacrificeAnArtifactCost>().Single();

        sac.CanPay(alice).Should().BeTrue("an artifact is available to sacrifice");
    }
}
