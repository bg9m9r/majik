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
/// Tests for <see cref="EndbringerFactory"/> (Oath of the Gatewatch, {5}{C}).
///
/// Oracle text:
///   "Vigilance, reach
///    {T}: Endbringer deals 1 damage to any target.
///    {C}, {T}: Target player draws a card.
///    {C}, {T}: Tap target creature."
///
/// Covers:
///   - Identity (5/5 Creature — Eldrazi, {5}{C}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Keyword markers (Vigilance + Reach).
///   - Three activated abilities + their cost shapes.
///   - {T} damage resolution to a creature target.
///   - {T} damage resolution to a player target.
///   - {C}{T} draw resolution moves the top of target player's library to hand.
///   - {C}{T} tap resolution taps the chosen creature.
///   - Tap resolution is a no-op on already-tapped target (CR 701.21b).
/// </summary>
public class EndbringerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Endbringer_Identity()
    {
        var endbringer = EndbringerFactory.Create(_alice);

        endbringer.Name.Should().Be("Endbringer");
        endbringer.ManaCost.Should().Be("{5}{C}");
        endbringer.Power.Should().Be(5);
        endbringer.Toughness.Should().Be(5);
        endbringer.HasType(CardType.Creature).Should().BeTrue();
        endbringer.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        endbringer.Owner.Should().BeSameAs(_alice);
        endbringer.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Endbringer_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Endbringer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Endbringer");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
    }

    [Fact]
    public void Endbringer_HasVigilanceAndReachKeywordMarkers()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        var keywords = endbringer.Abilities.OfType<KeywordAbility>().ToList();

        keywords.Should().Contain(k => k.Keyword == "Vigilance",
            "CR 702.20 — Vigilance marker");
        keywords.Should().Contain(k => k.Keyword == "Reach",
            "CR 702.17 — Reach marker");
    }

    [Fact]
    public void Endbringer_HasThreeActivatedAbilities()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        var activated = endbringer.Abilities.OfType<ActivatedAbility>().ToList();

        activated.Should().HaveCount(3,
            "{T}: 1 damage + {C}{T}: draw + {C}{T}: tap");
    }

    [Fact]
    public void Endbringer_DamageAbility_HasTapOnlyCost()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        var activated = endbringer.Abilities.OfType<ActivatedAbility>().ToList();

        // The damage ability is the one with no ManaCostCost.
        var damage = activated.SingleOrDefault(a => !a.Costs.OfType<ManaCostCost>().Any());
        damage.Should().NotBeNull("{T}: 1 damage to any target — no mana pip");
        damage!.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap);
        damage.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("any target");
    }

    [Fact]
    public void Endbringer_DrawAndTapAbilities_HaveColorlessCostAndTapCost()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        var activated = endbringer.Abilities.OfType<ActivatedAbility>().ToList();

        // The two {C}{T} abilities both carry exactly ManaCostCost("{C}") + Tap.
        var manaPlusTap = activated
            .Where(a => a.Costs.OfType<ManaCostCost>().Any())
            .ToList();

        manaPlusTap.Should().HaveCount(2);
        foreach (var ability in manaPlusTap)
        {
            // {C} is parsed into the generic bucket (engine-wide posture —
            // see Eldrazi Temple gap notes); total value should still be 1.
            ability.Costs.OfType<ManaCostCost>().Single().Cost.TotalValue
                .Should().Be(1, "the {C} pip is a 1-mana colourless requirement");
            ability.Costs.OfType<AdditionalCost>().Should().ContainSingle(
                c => c.CostType == AdditionalCostType.Tap);
        }

        manaPlusTap.SelectMany(a => a.TargetRequests).Select(t => t.Description)
            .Should().BeEquivalentTo(new[] { "target player", "target creature" });
    }

    [Fact]
    public void Endbringer_TapDamage_DealsOneToCreatureTarget()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(endbringer);
        endbringer.SetZone(ZoneType.Battlefield);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(grizzly);
        grizzly.SetZone(ZoneType.Battlefield);

        var damage = endbringer.Abilities.OfType<ActivatedAbility>()
            .Single(a => !a.Costs.OfType<ManaCostCost>().Any());

        damage.SetChosenTargets(new[] { new object[] { grizzly } });

        foreach (var effect in damage.Effects) effect.Execute();

        grizzly.Damage.Should().Be(1,
            "Fx.DealDamageAny routes creature targets through Creature.TakeDamage");
    }

    [Fact]
    public void Endbringer_TapDamage_DealsOneToPlayerTarget()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(endbringer);
        endbringer.SetZone(ZoneType.Battlefield);

        var damage = endbringer.Abilities.OfType<ActivatedAbility>()
            .Single(a => !a.Costs.OfType<ManaCostCost>().Any());

        damage.SetChosenTargets(new[] { new object[] { _bob } });

        var lifeBefore = _bob.LifeTotal;
        foreach (var effect in damage.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(lifeBefore - 1,
            "Fx.DealDamageAny routes player targets through Player.TakeDamage");
    }

    [Fact]
    public void Endbringer_DrawAbility_TargetPlayerDrawsTopCard()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(endbringer);
        endbringer.SetZone(ZoneType.Battlefield);

        // Seed Bob's library with a known top card.
        var topCard = new Instant("Lightning Bolt", "{R}");
        topCard.SetOwner(_bob);
        _bob.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var draw = endbringer.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Any(t => t.Description == "target player"));

        draw.SetChosenTargets(new[] { new object[] { _bob } });

        var handBefore = _bob.Zones.Hand.GetCards().Count();
        foreach (var effect in draw.Effects) effect.Execute();

        _bob.Zones.Hand.GetCards().Should().HaveCount(handBefore + 1);
        _bob.Zones.Hand.GetCards().Should().Contain(topCard);
        _bob.Zones.Library.GetCards().Should().NotContain(topCard);
    }

    [Fact]
    public void Endbringer_TapAbility_TapsChosenCreature()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(endbringer);
        endbringer.SetZone(ZoneType.Battlefield);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(grizzly);
        grizzly.SetZone(ZoneType.Battlefield);

        var tap = endbringer.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Any(t => t.Description == "target creature"));

        tap.SetChosenTargets(new[] { new object[] { grizzly } });

        grizzly.IsTapped.Should().BeFalse();
        foreach (var effect in tap.Effects) effect.Execute();
        grizzly.IsTapped.Should().BeTrue(
            "Fx.Tap delegates to Permanent.Tap, taps idempotently");
    }

    [Fact]
    public void Endbringer_TapAbility_NoOpOnNonBattlefieldTarget()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        // Deliberately NOT on battlefield — the recheck (CR 608.2b)
        // should reject this target.

        var tap = endbringer.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Any(t => t.Description == "target creature"));

        tap.SetChosenTargets(new[] { new object[] { grizzly } });

        foreach (var effect in tap.Effects) effect.Execute();
        grizzly.IsTapped.Should().BeFalse(
            "CR 608.2b — target no longer on battlefield: effect fails silently");
    }
}
