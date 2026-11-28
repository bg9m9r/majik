using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Tests.Game;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// CR 605 — mana abilities produce mana the same way whether a player
/// activates one (ManaAbilityActivator) or a payment taps the source
/// (ManaPaymentResolver). Both now go through ManaAbilityActivator.Produce:
/// the mana is added with its spend restriction and provenance (CR 106.4)
/// and a ManaAbilityActivatedEvent is published, so "whenever ... is tapped
/// for mana" triggers (Manabarbs, Overgrowth, Mirari's Wake) see it.
/// </summary>
public class ManaProductionFunnelTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Land Mountain(Player owner)
    {
        var land = (Land)NamedCardFactory.Create("Mountain", owner);
        land.SetController(owner);
        land.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(land);
        return land;
    }

    [Fact]
    public async Task ManabarbsTriggers_WhenALiveCastPaysByTappingAMountain()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var replacements = new Majik.Core.Effects.ReplacementBus();
        var zones = new ZoneService(bus, replacements);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var players = new List<Player> { _alice, _bob };
        var priority = new PriorityManager(players, stack, bus, triggers);

        var barbs = ManabarbsFactory.Create(_alice, triggers);
        barbs.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(barbs);
        var mountain = Mountain(_alice);
        var goblin = new Creature("Goblin", "{R}", 1, 1) { Owner = _alice, Controller = _alice };
        goblin.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(goblin);
        foreach (var p in players)
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Mountain", p);
                c.SetZone(ZoneType.Library);
                p.Zones.Library.AddCard(c);
            }

        var manaEvents = new List<ManaAbilityActivatedEvent>();
        bus.Subscribe<ManaAbilityActivatedEvent>(manaEvents.Add);

        var inner = new ScriptedAgent();
        inner.QueueMana(new ManaPayment(new ICard[] { mountain }));
        var aliceAgent = new MainPhaseCastAgent(inner, goblin, _alice);
        AgentRegistry.Set(_alice, aliceAgent);
        var bobAgent = new DeterministicBotAgent();
        AgentRegistry.Set(_bob, bobAgent);

        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent> { [_alice] = aliceAgent, [_bob] = bobAgent },
            stack, zones, triggers, resolver, sba, priority,
            new Majik.Core.Combat.CombatFlow(bus, sba),
            continuousEffects: null,
            replacements: replacements,
            landDropTracker: new LandDropTracker(),
            eventBus: bus);

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        goblin.Zone.Should().Be(ZoneType.Battlefield, "the cast was paid by tapping the Mountain");
        mountain.IsTapped.Should().BeTrue();
        manaEvents.Should().ContainSingle(e => ReferenceEquals(e.Source, mountain))
            .Which.ManaGenerated.Red.Should().Be(1, "the event carries the mana produced");
        _alice.LifeTotal.Should().Be(19, "Manabarbs: whenever a player taps a land for mana, it deals 1 damage to them");
    }

    [Fact]
    public void APaymentThatTapsSources_PublishesOneEventPerSource_InPaymentOrder()
    {
        var bus = new EventBus();
        var first = Mountain(_alice);
        var second = Mountain(_alice);
        var events = new List<ManaAbilityActivatedEvent>();
        bus.Subscribe<ManaAbilityActivatedEvent>(events.Add);

        new ManaPaymentResolver(eventBus: bus)
            .Pay(_alice, ManaCost.Parse("{R}{R}"), new ManaPayment(new ICard[] { first, second }))
            .Should().BeTrue();

        events.Select(e => e.Source).Should().Equal(first, second);
        events.Should().OnlyContain(e => ReferenceEquals(e.Player, _alice) && e.ManaGenerated.Red == 1);
        _alice.ManaPool.Red.Should().Be(0, "the mana paid the cost");
    }

    [Fact]
    public void ManuallyActivatedRestrictedMana_KeepsItsSpendRestriction()
    {
        // Ancient Ziggurat: "Add one mana of any color. Spend this mana only to
        // cast a creature spell." (CR 106.4.) Activated by hand and left
        // floating, it must not pay for an instant.
        var ziggurat = (Land)NamedCardFactory.Create("Ancient Ziggurat", _alice);
        ziggurat.SetController(_alice);
        ziggurat.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ziggurat);
        var red = ziggurat.Abilities.OfType<IManaAbility>().Single(a => a.ManaGenerated.Red == 1);

        new ManaAbilityActivator().ActivateManaAbility(red, _alice);

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Controller = _alice };
        new ManaPaymentResolver()
            .Pay(_alice, ManaCost.Parse("{R}"), ManaPayment.Empty, spentOn: bolt, spendAsAnyColor: false, out _, out _)
            .Should().BeFalse("Ziggurat mana can only be spent on a creature spell");
        var goblin = new Creature("Goblin", "{R}", 1, 1) { Owner = _alice, Controller = _alice };
        new ManaPaymentResolver()
            .Pay(_alice, ManaCost.Parse("{R}"), ManaPayment.Empty, spentOn: goblin, spendAsAnyColor: false, out _, out _)
            .Should().BeTrue();
    }
}
