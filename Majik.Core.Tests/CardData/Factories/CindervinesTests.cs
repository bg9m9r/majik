using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Cindervines (Modern Horizons, {R}{G}).
///
/// Oracle text:
///   "Whenever an opponent casts a noncreature spell, this enchantment
///    deals 1 damage to that player."
///   "{1}, Sacrifice this enchantment: Destroy target artifact or
///    enchantment. This enchantment deals 2 damage to that permanent's
///    controller."
///
/// Covers:
///   - Card identity (Enchantment, {R}{G}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Opponent-noncreature-cast trigger: opponent's noncreature spell
///     deals 1 damage to that opponent; controller's own casts and
///     opponent creature spells do not fire (CR 603.1 / 109.5 / 202.3).
///   - Ability shape: single <see cref="ActivatedAbility"/> with a {1}
///     mana cost + sacrifice cost and one 1..1 target request.
///   - Resolution: target artifact → destroyed, its controller takes 2,
///     Cindervines sacrificed.
///   - Resolution: target enchantment → destroyed, controller takes 2.
///   - Resolution: target creature (illegal pick) → no destroy / no
///     damage, Cindervines still sacrificed (CR 608.2b).
///   - Resolution: target off battlefield at resolution → no destroy /
///     no damage, Cindervines still sacrificed (CR 608.2b).
/// </summary>
public class CindervinesTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Cindervines_IsEnchantment_AtRG()
    {
        var card = CindervinesFactory.Create(_alice);

        card.Name.Should().Be("Cindervines");
        card.ManaCost.Should().Be("{R}{G}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesCindervines()
    {
        var card = NamedCardFactory.Create("Cindervines", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Cindervines");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{R}{G}");
    }

    // -----------------------------------------------------------------------
    // Opponent-noncreature-cast trigger
    // -----------------------------------------------------------------------

    private static Majik.Core.Spells.Spell NewInstant(Player controller, string name, string manaCost)
    {
        var c = new Instant(name, manaCost) { Owner = controller };
        return new Majik.Core.Spells.Spell(c, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name, string manaCost)
    {
        var c = new Creature(name, manaCost: manaCost, power: 1, toughness: 1) { Owner = controller };
        return new Majik.Core.Spells.Spell(c, controller);
    }

    [Fact]
    public void Trigger_OpponentNoncreatureSpell_Deals1ToThatPlayer()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var cindervines = CindervinesFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(cindervines);
        cindervines.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstant(_bob, "Lightning Bolt", "{R}")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(19, "opponent's noncreature spell deals 1 to that opponent");
        _alice.LifeTotal.Should().Be(20, "Cindervines' controller is untouched");
    }

    [Fact]
    public void Trigger_OwnNoncreatureSpell_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var cindervines = CindervinesFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(cindervines);
        cindervines.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstant(_alice, "Own Bolt", "{R}")));

        triggers.PendingCount.Should().Be(0,
            "the controller's own casts do not fire 'an opponent casts'");
    }

    [Fact]
    public void Trigger_OpponentCreatureSpell_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var cindervines = CindervinesFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(cindervines);
        cindervines.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_bob, "Bear", "{1}{G}")));

        triggers.PendingCount.Should().Be(0,
            "creature spells do not fire a noncreature-cast trigger");
    }

    // -----------------------------------------------------------------------
    // Sacrifice activated ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Cindervines_HasSingleSacrificeAbility_WithManaAndOneTarget()
    {
        var card = CindervinesFactory.Create(_alice);

        var abilities = card.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(1);

        var ab = abilities[0];
        ab.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(x => x.CostType == AdditionalCostType.Sacrifice,
                "the printed cost includes 'Sacrifice this enchantment'");
        ab.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle("the ability carries a {1} mana component");

        ab.TargetRequests.Should().ContainSingle();
        ab.TargetRequests[0].MinTargets.Should().Be(1);
        ab.TargetRequests[0].MaxTargets.Should().Be(1);
        ab.TargetRequests[0].Description.Should()
            .Contain("artifact").And.Contain("enchantment");
    }

    [Fact]
    public void Activate_DestroysTargetArtifact_Deals2ToController_SacrificesSelf()
    {
        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var card = CindervinesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ab = card.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });
        ab.Resolve();

        trinket.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(trinket);
        _bob.LifeTotal.Should().Be(18, "the destroyed permanent's controller takes 2 damage");

        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
    }

    [Fact]
    public void Activate_DestroysTargetEnchantment_Deals2ToController_SacrificesSelf()
    {
        var ench = new Enchantment("Bob's Enchant", "{1}{G}");
        ench.SetOwner(_bob);
        ench.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(ench);
        ench.SetZone(ZoneType.Battlefield);

        var card = CindervinesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ab = card.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ench } });
        ab.Resolve();

        ench.Zone.Should().Be(ZoneType.Graveyard);
        _bob.LifeTotal.Should().Be(18, "the destroyed enchantment's controller takes 2 damage");
        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
    }

    [Fact]
    public void Activate_IllegalCreatureTarget_NoDestroyNoDamage_StillSacrifices()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var card = CindervinesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ab = card.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        ab.Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.LifeTotal.Should().Be(20, "an illegal (creature) target deals no damage — CR 608.2b");

        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
    }

    [Fact]
    public void Activate_TargetLeftBattlefield_NoDestroyNoDamage_StillSacrifices()
    {
        var trinket = new Artifact("Trinket", "{1}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var card = CindervinesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ab = card.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });

        // Target leaves before resolution.
        _bob.Zones.Battlefield.RemoveCard(trinket);
        _bob.Zones.Hand.AddCard(trinket);
        trinket.SetZone(ZoneType.Hand);

        ab.Resolve();

        trinket.Zone.Should().Be(ZoneType.Hand);
        _bob.LifeTotal.Should().Be(20, "no legal permanent at resolution → no damage — CR 608.2b");

        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
    }
}
