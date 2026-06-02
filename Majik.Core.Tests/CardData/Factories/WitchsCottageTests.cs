using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
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
/// Tests for <see cref="WitchsCottageFactory"/> — Land — Swamp (Throne of
/// Eldraine). Oracle:
///   "({T}: Add {B}.)
///    This land enters tapped unless you control three or more other Swamps.
///    When this land enters untapped, you may put target creature card from
///    your graveyard on top of your library."
///
/// Covers:
/// - Card identity (Land + Swamp subtype, nonbasic, non-legendary) + dispatch.
/// - {T}: Add {B} mana ability (exactly one ManaAbility producing black).
/// - Enters-untapped trigger shape: 1..1 creature target request.
/// - Enters-tapped gate (CR 614.1c): &lt;3 other Swamps → tapped; ≥3 → untapped.
/// - Untapped entry (≥3 Swamps) → trigger queued, chosen creature → top of library.
/// - Tapped entry (&lt;3 Swamps) → enters tapped, no enters-untapped trigger.
/// - No target supplied → effect no-ops cleanly.
/// </summary>
[Trait("Color", "C")]
public class WitchsCottageTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WitchsCottage_IsLand_WithSwampSubtype_NonBasic()
    {
        var land = WitchsCottageFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Swamp).Should().BeTrue("Witch's Cottage is a land with the Swamp subtype");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Witch's Cottage is not a basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Name.Should().Be("Witch's Cottage");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        // Exactly one black ManaAbility + one enters-untapped TriggeredAbility.
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
    // -----------------------------------------------------------------------
    // {T}: Add {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void WitchsCottage_ManaAbility_ProducesBlack()
    {
        var land = WitchsCottageFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1);
        manaAbilities[0].ManaGenerated.Black.Should().Be(1);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Enters-untapped trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WitchsCottage_EtbTrigger_HasCorrectTargetRequest()
    {
        var land = WitchsCottageFactory.Create(_alice);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
        req.Description.Should().Contain("graveyard");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Enters-tapped gate (CR 614.1c): 3+ other Swamps → untapped
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersUntapped_WithThreeOtherSwamps_TriggerFires_CreatureGoesToTopOfLibrary()
    {
        var (zones, _, triggers, replacements) = BuildEngine();

        // Pre-seed 3 Swamps on Alice's battlefield → gate satisfied.
        for (int i = 0; i < 3; i++)
            PlaceSwampOnBattlefield(_alice, $"Swamp {i}");

        var cottage = WitchsCottageFactory.Create(_alice, replacements, triggers);
        _alice.Zones.Hand.AddCard(cottage);
        cottage.SetZone(ZoneType.Hand);

        // A creature card in the graveyard as the recur target.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bear);

        // Library filler so we can verify the bear lands at index 0.
        var filler = new Instant("Filler", "");
        filler.SetOwner(_alice);
        _alice.Zones.Library.AddCard(filler);

        var etb = cottage.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        // Move Cottage → Battlefield. 3 other Swamps → replacement leaves it
        // untapped → CardMovedEvent fires with IsTapped == false → trigger queues.
        zones.MoveCardTo(cottage, ZoneType.Battlefield, controller: _alice);

        cottage.IsTapped.Should().BeFalse("3 other Swamps satisfies the gate — Witch's Cottage enters untapped");
        triggers.PendingCount.Should().Be(1, "entered untapped → the enters-untapped trigger should queue");

        foreach (var effect in etb.Effects)
            effect.Execute();

        bear.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Graveyard.ContainsCard(bear).Should().BeFalse();
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(bear,
            "the creature should sit at index 0 — top of the library — ahead of the filler");
    }

    // -----------------------------------------------------------------------
    // Enters-tapped gate (CR 614.1c): <3 other Swamps → tapped, no trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WithTwoOtherSwamps_NoUntappedTriggerQueued()
    {
        var (zones, _, triggers, replacements) = BuildEngine();

        // Only 2 Swamps — one short of the 3+ threshold.
        for (int i = 0; i < 2; i++)
            PlaceSwampOnBattlefield(_alice, $"Swamp {i}");

        var cottage = WitchsCottageFactory.Create(_alice, replacements, triggers);
        _alice.Zones.Hand.AddCard(cottage);
        cottage.SetZone(ZoneType.Hand);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bear);

        zones.MoveCardTo(cottage, ZoneType.Battlefield, controller: _alice);

        cottage.IsTapped.Should().BeTrue("only 2 other Swamps — the gate fails, Witch's Cottage enters tapped");
        triggers.PendingCount.Should().Be(0,
            "entered tapped → the enters-untapped trigger should NOT queue");
        bear.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.ContainsCard(bear).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // No target → effect no-ops cleanly
    // -----------------------------------------------------------------------

    [Fact]
    public void NoTargetChosen_EffectNoOps_Cleanly()
    {
        var land = WitchsCottageFactory.Create(_alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects)
                effect.Execute();
        };

        act.Should().NotThrow("a trigger with no chosen targets should no-op without exception");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PlaceSwampOnBattlefield(Player owner, string name)
    {
        var swamp = new Land(name, supertypes: null, subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(owner);
        swamp.SetController(owner);
        owner.Zones.Battlefield.AddCard(swamp);
        swamp.SetZone(ZoneType.Battlefield);
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
