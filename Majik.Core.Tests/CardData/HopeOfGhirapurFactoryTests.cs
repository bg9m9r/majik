using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Hope of Ghirapur — Legendary Artifact Creature — Thopter
/// 1/1 {0} (Aether Revolt).
///
/// Covers:
/// - Card identity (Legendary supertype, Artifact+Creature, Thopter subtype,
///   mana cost {0}, P/T 1/1) + dispatcher routing.
/// - Flying keyword marker.
/// - Activated ability shape: sole cost is Sacrifice (no mana pip).
/// - Per-turn damage tracking: only players Hope has dealt combat damage
///   to this turn become legal restriction recipients.
/// - Sacrifice resolution: registers a noncreature-spell restriction on
///   the chosen damaged player (CR 601.3); Hope lands in graveyard.
/// - Illegal target (player NOT dealt damage by Hope) no-ops the
///   restriction half (CR 608.2b).
/// - Per-turn damage set clears at TurnStartedEvent.
/// - Noncreature restriction clears at the start of the controller's next
///   turn (CR 514.2); opponent's intermediate turn does NOT clear.
/// </summary>
public class HopeOfGhirapurFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public HopeOfGhirapurFactoryTests()
    {
        CastingRestrictions.Clear();
    }

    public void Dispose()
    {
        CastingRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HopeOfGhirapur_Identity()
    {
        var h = HopeOfGhirapurFactory.Create(_alice);

        h.Name.Should().Be("Hope of Ghirapur");
        h.ManaCost.Should().Be("{0}");
        h.Power.Should().Be(1);
        h.Toughness.Should().Be(1);
        h.HasType(CardType.Artifact).Should().BeTrue();
        h.HasType(CardType.Creature).Should().BeTrue();
        h.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        h.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
        h.Owner.Should().BeSameAs(_alice);
        h.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HopeOfGhirapur_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Hope of Ghirapur", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Hope of Ghirapur");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
    }

    [Fact]
    public void HopeOfGhirapur_HasFlying()
    {
        var h = HopeOfGhirapurFactory.Create(_alice);
        CombatAbilities.HasFlying(h).Should().BeTrue("printed Flying keyword");
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HopeOfGhirapur_SacAbility_HasSacrificeCost_AndNoManaCost()
    {
        var h = HopeOfGhirapurFactory.Create(_alice);
        var ability = h.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            ac => ac.CostType == AdditionalCostType.Sacrifice,
            "the sole cost is Sacrifice Hope of Ghirapur");
        ability.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "the printed activation has no mana pip");
    }

    // -----------------------------------------------------------------------
    // Damage tracking + sacrifice resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void SacAbility_OnResolve_AfterCombatDamageToPlayer_RegistersNoncreatureRestriction()
    {
        var bus = new EventBus();
        var h = HopeOfGhirapurFactory.Create(_alice, bus);
        _alice.Zones.Battlefield.AddCard(h);
        h.SetZone(ZoneType.Battlefield);

        // Hope deals combat damage to Bob (the canonical "dealt combat
        // damage by Hope" event).
        bus.Publish(new CombatDamageDealtEvent(h, _bob, 1));

        var ability = h.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new object[] { _bob } });
        foreach (var effect in ability.Effects) effect.Execute();

        // Hope sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(h);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(h);

        // Bob can't cast noncreature spells.
        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeTrue();
        CastingRestrictions.CannotCastNoncreatureSpell(_alice).Should().BeFalse();
    }

    [Fact]
    public void SacAbility_OnResolve_TargetNotDamagedByHope_NoOpsTheRestrictionHalf()
    {
        var bus = new EventBus();
        var h = HopeOfGhirapurFactory.Create(_alice, bus);
        _alice.Zones.Battlefield.AddCard(h);
        h.SetZone(ZoneType.Battlefield);

        // No damage event — set is empty.

        var ability = h.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new object[] { _bob } });
        foreach (var effect in ability.Effects) effect.Execute();

        // Sac still resolved, but restriction NOT registered.
        _alice.Zones.Graveyard.GetCards().Should().Contain(h);
        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeFalse(
            "Bob was not dealt combat damage by Hope of Ghirapur this turn");
    }

    [Fact]
    public void DamageDealtBy_AnotherSource_DoesNotPopulateHopesSet()
    {
        var bus = new EventBus();
        var h = HopeOfGhirapurFactory.Create(_alice, bus);
        _alice.Zones.Battlefield.AddCard(h);
        h.SetZone(ZoneType.Battlefield);

        // A different creature deals combat damage to Bob.
        var other = new Creature("Memnite", "{0}", 1, 1) { Owner = _alice, Controller = _alice };
        bus.Publish(new CombatDamageDealtEvent(other, _bob, 1));

        var ability = h.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new object[] { _bob } });
        foreach (var effect in ability.Effects) effect.Execute();

        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeFalse(
            "the printed condition is 'dealt combat damage by Hope of Ghirapur'");
    }

    [Fact]
    public void TurnStarted_ClearsPerTurnDamageSet()
    {
        var bus = new EventBus();
        var h = HopeOfGhirapurFactory.Create(_alice, bus);
        _alice.Zones.Battlefield.AddCard(h);
        h.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(h, _bob, 1));

        // Start of a new turn — per-turn set clears.
        bus.Publish(new TurnStartedEvent(_bob, 2));

        var ability = h.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new object[] { _bob } });
        foreach (var effect in ability.Effects) effect.Execute();

        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeFalse(
            "per-turn damage tracker cleared at TurnStartedEvent");
    }

    [Fact]
    public void ControllersNextTurnStart_ClearsNoncreatureRestriction()
    {
        var bus = new EventBus();
        var h = HopeOfGhirapurFactory.Create(_alice, bus);
        _alice.Zones.Battlefield.AddCard(h);
        h.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(h, _bob, 1));

        var ability = h.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new object[] { _bob } });
        foreach (var effect in ability.Effects) effect.Execute();
        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeTrue();

        // Opponent's turn does NOT clear (CR 514.2 — "until your next turn").
        bus.Publish(new TurnStartedEvent(_bob, 2));
        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeTrue(
            "opponent's turn must not clear the restriction");

        // Controller's next turn clears it.
        bus.Publish(new TurnStartedEvent(_alice, 3));
        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeFalse(
            "controller's next turn clears the noncreature restriction");
    }
}
