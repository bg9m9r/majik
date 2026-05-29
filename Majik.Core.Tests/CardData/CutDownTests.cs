using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Cut Down (Dominaria United, {B}, Instant).
///
/// Oracle text: "Destroy target creature with total power and toughness 5 or less."
///
/// Covers:
///   - Card identity (Instant, {B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a creature whose power + toughness ≤ 5 (CR 701.7).
///   - Creature whose power + toughness ≥ 6 → no-op at resolution (CR 608.2b).
///   - Boundary: exactly 5 is legal; exactly 6 is illegal.
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class CutDownTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CutDown_IsInstant_AtCostB()
    {
        var card = CutDownFactory.Create(_alice);

        card.Name.Should().Be("Cut Down");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CutDown()
    {
        var card = NamedCardFactory.Create("Cut Down", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Cut Down");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys low-stat creature
    // -----------------------------------------------------------------------

    [Fact]
    public void CutDown_DestroysCreature_WithTotalPowerToughness5OrLess()
    {
        // 2/2 → total 4 ≤ 5, legal target.
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Cut Down destroys a target whose power + toughness ≤ 5 (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void CutDown_DestroysCreature_AtExactlyFive()
    {
        // 3/2 → total 5, exactly at the boundary — legal.
        var creature = NewControlledCreature(_bob, "Goblin Test", "{1}{R}", 3, 2);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "total power + toughness of exactly 5 is legal (≤ 5)");
    }

    // -----------------------------------------------------------------------
    // Resolution — high-stat creature filter
    // -----------------------------------------------------------------------

    [Fact]
    public void CutDown_HighStatCreature_NotDestroyed()
    {
        // 4/4 → total 8 > 5, illegal target.
        var serra = NewControlledCreature(_bob, "Serra Angel", "{3}{W}{W}", 4, 4);

        Resolve(serra);

        serra.Zone.Should().Be(ZoneType.Battlefield,
            "Cut Down cannot destroy a creature with power + toughness > 5 (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(serra);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(serra);
    }

    [Fact]
    public void CutDown_AtExactlySix_NotDestroyed()
    {
        // 3/3 → total 6, just over the boundary — illegal.
        var creature = NewControlledCreature(_bob, "Centaur Test", "{2}{G}", 3, 3);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            "total power + toughness of 6 exceeds the ≤ 5 threshold");
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void CutDown_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Llanowar Elves", "{G}", 1, 1);

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.2b — illegal target at resolution → effect does nothing");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = CutDownFactory.BuildDefinition(targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(
        Player owner, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
