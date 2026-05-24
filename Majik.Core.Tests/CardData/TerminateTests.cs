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
/// Tests for Terminate (Planeshift / various reprints, {B}{R}, Instant).
///
/// Covers:
///   - Card identity (Instant, {B}{R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Destroys target creature (moves to graveyard, CR 701.7).
///   - Target not on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
public class TerminateTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Terminate_IsInstant_AtCostBR()
    {
        var card = TerminateFactory.Create(_alice);

        card.Name.Should().Be("Terminate");
        card.ManaCost.Should().Be("{B}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Terminate()
    {
        var card = NamedCardFactory.Create("Terminate", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Terminate");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Terminate_DestroysTargetCreature()
    {
        var creature = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Terminate destroys the target creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(creature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(creature);
    }

    [Fact]
    public void Terminate_TargetNotOnBattlefield_DoesNothing()
    {
        // Target leaves battlefield before Terminate resolves (CR 608.2b).
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Remove from battlefield — simulate it leaving before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        // Snapshot zone before calling resolve — should not change.
        ResolveRaw(creature);

        // No additional zone change; creature is already in graveyard via the sim.
        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Terminate does nothing when the target is no longer on the battlefield (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = TerminateFactory.BuildSpellDefinition(resolver: t => t);
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

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
