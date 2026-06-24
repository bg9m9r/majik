using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CarrotCakeFactory"/>.
///
/// Carrot Cake (Bloomburrow Commander, {1}{W}). Artifact — Food. Oracle text:
///   "When this artifact enters and when you sacrifice it, create a 1/1 white
///    Rabbit creature token and scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {2}, {T}, Sacrifice this artifact: You gain 3 life."
///
/// Covers ONLY the card's unique behaviour:
/// - Identity ({1}{W}, Artifact — Food, white, two triggered + one activated ability).
/// - ETB trigger: entering the battlefield creates a 1/1 white Rabbit token
///   and scries 1 (CR 603.6a).
/// - Sacrifice trigger: sacrificing the Cake via its own gain-3-life ability
///   ALSO creates a 1/1 white Rabbit token, and the controller gains 3 life
///   (CR 603.1 + CR 602.1).
/// </summary>
[Trait("Color", "W")]
public class CarrotCakeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CarrotCake_Identity()
    {
        var c = CarrotCakeFactory.Create(_alice);

        c.Name.Should().Be("Carrot Cake");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Food).Should().BeTrue();
        CardColors.GetColors(c).Should()
            .BeEquivalentTo(new[] { Majik.Core.ValueObjects.ManaColor.White });
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // Two triggered abilities ("When this enters" + "when you sacrifice it")
        // plus exactly one activated ability ({2},{T},Sac: gain 3 life).
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EntersBattlefield_CreatesRabbitToken()
    {
        var (zones, stack, triggers, _) = BuildEngine();
        SeedLibrary(_alice, "Plains", "Island");

        var cake = CarrotCakeFactory.Create(_alice, zones, triggers);
        cake.SetOwner(_alice);
        cake.SetController(_alice);
        _alice.Zones.Hand.AddCard(cake);
        cake.SetZone(ZoneType.Hand);

        // ETB move publishes the CardMovedEvent the auto-registered enter
        // trigger fires on (CR 603.6a).
        zones.MoveCardTo(cake, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1, "the enter trigger should be queued");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        AssertExactlyOneRabbitToken(_alice);
    }

    [Fact]
    public void SacrificeForLife_GainsLifeAndFiresSacTrigger()
    {
        var (zones, stack, triggers, bus) = BuildEngine();
        SeedLibrary(_alice, "Plains");

        var cake = CarrotCakeFactory.Create(_alice, zones, triggers);
        cake.SetOwner(_alice);
        cake.SetController(_alice);
        // Place directly on the battlefield WITHOUT routing through ZoneService
        // so the enter trigger does not pre-fire (isolates the sac trigger).
        cake.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cake);
        triggers.BindCard(cake); // register its triggers without an ETB move

        var ability = cake.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = ability.Costs.OfType<SacrificeSelfCost>().Single();
        var lifeBefore = _alice.LifeTotal;

        // Pay the Sacrifice cost via the bus-aware path so the
        // PermanentSacrificedEvent publishes and the "when you sacrifice it"
        // trigger fires (CR 701.16 / CR 603.1).
        sacCost.Pay(_alice, bus);

        // Resolve the gain-3-life effect body of the ability (self-contained).
        foreach (var fx in ability.Effects)
        {
            fx.Execute();
        }

        _alice.LifeTotal.Should().Be(lifeBefore + 3, "{2},{T},Sac: You gain 3 life.");

        triggers.PendingCount.Should().Be(1, "the sacrifice trigger should be queued");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        AssertExactlyOneRabbitToken(_alice);
        _alice.Zones.Graveyard.GetCards().Should().Contain(cake);
    }

    private static void AssertExactlyOneRabbitToken(Player player)
    {
        var rabbits = player.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Rabbit))
            .ToList();
        rabbits.Should().HaveCount(1, "create a 1/1 white Rabbit creature token");
        rabbits[0].BasePower.Should().Be(1);
        rabbits[0].BaseToughness.Should().Be(1);
        CardColors.GetColors(rabbits[0]).Should()
            .BeEquivalentTo(new[] { Majik.Core.ValueObjects.ManaColor.White });
    }

    private static void SeedLibrary(Player player, params string[] names)
    {
        foreach (var n in names)
        {
            var c = new Land(n, new[] { CardSupertype.Basic }, Array.Empty<CardSubtype>());
            c.SetOwner(player);
            player.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    private static void ResolveAll(Majik.Core.Stack.Stack stack)
    {
        while (stack.Count > 0)
        {
            stack.Pop()!.Resolve();
        }
    }

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers, IEventBus bus) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, bus);
    }
}
