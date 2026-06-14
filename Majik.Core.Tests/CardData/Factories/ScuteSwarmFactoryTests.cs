using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ScuteSwarmFactory"/> (Zendikar Rising, {1}{G}).
///
/// Scute Swarm — Creature — Insect 1/1. Oracle text (verified against
/// Scryfall):
///   "Landfall — Whenever a land you control enters, create a 1/1 green
///    Insect creature token. If you control six or more lands, create a
///    token that's a copy of this creature instead."
///
/// Same landfall trigger plumbing as <see cref="PlatedGeopedeFactory"/>
/// (<see cref="Triggers.OnLandEntersUnderControl"/>, CR 603.6a). The unique
/// behaviour exercised here is the resolve body's token mint: a vanilla 1/1
/// green Insect below six lands, a self-copy token (CR 706.2) at six-or-more
/// lands, and the intervening-if count read at resolution (CR 603.4).
///
/// Coverage:
/// - Identity (Creature — Insect, 1/1, {1}{G}, green, owner/controller).
/// - Landfall trigger attached, self-affecting (no targets).
/// - Controller's land ETB below 6 lands mints one 1/1 green Insect token.
/// - At 6+ lands, mints a Scute Swarm copy token (a token whose own landfall
///   trigger then fires on subsequent land drops — the snowball).
/// - Opponent's land ETB does NOT fire (CR 603.6a — "a land you control").
/// </summary>
[Trait("Color", "G")]
public class ScuteSwarmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ScuteSwarm_Identity_CreatureInsect_1_1_Green1G()
    {
        var swarm = ScuteSwarmFactory.Create(_alice);

        swarm.Name.Should().Be("Scute Swarm");
        swarm.HasType(CardType.Creature).Should().BeTrue();
        swarm.ManaCost.Should().Be("{1}{G}");
        swarm.ManaCostValue.TotalValue.Should().Be(2);
        CardColors.GetColors(swarm).Should().Contain(ManaColor.Green);
        swarm.Power.Should().Be(1);
        swarm.Toughness.Should().Be(1);
        swarm.Subtypes.Should().Contain(CardSubtype.Insect);
        swarm.Owner.Should().BeSameAs(_alice);
        swarm.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ScuteSwarm_LandfallTrigger_IsSelfAffecting_NoTargets()
    {
        var swarm = ScuteSwarmFactory.Create(_alice);

        var trigger = swarm.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(swarm);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "the landfall token-mint names no target");
    }

    // -----------------------------------------------------------------------
    // Landfall below six lands — mint one 1/1 green Insect token
    // -----------------------------------------------------------------------

    [Fact]
    public void ScuteSwarm_LandfallBelowSixLands_MintsOneInsectToken()
    {
        var (zones, stack, triggers) = BuildEngine();

        var swarm = ScuteSwarmFactory.Create(_alice, zones, triggers);
        PlaceOnBattlefield(swarm, _alice);
        triggers.BindCard(swarm);

        // Drop a single land (controller now controls 1 land, < 6).
        DropLand(_alice, "Forest", zones);

        triggers.PendingCount.Should().Be(1,
            "landfall trigger queues on a land entering under controller's control");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var tokens = TokenCreatures(_alice).ToList();
        tokens.Should().ContainSingle("below six lands a single 1/1 green Insect token is created");
        var insect = tokens[0];
        insect.Name.Should().Be(ScuteSwarmFactory.InsectTokenName);
        insect.Power.Should().Be(1);
        insect.Toughness.Should().Be(1);
        insect.Subtypes.Should().Contain(CardSubtype.Insect);
        CardColors.GetColors(insect).Should().Contain(ManaColor.Green);
        insect.IsToken.Should().BeTrue();
        // A vanilla Insect token is NOT a Scute Swarm — no landfall trigger.
        insect.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Landfall at six+ lands — mint a Scute Swarm copy token that snowballs
    // -----------------------------------------------------------------------

    [Fact]
    public void ScuteSwarm_LandfallAtSixLands_MintsScuteSwarmCopyToken_ThatSnowballs()
    {
        var (zones, stack, triggers) = BuildEngine();

        var swarm = ScuteSwarmFactory.Create(_alice, zones, triggers);
        PlaceOnBattlefield(swarm, _alice);
        triggers.BindCard(swarm);

        // Put five lands directly on the battlefield, then drop the sixth so
        // the landfall trigger sees six lands at resolution (CR 603.4).
        for (int i = 0; i < 5; i++)
        {
            var land = new Land("Forest");
            land.SetOwner(_alice);
            PlaceOnBattlefield(land, _alice);
        }
        DropLand(_alice, "Forest", zones); // sixth land, fires landfall.

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 706.2 — at six lands the token is a COPY of Scute Swarm.
        var tokens = TokenCreatures(_alice).ToList();
        tokens.Should().ContainSingle();
        var copy = tokens[0];
        copy.Name.Should().Be(ScuteSwarmFactory.CardName);
        copy.IsToken.Should().BeTrue();
        copy.Subtypes.Should().Contain(CardSubtype.Insect);
        CardColors.GetColors(copy).Should().Contain(ManaColor.Green);

        // The copy is itself a Scute Swarm: it carries its OWN landfall
        // trigger and snowballs on the next land drop.
        copy.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "a Scute Swarm copy token carries the landfall trigger");

        // Drop a seventh land: BOTH the original and the copy should trigger.
        DropLand(_alice, "Forest", zones);
        triggers.PendingCount.Should().Be(2,
            "both the original Scute Swarm and its copy token trigger on the next landfall");
    }

    [Fact]
    public void ScuteSwarm_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var swarm = ScuteSwarmFactory.Create(_alice, zones, triggers);
        PlaceOnBattlefield(swarm, _alice);
        triggers.BindCard(swarm);

        DropLand(_bob, "Swamp", zones);

        triggers.PendingCount.Should().Be(0,
            "landfall only triggers on a land entering under YOUR control");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PlaceOnBattlefield(Permanent card, Player controller)
    {
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static void DropLand(Player controller, string name, ZoneService zones)
    {
        var land = new Land(name);
        land.SetOwner(controller);
        land.SetZone(ZoneType.Hand);
        controller.Zones.Hand.AddCard(land);
        zones.MoveCardTo(land, ZoneType.Battlefield, controller);
    }

    private static System.Collections.Generic.IEnumerable<Creature> TokenCreatures(Player p) =>
        p.Zones.Battlefield.GetCards().OfType<Creature>().Where(c => c.IsToken);

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
