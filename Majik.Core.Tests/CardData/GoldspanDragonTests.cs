using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Targeting;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Goldspan Dragon (Kaldheim, {3}{R}{R}) and the underlying
/// Treasure-mana-modify static seam (deferral
/// <c>treasure-mana-ability-modify-static</c>).
///
/// Goldspan Dragon — Creature — Dragon 4/4. Oracle text (Scryfall-verified):
///   "Flying, haste
///    Whenever this creature attacks or becomes the target of a spell, create
///    a Treasure token.
///    Treasures you control have '{T}, Sacrifice this artifact: Add two mana
///    of any one color.'"
/// </summary>
public class GoldspanDragonTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GoldspanDragonTests()
    {
        _zones = new ZoneService(_bus);
    }

    [Fact]
    public void Goldspan_Is_Dragon_4_4_At3RR_WithFlyingHaste()
    {
        var dragon = GoldspanDragonFactory.Create(_alice);

        dragon.Name.Should().Be("Goldspan Dragon");
        dragon.ManaCost.Should().Be("{3}{R}{R}");
        dragon.HasType(CardType.Creature).Should().BeTrue();
        dragon.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        dragon.BasePower.Should().Be(4);
        dragon.BaseToughness.Should().Be(4);
        CombatAbilities.HasFlying(dragon).Should().BeTrue();
        CombatAbilities.HasHaste(dragon).Should().BeTrue();
    }

    [Fact]
    public void NamedFactory_Dispatches_Goldspan()
    {
        var card = NamedCardFactory.Create("Goldspan Dragon", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Goldspan Dragon");
    }

    [Fact]
    public void Treasure_WithoutModifier_ProducesOneMana()
    {
        var treasure = TokenFactory.CreateTreasure(_alice, _zones);

        // Red option (index 3 — W,U,B,R,G order).
        var redAbility = treasure.Abilities.OfType<ManaAbility>().ElementAt(3);
        var produced = redAbility.Activate();

        produced.Red.Should().Be(1);
        produced.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Treasure_UnderGoldspan_ProducesTwoManaOfChosenColor()
    {
        // Goldspan on Alice's battlefield → its continuous static is active.
        var dragon = GoldspanDragonFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(dragon);
        dragon.SetZone(ZoneType.Battlefield);

        var treasure = TokenFactory.CreateTreasure(_alice, _zones);

        // Green option (index 4) → "two mana of any ONE color" = {G}{G}.
        var greenAbility = treasure.Abilities.OfType<ManaAbility>().ElementAt(4);
        var produced = greenAbility.Activate();

        produced.Green.Should().Be(2);
        produced.TotalValue.Should().Be(2);
    }

    [Fact]
    public void Treasure_UnderGoldspan_IsControllerScoped_OpponentTreasureUnaffected()
    {
        var dragon = GoldspanDragonFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(dragon);
        dragon.SetZone(ZoneType.Battlefield);

        // Bob's Treasure — Bob controls no modifier → still one mana.
        var bobTreasure = TokenFactory.CreateTreasure(_bob, _zones);
        var redAbility = bobTreasure.Abilities.OfType<ManaAbility>().ElementAt(3);
        var produced = redAbility.Activate();

        produced.Red.Should().Be(1);
        produced.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Treasure_AfterGoldspanLeaves_ProducesOneManaAgain()
    {
        var dragon = GoldspanDragonFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(dragon);
        dragon.SetZone(ZoneType.Battlefield);
        var treasure = TokenFactory.CreateTreasure(_alice, _zones);

        // While Goldspan is in play: two mana.
        treasure.Abilities.OfType<ManaAbility>().ElementAt(0).Activate().TotalValue.Should().Be(2);

        // Goldspan dies — the continuous effect ends (CR 604.2).
        _alice.Zones.Battlefield.RemoveCard(dragon);
        dragon.SetZone(ZoneType.Graveyard);

        // A FRESH Treasure now produces one mana (the static is gone).
        var treasure2 = TokenFactory.CreateTreasure(_alice, _zones);
        treasure2.Abilities.OfType<ManaAbility>().ElementAt(0).Activate().TotalValue.Should().Be(1);
    }

    [Fact]
    public void Goldspan_OnAttack_CreatesTreasure()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var dragon = GoldspanDragonFactory.Create(
            _alice, eventBus: _bus, triggers: triggers, zoneService: _zones);
        _alice.Zones.Battlefield.AddCard(dragon);
        dragon.SetZone(ZoneType.Battlefield);

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Treasure)).Should().Be(0);

        _bus.Publish(new CreatureAttacksEvent(dragon, _bob));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Treasure)).Should().Be(1);
    }

    [Fact]
    public void Goldspan_BecomesTargetOfSpell_CreatesTreasure()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var dragon = GoldspanDragonFactory.Create(
            _alice, eventBus: _bus, triggers: triggers, zoneService: _zones);
        _alice.Zones.Battlefield.AddCard(dragon);
        dragon.SetZone(ZoneType.Battlefield);

        // A spell targeting Goldspan (Bob's removal).
        var shockCard = new Instant("Shock", "R") { Owner = _bob, Controller = _bob };
        var spell = new Majik.Core.Spells.Spell(shockCard, _bob);
        var target = Target.Permanent(dragon);
        _bus.Publish(new TargetsChosenEvent(spell, new ITarget[] { target }));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Treasure)).Should().Be(1);
    }
}
