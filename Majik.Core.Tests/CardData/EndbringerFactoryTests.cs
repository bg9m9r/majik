using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="EndbringerFactory"/> (Oath of the Gatewatch, {5}{C}).
///
/// Oracle text (Scryfall, verified 2025):
///   "Untap this creature during each other player's untap step.
///    {T}: This creature deals 1 damage to any target.
///    {C}, {T}: Target creature can't attack or block this turn.
///    {C}{C}, {T}: Draw a card."
///
/// (The factory previously shipped a STALE oracle: "Vigilance, reach /
/// {C},{T}: Target player draws / {C},{T}: Tap target creature." These tests
/// exercise the rewritten current printed text.)
///
/// Covers:
///   - Identity (5/5 Creature — Eldrazi, {5}{C}, owner / controller).
///   - NamedCardFactory dispatch.
///   - No Vigilance / Reach markers (the stale clause is gone).
///   - Three activated abilities + their cost shapes.
///   - {T} damage resolution to a creature target.
///   - {T} damage resolution to a player target.
///   - {C}{T} "can't attack or block" registers both combat restrictions.
///   - {C}{C}{T} draw moves the top of controller's library to hand.
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
    public void Endbringer_HasNoVigilanceOrReachMarkers()
    {
        // The stale oracle's "Vigilance, reach" line is gone in the current
        // printing — Endbringer has no keyword markers.
        var endbringer = EndbringerFactory.Create(_alice);
        var keywords = endbringer.Abilities.OfType<KeywordAbility>().ToList();

        keywords.Should().BeEmpty(
            "current printed Endbringer has no Vigilance / Reach");
    }

    [Fact]
    public void Endbringer_HasThreeActivatedAbilities()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        var activated = endbringer.Abilities.OfType<ActivatedAbility>().ToList();

        activated.Should().HaveCount(3,
            "{T}: 1 damage + {C}{T}: can't attack/block + {C}{C}{T}: draw");
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
    public void Endbringer_CantAttackOrBlockAbility_HasSingleColorlessAndTapCost()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        var activated = endbringer.Abilities.OfType<ActivatedAbility>().ToList();

        var ability = activated.Single(
            a => a.TargetRequests.Any(t => t.Description == "target creature"));

        // {C} parses into the generic bucket (engine-wide posture — see
        // Eldrazi Temple gap notes); total value should be 1.
        ability.Costs.OfType<ManaCostCost>().Single().Cost.TotalValue
            .Should().Be(1, "the {C} pip is a 1-mana colourless requirement");
        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap);
    }

    [Fact]
    public void Endbringer_DrawAbility_HasDoubleColorlessTapCostAndNoTarget()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        var activated = endbringer.Abilities.OfType<ActivatedAbility>().ToList();

        var draw = activated.Single(
            a => a.Effects.Any(e => e.Description.Contains("draw", StringComparison.OrdinalIgnoreCase)));

        draw.TargetRequests.Should().BeEmpty("\"Draw a card.\" has no target");
        // {C}{C} parses into the generic bucket; total value should be 2.
        draw.Costs.OfType<ManaCostCost>().Single().Cost.TotalValue
            .Should().Be(2, "the {C}{C} pips are a 2-mana colourless requirement");
        draw.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap);
    }

    [Fact]
    public async Task Endbringer_TapDamage_DealsOneToCreatureTarget()
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
        await damage.ResolveAsync(agent: null, game: null);

        grizzly.Damage.Should().Be(1,
            "Fx.DealDamageAny routes creature targets through Creature.TakeDamage");
    }

    [Fact]
    public async Task Endbringer_TapDamage_DealsOneToPlayerTarget()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(endbringer);
        endbringer.SetZone(ZoneType.Battlefield);

        var damage = endbringer.Abilities.OfType<ActivatedAbility>()
            .Single(a => !a.Costs.OfType<ManaCostCost>().Any());

        damage.SetChosenTargets(new[] { new object[] { _bob } });

        var lifeBefore = _bob.LifeTotal;
        await damage.ResolveAsync(agent: null, game: null);

        _bob.LifeTotal.Should().Be(lifeBefore - 1,
            "Fx.DealDamageAny routes player targets through Player.TakeDamage");
    }

    [Fact]
    public async Task Endbringer_CantAttackOrBlock_RegistersBothRestrictionsOnTarget()
    {
        var bus = new EventBus();
        var endbringer = EndbringerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(endbringer);
        endbringer.SetZone(ZoneType.Battlefield);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        // The combat restriction lives on the target's own ContinuousEffectsService.
        var targetEffects = new ContinuousEffectsService(bus);
        grizzly.ActiveEffects = targetEffects;
        _bob.Zones.Battlefield.AddCard(grizzly);
        grizzly.SetZone(ZoneType.Battlefield);

        var ability = endbringer.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Any(t => t.Description == "target creature"));

        ability.SetChosenTargets(new[] { new object[] { grizzly } });
        await ability.ResolveAsync(agent: null, game: null);

        targetEffects.HasRestriction(grizzly, CombatRestriction.CannotAttack)
            .Should().BeTrue("CR 508.1c — the target can't attack this turn");
        targetEffects.HasRestriction(grizzly, CombatRestriction.CannotBlock)
            .Should().BeTrue("CR 509.1c — the target can't block this turn");
    }

    [Fact]
    public async Task Endbringer_CantAttackOrBlock_NoOpOnNonBattlefieldTarget()
    {
        var bus = new EventBus();
        var endbringer = EndbringerFactory.Create(_alice);
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        var targetEffects = new ContinuousEffectsService(bus);
        grizzly.ActiveEffects = targetEffects;
        // Deliberately NOT on battlefield — CR 608.2b rejects this target.

        var ability = endbringer.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Any(t => t.Description == "target creature"));

        ability.SetChosenTargets(new[] { new object[] { grizzly } });
        await ability.ResolveAsync(agent: null, game: null);

        targetEffects.HasRestriction(grizzly, CombatRestriction.CannotAttack)
            .Should().BeFalse("CR 608.2b — off-battlefield target: effect fails silently");
        targetEffects.HasRestriction(grizzly, CombatRestriction.CannotBlock)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Endbringer_DrawAbility_ControllerDrawsTopCard()
    {
        var endbringer = EndbringerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(endbringer);
        endbringer.SetZone(ZoneType.Battlefield);

        // Seed Alice's library with a known top card.
        var topCard = new Instant("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var draw = endbringer.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Effects.Any(e =>
                e.Description.Contains("draw", StringComparison.OrdinalIgnoreCase)));

        var handBefore = _alice.Zones.Hand.GetCards().Count();
        await draw.ResolveAsync(agent: null, game: null);

        _alice.Zones.Hand.GetCards().Should().HaveCount(handBefore + 1);
        _alice.Zones.Hand.GetCards().Should().Contain(topCard);
        _alice.Zones.Library.GetCards().Should().NotContain(topCard);
    }
}
