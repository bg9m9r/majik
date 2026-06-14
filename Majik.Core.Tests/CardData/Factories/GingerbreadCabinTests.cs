using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="GingerbreadCabinFactory"/> — Land — Forest (Throne of
/// Eldraine). Oracle:
///   "({T}: Add {G}.)
///    This land enters tapped unless you control three or more other Forests.
///    When this land enters untapped, create a Food token."
///
/// Covers the card's UNIQUE behaviour:
/// - Card identity (Land + Forest subtype, nonbasic, non-legendary).
/// - {T}: Add {G} mana ability + exactly one enters-untapped trigger.
/// - Enters-tapped gate (CR 614.1c): &lt;3 other Forests → tapped; ≥3 → untapped.
/// - Untapped entry (≥3 Forests) → trigger queued, resolution → Food token.
/// - Tapped entry (&lt;3 Forests) → enters tapped, no enters-untapped trigger.
/// </summary>
[Trait("Color", "C")]
public class GingerbreadCabinTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GingerbreadCabin_IsLand_WithForestSubtype_NonBasic()
    {
        var land = GingerbreadCabinFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Forest).Should().BeTrue("Gingerbread Cabin is a land with the Forest subtype");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Gingerbread Cabin is not a basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Name.Should().Be("Gingerbread Cabin");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        // Exactly one green ManaAbility + one enters-untapped TriggeredAbility.
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {G}
    // -----------------------------------------------------------------------

    [Fact]
    public void GingerbreadCabin_ManaAbility_ProducesGreen()
    {
        var land = GingerbreadCabinFactory.Create(_alice);

        var mana = land.Abilities.OfType<ManaAbility>().Single();
        mana.ManaGenerated.Green.Should().Be(1);
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.Blue.Should().Be(0);
        mana.ManaGenerated.Black.Should().Be(0);
        mana.ManaGenerated.Red.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Enters-untapped trigger shape — no target (vs Witch's Cottage's recur)
    // -----------------------------------------------------------------------

    [Fact]
    public void GingerbreadCabin_EtbTrigger_HasNoTargetRequest()
    {
        var land = GingerbreadCabinFactory.Create(_alice);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().BeEmpty("creating a Food token is not a targeted effect");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Enters-tapped gate (CR 614.1c): 3+ other Forests → untapped → Food
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersUntapped_WithThreeOtherForests_TriggerFires_CreatesFoodToken()
    {
        var (zones, _, triggers, replacements) = BuildEngine();

        // Pre-seed 3 Forests on Alice's battlefield → gate satisfied.
        for (int i = 0; i < 3; i++)
            PlaceForestOnBattlefield(_alice, $"Forest {i}");

        var cabin = GingerbreadCabinFactory.Create(_alice, replacements, triggers, zones);
        _alice.Zones.Hand.AddCard(cabin);
        cabin.SetZone(ZoneType.Hand);

        int foodBefore = CountFood(_alice);

        // Move Cabin → Battlefield. 3 other Forests → replacement leaves it
        // untapped → CardMovedEvent fires with IsTapped == false → trigger queues.
        zones.MoveCardTo(cabin, ZoneType.Battlefield, controller: _alice);

        cabin.IsTapped.Should().BeFalse("3 other Forests satisfies the gate — Gingerbread Cabin enters untapped");
        triggers.PendingCount.Should().Be(1, "entered untapped → the enters-untapped trigger should queue");

        var etb = cabin.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects)
            effect.Execute();

        CountFood(_alice).Should().Be(foodBefore + 1, "the enters-untapped trigger creates exactly one Food token");
    }

    // -----------------------------------------------------------------------
    // Enters-tapped gate (CR 614.1c): <3 other Forests → tapped, no trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WithTwoOtherForests_NoUntappedTriggerQueued_NoFood()
    {
        var (zones, _, triggers, replacements) = BuildEngine();

        // Only 2 Forests — one short of the 3+ threshold.
        for (int i = 0; i < 2; i++)
            PlaceForestOnBattlefield(_alice, $"Forest {i}");

        var cabin = GingerbreadCabinFactory.Create(_alice, replacements, triggers, zones);
        _alice.Zones.Hand.AddCard(cabin);
        cabin.SetZone(ZoneType.Hand);

        int foodBefore = CountFood(_alice);

        zones.MoveCardTo(cabin, ZoneType.Battlefield, controller: _alice);

        cabin.IsTapped.Should().BeTrue("only 2 other Forests — the gate fails, Gingerbread Cabin enters tapped");
        triggers.PendingCount.Should().Be(0,
            "entered tapped → the enters-untapped trigger should NOT queue");
        CountFood(_alice).Should().Be(foodBefore, "no untapped trigger → no Food token");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static int CountFood(Player player) =>
        player.Zones.Battlefield.GetCards().Count(c => c.HasSubtype(CardSubtype.Food));

    private static void PlaceForestOnBattlefield(Player owner, string name)
    {
        var forest = new Land(name, supertypes: null, subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(owner);
        forest.SetController(owner);
        owner.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);
    }

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers, ReplacementBus replacements) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, rep);
    }
}
