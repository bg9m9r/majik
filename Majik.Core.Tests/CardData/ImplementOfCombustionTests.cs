using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ImplementOfCombustionFactory"/> — Artifact {1}.
///   "{R}, Sacrifice this artifact: It deals 1 damage to target player or
///    planeswalker.
///    When this artifact is put into a graveyard from the battlefield,
///    draw a card."
///
/// Closest analogues: <see cref="PyriteSpellbombFactory"/> (the {R}-sac
/// targeted-damage activated ability) and <see cref="IchorWellspringFactory"/>
/// (the Battlefield → Graveyard draw trigger). Implement of Combustion's
/// damage targets only a player or planeswalker (not "any target"), so the
/// resolution routes through <see cref="Majik.Core.Primitives.Fx.DealDamageAny"/>
/// which handles Player (life loss, CR 119) and Planeswalker (loyalty
/// removal, CR 306.7).
///
/// Covers:
/// - Identity (Artifact, {1}, owner/controller) + NamedCardFactory dispatch.
/// - One activated ability ({R} + Sacrifice + 1 target) and one triggered
///   ability (dies → draw).
/// - Damage resolution to a player target (1 damage) + spellbomb sacrificed.
/// - Damage resolution to a planeswalker target (1 loyalty removed).
/// - Dies trigger fires only on Battlefield → Graveyard and draws one card.
/// </summary>
public class ImplementOfCombustionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ImplementOfCombustion_IsArtifact_WithOneManaCost()
    {
        var impl = ImplementOfCombustionFactory.Create(_alice);

        impl.HasType(CardType.Artifact).Should().BeTrue();
        impl.Name.Should().Be("Implement of Combustion");
        impl.ManaCost.Should().Be("{1}");
        impl.Owner.Should().BeSameAs(_alice);
        impl.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ImplementOfCombustion()
    {
        var card = NamedCardFactory.Create("Implement of Combustion", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Implement of Combustion");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
    }

    // -----------------------------------------------------------------------
    // Ability shape — one activated ({R}, sac, 1 target) + one trigger (dies)
    // -----------------------------------------------------------------------

    [Fact]
    public void ImplementOfCombustion_HasOneActivated_AndOneTriggered()
    {
        var impl = ImplementOfCombustionFactory.Create(_alice);

        impl.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        impl.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void DamageAbility_HasR_AndSacrifice_AndOneTarget()
    {
        var impl = ImplementOfCombustionFactory.Create(_alice);

        var dmg = impl.Abilities.OfType<ActivatedAbility>().Single();

        dmg.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("R"),
                "the damage mode costs {R}");
        dmg.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the damage mode sacrifices the artifact");

        dmg.TargetRequests.Should().HaveCount(1);
        dmg.TargetRequests[0].MinTargets.Should().Be(1);
        dmg.TargetRequests[0].MaxTargets.Should().Be(1);
        dmg.TargetRequests[0].Description.Should().Contain("player or planeswalker");
    }

    // -----------------------------------------------------------------------
    // {R}, sac: 1 damage to target player or planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_DealsOneToPlayerTarget_AndSacrifices()
    {
        var impl = ImplementOfCombustionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(impl);
        impl.SetZone(ZoneType.Battlefield);

        var dmg = impl.Abilities.OfType<ActivatedAbility>().Single();
        dmg.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        dmg.Resolve();

        _bob.LifeTotal.Should().Be(19, "1 damage to Bob");
        _bob.LifeLostThisTurn.Should().Be(1);

        _alice.Zones.Graveyard.GetCards().Should().Contain(impl);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(impl);
        impl.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_PlaneswalkerTarget_RoutesToLoyaltyRemoval()
    {
        // CR 306.7 — 1 damage to a planeswalker removes 1 loyalty counter.
        var pw = new Planeswalker("Test Walker", "{3}", startingLoyalty: 4,
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var impl = ImplementOfCombustionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(impl);
        impl.SetZone(ZoneType.Battlefield);

        var dmg = impl.Abilities.OfType<ActivatedAbility>().Single();
        dmg.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        dmg.Resolve();

        pw.Loyalty.Should().Be(3, "1 loyalty counter removed (4 - 1)");
    }

    // -----------------------------------------------------------------------
    // Dies trigger — Battlefield → Graveyard draws a card
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_FiresOnBattlefieldToGraveyard_Only()
    {
        var impl = ImplementOfCombustionFactory.Create(_alice);
        impl.SetZone(ZoneType.Battlefield);

        var trigger = impl.Abilities.OfType<TriggeredAbility>().Single();

        var dies = new CardMovedEvent(impl, ZoneType.Battlefield, ZoneType.Graveyard);
        trigger.IsTriggered(dies).Should().BeTrue(
            "the dies trigger fires on Battlefield → Graveyard");

        var bounce = new CardMovedEvent(impl, ZoneType.Battlefield, ZoneType.Hand);
        trigger.IsTriggered(bounce).Should().BeFalse(
            "Battlefield → Hand is a bounce, not LTB-to-graveyard");

        var exile = new CardMovedEvent(impl, ZoneType.Battlefield, ZoneType.Exile);
        trigger.IsTriggered(exile).Should().BeFalse(
            "Battlefield → Exile bypasses the graveyard step");
    }

    [Fact]
    public void DiesTrigger_Resolve_DrawsACard()
    {
        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var impl = ImplementOfCombustionFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(impl);
        impl.SetZone(ZoneType.Graveyard);

        var trigger = impl.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top, "dies trigger drew one card");
        top.Zone.Should().Be(ZoneType.Hand);
    }
}
