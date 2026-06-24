using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RampagingBalothsFactory"/> (Zendikar, {4}{G}{G}).
///
/// Rampaging Baloths — Creature — Beast 6/6. Oracle text (verified against
/// Scryfall):
///   "Trample
///    Landfall — Whenever a land you control enters, create a 4/4 green
///    Beast creature token."
///
/// Same landfall trigger plumbing as <see cref="ScuteSwarmFactory"/>
/// (<see cref="Triggers.OnLandEntersUnderControl"/>, CR 603.6a). The unique
/// behaviour exercised here is the resolve body's token mint — one 4/4 green
/// Beast token (CR 111 / CR 111.4) on every controller land drop — plus the
/// printed Trample keyword on the body.
///
/// Coverage:
/// - Identity (Creature — Beast, 6/6, {4}{G}{G}, green, Trample, owner/controller).
/// - Landfall trigger attached, self-affecting (no targets).
/// - Controller's land ETB mints one 4/4 green Beast token.
/// - Opponent's land ETB does NOT fire (CR 603.6a — "a land you control").
/// </summary>
[Trait("Color", "G")]
public class RampagingBalothsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void RampagingBaloths_Identity_CreatureBeast_6_6_Green4GG_Trample()
    {
        var baloths = RampagingBalothsFactory.Create(_alice);

        baloths.Name.Should().Be("Rampaging Baloths");
        baloths.HasType(CardType.Creature).Should().BeTrue();
        baloths.ManaCost.Should().Be("{4}{G}{G}");
        baloths.ManaCostValue.TotalValue.Should().Be(6);
        CardColors.GetColors(baloths).Should().Contain(ManaColor.Green);
        baloths.Power.Should().Be(6);
        baloths.Toughness.Should().Be(6);
        baloths.Subtypes.Should().Contain(CardSubtype.Beast);
        // CR 702.19 — Trample present as a KeywordAbility marker, read by combat.
        baloths.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Trample");
        CombatAbilities.HasTrample(baloths).Should().BeTrue();
        baloths.Owner.Should().BeSameAs(_alice);
        baloths.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RampagingBaloths_LandfallTrigger_IsSelfAffecting_NoTargets()
    {
        var baloths = RampagingBalothsFactory.Create(_alice);

        var trigger = baloths.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(baloths);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "the landfall token-mint names no target");
    }

    [Fact]
    public void RampagingBaloths_OwnersLandEnters_MintsOneBeastToken()
    {
        var (zones, stack, triggers) = BuildEngine();

        var baloths = RampagingBalothsFactory.Create(_alice, zones, triggers);
        PlaceOnBattlefield(baloths, _alice);
        triggers.BindCard(baloths);

        DropLand(_alice, "Forest", zones);

        triggers.PendingCount.Should().Be(1,
            "landfall trigger queues on a land entering under controller's control");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var tokens = TokenCreatures(_alice).ToList();
        tokens.Should().ContainSingle("each landfall mints a single 4/4 green Beast token");
        var beast = tokens[0];
        beast.Name.Should().Be(RampagingBalothsFactory.BeastTokenName);
        beast.Power.Should().Be(RampagingBalothsFactory.TokenPower);
        beast.Toughness.Should().Be(RampagingBalothsFactory.TokenToughness);
        beast.Subtypes.Should().Contain(CardSubtype.Beast);
        CardColors.GetColors(beast).Should().Contain(ManaColor.Green);
        beast.IsToken.Should().BeTrue();
        // A vanilla Beast token is NOT a Rampaging Baloths — no landfall trigger.
        beast.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Fact]
    public void RampagingBaloths_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var baloths = RampagingBalothsFactory.Create(_alice, zones, triggers);
        PlaceOnBattlefield(baloths, _alice);
        triggers.BindCard(baloths);

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
