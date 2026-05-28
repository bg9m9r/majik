using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class OracleTriggeredAbilityBinderTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ETB_GainLife_BindsAndFires()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var warden = new Creature("Soul Warden", "W", 1, 1) { Owner = _alice, Controller = _alice };
        warden.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(warden);

        var entity = new CardEntity
        {
            Name = "Soul Warden",
            OracleText = "When ~ enters the battlefield, you gain 1 life.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(warden, entity))
        {
            warden.AddAbility(ab);
        }
        triggers.BindCard(warden);

        zones.MoveCardTo(warden, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void ETB_DrawCards_BindsAndFires()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        for (var i = 0; i < 5; i++)
        {
            var c = NamedCardFactory.Create("Mountain", _alice);
            _alice.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library);
        }

        var mull = new Creature("Mulldrifter", "4U", 2, 2) { Owner = _alice, Controller = _alice };
        mull.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mull);

        var entity = new CardEntity
        {
            Name = "Mulldrifter",
            OracleText = "When ~ enters the battlefield, draw two cards.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(mull, entity))
        {
            mull.AddAbility(ab);
        }
        triggers.BindCard(mull);

        zones.MoveCardTo(mull, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.Count.Should().Be(2);
    }

    [Fact]
    public void Dies_GainLife_BindsAndFires()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var creature = new Creature("Sad Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(creature);

        var entity = new CardEntity
        {
            Name = "Sad Bear",
            OracleText = "When ~ dies, you gain 2 life.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(creature, entity))
        {
            creature.AddAbility(ab);
        }
        triggers.BindCard(creature);

        // Simulate death: move to graveyard via fake event.
        _bus.Publish(new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Graveyard));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(22);
    }

    [Fact]
    public void UnrecognisedText_NoBindings()
    {
        var creature = new Creature("Mystery", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice,
        };
        var entity = new CardEntity
        {
            Name = "Mystery",
            OracleText = "When ~ enters the battlefield, something weird happens.",
        };

        var bindings = OracleTriggeredAbilityBinder.Bind(creature, entity).ToList();

        // ETB pattern matches, but effect tail doesn't — produces no ability.
        bindings.Should().BeEmpty();
    }

    [Fact]
    public void Dies_DestroyTargetLand_BindsAndFires()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        // Fulminator Mage on Alice's battlefield.
        var fulm = new Creature("Fulminator Mage", "1BR", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(fulm);

        // Bob has a Mountain on his battlefield.
        var bobLand = new Majik.Core.Cards.Land("Mountain");
        bobLand.SetOwner(_bob);
        bobLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobLand);

        var allPlayers = new List<Player> { _alice, _bob };

        var entity = new CardEntity
        {
            Name = "Fulminator Mage",
            TypeLine = "Creature — Elemental Shaman",
            OracleText = "When Fulminator Mage dies, you may destroy target land.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(fulm, entity, _alice, allPlayers))
        {
            fulm.AddAbility(ab);
        }
        triggers.BindCard(fulm);

        // Simulate death — publish the event with card.Zone still at Battlefield
        // (matching the test-harness convention used by Dies_GainLife_BindsAndFires).
        _bus.Publish(new Majik.Core.Events.CardMovedEvent(fulm, ZoneType.Battlefield, ZoneType.Graveyard));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobLand,
            "the land should have been destroyed by Fulminator Mage's trigger");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobLand,
            "destroyed lands move to the graveyard (CR 701.7)");
    }

    [Fact]
    public void Bind_Endurance_EtbTargetsOpponent_GraveyardToLibraryBottom()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        // Put two cards in Bob's graveyard.
        var cardA = new Creature("Ragavan", "R", 2, 1) { Owner = _bob, Controller = _bob };
        var cardB = new Creature("Dragon's Rage Channeler", "R", 3, 3) { Owner = _bob, Controller = _bob };
        cardA.SetZone(ZoneType.Graveyard);
        cardB.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(cardA);
        _bob.Zones.Graveyard.AddCard(cardB);

        var allPlayers = new List<Player> { _alice, _bob };

        var endurance = new Creature("Endurance", "1GG", 3, 4) { Owner = _alice, Controller = _alice };
        endurance.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(endurance);

        var entity = new CardEntity
        {
            Name = "Endurance",
            TypeLine = "Creature — Elemental Incarnation",
            OracleText = "Flash\nReach\nWhen Endurance enters the battlefield, target player puts all the cards from their graveyard on the bottom of their library in a random order.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(endurance, entity, _alice, allPlayers))
        {
            endurance.AddAbility(ab);
        }
        triggers.BindCard(endurance);

        zones.MoveCardTo(endurance, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty(
            "all graveyard cards should have moved to Bob's library");
        _bob.Zones.Library.GetCards().Should().Contain(new[] { cardA, cardB },
            "graveyard cards go to the bottom of the target player's library");
    }

    [Fact]
    public void Bind_Endurance_Etb_FallsBackToController_WhenNoOpponents()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        // Card in Alice's own graveyard; allPlayers only contains Alice.
        var cardA = new Creature("Ragavan", "R", 2, 1) { Owner = _alice, Controller = _alice };
        cardA.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(cardA);

        var allPlayers = new List<Player> { _alice };

        var endurance = new Creature("Endurance", "1GG", 3, 4) { Owner = _alice, Controller = _alice };
        endurance.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(endurance);

        var entity = new CardEntity
        {
            Name = "Endurance",
            TypeLine = "Creature — Elemental Incarnation",
            OracleText = "Flash\nReach\nWhen Endurance enters the battlefield, target player puts all the cards from their graveyard on the bottom of their library in a random order.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(endurance, entity, _alice, allPlayers))
        {
            endurance.AddAbility(ab);
        }
        triggers.BindCard(endurance);

        zones.MoveCardTo(endurance, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "with no opponents, the controller's own graveyard is the fallback target");
        _alice.Zones.Library.GetCards().Should().Contain(cardA);
    }

    [Fact]
    public void GoblinGuide_Attack_RevealsTop_LandToDefenderHand()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        // Top of Bob's library is a land — should move to Bob's hand on attack.
        var topLand = new Majik.Core.Cards.Land("Mountain");
        topLand.SetOwner(_bob);
        topLand.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(topLand);

        var gg = new Creature("Goblin Guide", "R", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(gg);

        var entity = new CardEntity
        {
            Name = "Goblin Guide",
            TypeLine = "Creature — Goblin Scout",
            OracleText = "Haste\nWhenever Goblin Guide attacks, defending player reveals the top card of their library. If it's a land card, that player puts it into their hand.",
        };
        var bindings = OracleTriggeredAbilityBinder.Bind(gg, entity, _alice).ToList();
        bindings.Should().HaveCount(1, "exactly one attack trigger should bind");
        foreach (var ab in bindings)
        {
            gg.AddAbility(ab);
        }
        triggers.BindCard(gg);

        // Fire the per-attacker event with Bob as defender.
        _bus.Publish(new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(gg, _bob));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Hand.GetCards().Should().Contain(topLand,
            "top of defender's library was a land — moves to hand");
        _bob.Zones.Library.GetCards().Should().NotContain(topLand);
    }

    [Fact]
    public void GoblinGuide_Attack_NonLandTop_StaysInLibrary()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        // Top of Bob's library is a creature — stays in library.
        var topCreature = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = _bob, Controller = _bob, Zone = ZoneType.Library,
        };
        _bob.Zones.Library.AddCard(topCreature);

        var gg = new Creature("Goblin Guide", "R", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(gg);

        var entity = new CardEntity
        {
            Name = "Goblin Guide",
            TypeLine = "Creature — Goblin Scout",
            OracleText = "Haste\nWhenever Goblin Guide attacks, defending player reveals the top card of their library. If it's a land card, that player puts it into their hand.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(gg, entity, _alice))
        {
            gg.AddAbility(ab);
        }
        triggers.BindCard(gg);

        _bus.Publish(new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(gg, _bob));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Library.GetCards().Should().Contain(topCreature,
            "non-land top card stays in the library");
        _bob.Zones.Hand.GetCards().Should().NotContain(topCreature);
    }

    [Fact]
    public void Ragavan_CombatDamage_CreatesTreasure_AndExilesDefenderTop()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var topCard = new Creature("Llanowar Elves", "G", 1, 1)
        {
            Owner = _bob, Controller = _bob, Zone = ZoneType.Library,
        };
        _bob.Zones.Library.AddCard(topCard);

        var rag = new Creature("Ragavan", "R", 2, 1)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(rag);

        var entity = new CardEntity
        {
            Name = "Ragavan, Nimble Pilferer",
            TypeLine = "Legendary Creature — Monkey Pirate",
            OracleText = "Whenever Ragavan deals combat damage to a player, create a Treasure token and exile the top card of that player's library. Until end of turn, you may cast that card.\nDash {1}{R}",
        };
        var bindings = OracleTriggeredAbilityBinder.Bind(rag, entity, _alice).ToList();
        bindings.Should().HaveCount(1, "one combat-damage trigger should bind");
        foreach (var ab in bindings)
        {
            rag.AddAbility(ab);
        }
        triggers.BindCard(rag);

        // Fire combat damage to Bob.
        _bus.Publish(new Majik.Core.Domain.DomainEvents.CombatDamageDealtEvent(rag, _bob, 2));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Treasure token under Alice's control.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Majik.Core.Cards.Artifact>()
            .Should().Contain(c => c.Name == "Treasure",
                "controller gets a Treasure token");

        // Bob's library top exiled.
        _bob.Zones.Exile.GetCards().Should().Contain(topCard,
            "top of damaged player's library is exiled");
        _bob.Zones.Library.GetCards().Should().NotContain(topCard);
    }

    [Fact]
    public void Dies_DestroyTargetLand_NoOp_WhenNoOpponents()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var fulm = new Creature("Fulminator Mage", "1BR", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(fulm);

        // allPlayers only contains Alice — no opponents to destroy a land from.
        var allPlayers = new List<Player> { _alice };

        var entity = new CardEntity
        {
            Name = "Fulminator Mage",
            OracleText = "When Fulminator Mage dies, you may destroy target land.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(fulm, entity, _alice, allPlayers))
        {
            fulm.AddAbility(ab);
        }
        triggers.BindCard(fulm);

        // Should not throw — trigger fires but no opponent land is available.
        _bus.Publish(new Majik.Core.Events.CardMovedEvent(fulm, ZoneType.Battlefield, ZoneType.Graveyard));
        triggers.PutPendingTriggersOnStack(_alice);
        var trigger = stack.Pop();
        var act = () => trigger!.Resolve();
        act.Should().NotThrow();
    }

    // ---- ETB surveil on the DSK surveil-land cycle (Underground Mortuary etc.)

    [Fact]
    public void ETB_SurveilLand_ThisLandEnters_BindsAndFiresWithoutAgent()
    {
        // CR 701.42 — production load path for the DSK surveil-land cycle
        // (Underground Mortuary, Lush Portico, Meticulous Archive, Shadowy
        // Backstreet, Thundering Falls). Oracle text uses "When this land
        // enters, surveil 1." Pre-fix the EtbLine regex didn't accept
        // "this land", so the trigger never bound — production lands ETBed
        // without any surveil at all.
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        // Stack 2 cards on top so surveil 1 has something to peek.
        var top = new Majik.Core.Cards.Land("Forest") { Owner = _alice, Controller = _alice };
        var next = new Majik.Core.Cards.Land("Forest") { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(top);
        _alice.Zones.Library.AddCard(next);
        top.SetZone(ZoneType.Library);
        next.SetZone(ZoneType.Library);

        var land = new Majik.Core.Cards.Land("Underground Mortuary")
        {
            Owner = _alice, Controller = _alice,
        };
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);

        var entity = new CardEntity
        {
            Name = "Underground Mortuary",
            TypeLine = "Land — Swamp Forest",
            OracleText = "({T}: Add {B} or {G}.)\nThis land enters tapped.\n" +
                         "When this land enters, surveil 1.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(land, entity, _alice))
        {
            land.AddAbility(ab);
        }
        triggers.BindCard(land);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Default decision (no agent registered) sends all peeked cards to
        // graveyard. Underground Mortuary surveils 1 → top card goes to GY.
        _alice.Zones.Graveyard.GetCards().Should().Contain(top,
            because: "no agent was registered; surveil falls back to all-to-graveyard");
    }
}
