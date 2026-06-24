using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Tinybones Joins Up (Final Fantasy Commander, {B}, Legendary
/// Enchantment).
///
/// Oracle text (Scryfall, verified 2026-06-24):
///   "When Tinybones Joins Up enters, any number of target players each
///    discard a card.
///    Whenever a legendary creature you control enters, any number of target
///    players each mill a card and lose 1 life."
///
/// Coverage of the UNIQUE behaviour (the contract test covers dispatch /
/// well-formedness):
/// - Identity: Legendary Enchantment, {B}, black, owner/controller wired.
/// - Both printed triggered abilities are present, each declaring a 0..many
///   "any number of target players" request.
/// - ETB trigger: each chosen player discards one card (empty hand → no-op).
/// - Legendary-creature trigger: each chosen player mills one card and loses
///   1 life.
/// - Empty target set is a clean no-op (CR 601.2c — "any number" includes 0).
/// - Two chosen players are each affected (variable target count).
/// </summary>
[Trait("Color", "B")]
public class TinybonesJoinsUpFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    [Fact]
    public void Create_HasLegendaryEnchantmentShape_Black_AtCostB()
    {
        var card = TinybonesJoinsUpFactory.Create(_alice);

        card.Name.Should().Be("Tinybones Joins Up");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
    }

    // ------------------------------------------------------------------
    // Ability shape
    // ------------------------------------------------------------------

    [Fact]
    public void Create_DeclaresTwoTriggers_EachWithAnyNumberOfTargetPlayers()
    {
        var card = TinybonesJoinsUpFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2);

        foreach (var t in triggers)
        {
            t.TargetRequests.Should().HaveCount(1);
            t.TargetRequests[0].Description.Should().ContainEquivalentOf("player");
            t.TargetRequests[0].MinTargets.Should().Be(0);
            t.TargetRequests[0].MaxTargets.Should().Be(int.MaxValue);
        }
    }

    // ------------------------------------------------------------------
    // ETB trigger — "each discard a card"
    // ------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_ChosenPlayersEachDiscardOneCard()
    {
        FillHand(_alice, 2);
        FillHand(_bob, 2);

        var card = TinybonesJoinsUpFactory.Create(_alice);
        var etb = EtbDiscardTrigger(card);

        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _alice, _bob } });
        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Hand.Count.Should().Be(1);
        _alice.Zones.Graveyard.Count.Should().Be(1);
        _bob.Zones.Hand.Count.Should().Be(1);
        _bob.Zones.Graveyard.Count.Should().Be(1);
    }

    [Fact]
    public void EtbTrigger_EmptyTargetSet_IsCleanNoOp()
    {
        FillHand(_bob, 2);

        var card = TinybonesJoinsUpFactory.Create(_alice);
        var etb = EtbDiscardTrigger(card);

        etb.SetChosenTargets(new IReadOnlyList<object>[] { Array.Empty<object>() });
        var act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
        _bob.Zones.Hand.Count.Should().Be(2);
        _bob.Zones.Graveyard.Count.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Legendary-creature trigger — "each mill a card and lose 1 life"
    // ------------------------------------------------------------------

    [Fact]
    public void LegendaryTrigger_ChosenPlayersEachMillOneAndLoseOneLife()
    {
        FillLibrary(_alice, 3);
        FillLibrary(_bob, 3);

        var card = TinybonesJoinsUpFactory.Create(_alice);
        var legend = LegendaryMillTrigger(card);

        legend.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _alice, _bob } });
        foreach (var e in legend.Effects) e.Execute();

        _alice.Zones.Library.Count.Should().Be(2);
        _alice.Zones.Graveyard.Count.Should().Be(1);
        _alice.LifeTotal.Should().Be(19);

        _bob.Zones.Library.Count.Should().Be(2);
        _bob.Zones.Graveyard.Count.Should().Be(1);
        _bob.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void LegendaryTrigger_EmptyTargetSet_IsCleanNoOp()
    {
        FillLibrary(_bob, 3);

        var card = TinybonesJoinsUpFactory.Create(_alice);
        var legend = LegendaryMillTrigger(card);

        legend.SetChosenTargets(new IReadOnlyList<object>[] { Array.Empty<object>() });
        var act = () => { foreach (var e in legend.Effects) e.Execute(); };

        act.Should().NotThrow();
        _bob.Zones.Library.Count.Should().Be(3);
        _bob.LifeTotal.Should().Be(20);
    }

    // ------------------------------------------------------------------
    // Trigger condition: only legendary creatures the controller controls fire
    // ------------------------------------------------------------------

    [Fact]
    public void LegendaryTrigger_Condition_FiresOnlyForControllerLegendaryCreatures()
    {
        var card = TinybonesJoinsUpFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        var legend = LegendaryMillTrigger(card);

        var myLegend = new Creature("Hero", "{1}{B}", 2, 2,
            supertypes: new[] { CardSupertype.Legendary });
        myLegend.SetOwner(_alice);
        myLegend.SetController(_alice);

        var myNonLegend = new Creature("Grunt", "{1}{B}", 2, 2);
        myNonLegend.SetOwner(_alice);
        myNonLegend.SetController(_alice);

        var oppLegend = new Creature("Rival", "{1}{B}", 2, 2,
            supertypes: new[] { CardSupertype.Legendary });
        oppLegend.SetOwner(_bob);
        oppLegend.SetController(_bob);

        // CR 603.1 — the condition fires only for a legendary creature the
        // source's controller controls.
        legend.Condition.Matches(
            new Majik.Core.Events.CardMovedEvent(myLegend, ZoneType.Hand, ZoneType.Battlefield), legend)
            .Should().BeTrue("a legendary creature I control entered");

        legend.Condition.Matches(
            new Majik.Core.Events.CardMovedEvent(myNonLegend, ZoneType.Hand, ZoneType.Battlefield), legend)
            .Should().BeFalse("a non-legendary creature I control entered");

        legend.Condition.Matches(
            new Majik.Core.Events.CardMovedEvent(oppLegend, ZoneType.Hand, ZoneType.Battlefield), legend)
            .Should().BeFalse("a legendary creature an opponent controls entered");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static TriggeredAbility EtbDiscardTrigger(Card card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("discard")));

    private static TriggeredAbility LegendaryMillTrigger(Card card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("mill")));

    private static void FillHand(Player player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var s = new Sorcery($"H{i + 1}", "{1}");
            s.SetOwner(player);
            s.SetController(player);
            s.SetZone(ZoneType.Hand);
            player.Zones.Hand.AddCard(s);
        }
    }

    private static void FillLibrary(Player player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var s = new Sorcery($"L{i + 1}", "{1}");
            s.SetOwner(player);
            s.SetController(player);
            s.SetZone(ZoneType.Library);
            player.Zones.Library.AddCard(s);
        }
    }
}
