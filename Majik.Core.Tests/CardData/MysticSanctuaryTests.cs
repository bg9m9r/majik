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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="MysticSanctuaryFactory"/> — Land — Island with an
/// ETB intervening-if trigger (CR 603.4) that recurs a target instant or
/// sorcery card from the controller's graveyard to the top of their library.
///
/// Covers:
/// - Card identity (Land + Island subtype, nonbasic, non-legendary).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {U} mana ability (exactly one ManaAbility producing blue).
/// - ETB trigger shape: 1..1 target request; intervening-if checks 3+ other Islands.
/// - ETB with 3 other Islands → trigger queued (1 pending trigger), chosen
///   instant placed on top of library.
/// - ETB with 2 other Islands → intervening-if fails, trigger not queued,
///   card stays in graveyard.
/// - No target supplied → effect no-ops cleanly (card in graveyard stays put).
/// </summary>
public class MysticSanctuaryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MysticSanctuary_IsLand_WithIslandSubtype_NonBasic()
    {
        var land = MysticSanctuaryFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Island).Should().BeTrue("Mystic Sanctuary is a land with the Island subtype");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Mystic Sanctuary is not a basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Name.Should().Be("Mystic Sanctuary");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        // Exactly one blue ManaAbility + one ETB TriggeredAbility.
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MysticSanctuary()
    {
        var card = NamedCardFactory.Create("Mystic Sanctuary", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Mystic Sanctuary");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSubtype(CardSubtype.Island).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {U}
    // -----------------------------------------------------------------------

    [Fact]
    public void MysticSanctuary_ManaAbility_ProducesBlue()
    {
        var land = MysticSanctuaryFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(1);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape (intervening-if structure)
    // -----------------------------------------------------------------------

    [Fact]
    public void MysticSanctuary_EtbTrigger_HasCorrectTargetRequest()
    {
        var land = MysticSanctuaryFactory.Create(_alice);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("instant or sorcery");
        req.Description.Should().Contain("graveyard");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void MysticSanctuary_InterveningIf_TrueWithThreeOtherIslands_FalseWithTwo()
    {
        // Place Mystic Sanctuary on the battlefield so CountOtherIslands sees it
        // (but excludes it via reference equality).
        var land = MysticSanctuaryFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        // 3 OTHER Islands → condition satisfied.
        AddIsland("Island A");
        AddIsland("Island B");
        AddIsland("Island C");

        etb.CanBePutOnStack().Should().BeTrue(
            "3 other Islands + Mystic Sanctuary = 4 Islands total; intervening-if (3+ other) satisfied");

        // Remove one → 2 OTHER Islands → condition fails.
        var doomed = _alice.Zones.Battlefield.GetCards().First(c => c.Name == "Island C");
        _alice.Zones.Battlefield.RemoveCard(doomed);

        etb.CanBePutOnStack().Should().BeFalse(
            "only 2 other Islands; the 3+ other Islands threshold is no longer met");
    }

    // -----------------------------------------------------------------------
    // Test 1: ETB with 3 other Islands → trigger queued, instant goes to top
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbWithThreeOtherIslands_TriggerFires_InstantGoesToTopOfLibrary()
    {
        var (zones, _, triggers) = BuildEngine();

        // Pre-seed 3 Islands on Alice's battlefield.
        for (int i = 0; i < 3; i++)
            PlaceIslandOnBattlefield(_alice, $"Island {i}");

        var sanctuary = MysticSanctuaryFactory.Create(_alice, triggers);
        _alice.Zones.Hand.AddCard(sanctuary);
        sanctuary.SetZone(ZoneType.Hand);

        // Place a Lightning Bolt (Instant) in the graveyard as the recur target.
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        // Pre-seed library with a filler so we can verify bolt ends up at index 0.
        var filler = new Instant("Filler", "");
        filler.SetOwner(_alice);
        _alice.Zones.Library.AddCard(filler);

        // Wire the chosen target before firing the ETB.
        var etb = sanctuary.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bolt } });

        // Move Sanctuary → Battlefield; ZoneService publishes CardMovedEvent
        // which the TriggerManager subscribes to. InterveningIf: 3 others ≥ 3 → true.
        zones.MoveCardTo(sanctuary, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "3 other Islands satisfies the intervening-if; ETB trigger should queue");

        // Resolve the effect manually (mirrors Murktide / Valakut test posture).
        foreach (var effect in etb.Effects)
            effect.Execute();

        bolt.Zone.Should().Be(ZoneType.Library,
            "bolt should have been moved from graveyard to library");
        _alice.Zones.Graveyard.ContainsCard(bolt).Should().BeFalse();
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(bolt,
            "bolt should sit at index 0 — top of the library — ahead of the filler");
    }

    // -----------------------------------------------------------------------
    // Test 2: ETB with 2 other Islands → intervening-if fails, trigger not queued
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbWithTwoOtherIslands_InterveningIfFails_TriggerNotQueued()
    {
        var (zones, _, triggers) = BuildEngine();

        // Only 2 Islands — one short of the 3+ threshold.
        for (int i = 0; i < 2; i++)
            PlaceIslandOnBattlefield(_alice, $"Island {i}");

        var sanctuary = MysticSanctuaryFactory.Create(_alice, triggers);
        _alice.Zones.Hand.AddCard(sanctuary);
        sanctuary.SetZone(ZoneType.Hand);

        // Place a target spell in the graveyard.
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        zones.MoveCardTo(sanctuary, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0,
            "only 2 other Islands; the intervening-if (3+ other Islands) is false, trigger should not queue");

        // Verify the card stayed in the graveyard (trigger never fired).
        bolt.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.ContainsCard(bolt).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Test 3: No target in graveyard → effect no-ops cleanly
    // -----------------------------------------------------------------------

    [Fact]
    public void NoTargetChosen_EffectNoOps_Cleanly()
    {
        // Directly exercise the effect body with no chosen targets.
        var land = MysticSanctuaryFactory.Create(_alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        // No call to SetChosenTargets → ChosenTargets is empty.
        var act = () =>
        {
            foreach (var effect in etb.Effects)
                effect.Execute();
        };

        act.Should().NotThrow("a trigger with no chosen targets should no-op without exception");
        _alice.Zones.Library.GetCards().Should().BeEmpty(
            "with no target the library should remain empty");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "nothing should have been moved from the graveyard");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void AddIsland(string name)
    {
        var island = new Land(name, supertypes: null, subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);
        island.SetZone(ZoneType.Battlefield);
    }

    private static void PlaceIslandOnBattlefield(Player owner, string name)
    {
        var island = new Land(name, supertypes: null, subtypes: new[] { CardSubtype.Island });
        island.SetOwner(owner);
        island.SetController(owner);
        owner.Zones.Battlefield.AddCard(island);
        island.SetZone(ZoneType.Battlefield);
    }

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
