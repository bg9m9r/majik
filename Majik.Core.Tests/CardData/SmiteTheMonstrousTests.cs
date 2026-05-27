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
/// Tests for Smite the Monstrous ({3}{W}, Instant).
///
/// Oracle text: "Destroy target creature with power 4 or greater."
///
/// Covers:
///   - Card identity (Instant, {3}{W}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a 4/4 creature → graveyard (CR 701.7, power exactly 4).
///   - Destroys a 5/1 creature → graveyard (power 5, ≥ 4).
///   - No-op on a 3/3 creature (power 3 &lt; 4, CR 608.2b illegal-target filter).
///   - No-op on target not on battlefield (CR 608.2b).
/// </summary>
public class SmiteTheMonstrousTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SmiteTheMonstrous_IsInstant_AtCost3W()
    {
        var card = SmiteTheMonstrousFactory.Create(_alice);

        card.Name.Should().Be("Smite the Monstrous");
        card.ManaCost.Should().Be("{3}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SmiteTheMonstrous()
    {
        var card = NamedCardFactory.Create("Smite the Monstrous", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Smite the Monstrous");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{W}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys creatures with power ≥ 4
    // -----------------------------------------------------------------------

    [Fact]
    public void SmiteTheMonstrous_Destroys_FourFour_Creature()
    {
        // 4/4 — power exactly 4, legal target.
        var creature = NewControlledCreature(_bob, "Serra Angel", "{3}{W}{W}", power: 4, toughness: 4);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Smite the Monstrous destroys target creature with power 4 or greater (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(creature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(creature);
    }

    [Fact]
    public void SmiteTheMonstrous_Destroys_FiveOne_Creature()
    {
        // 5/1 — power 5 ≥ 4, legal target.
        var creature = NewControlledCreature(_bob, "Ball Lightning", "{R}{R}{R}", power: 5, toughness: 1);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Smite the Monstrous destroys target creature with power 5 (≥ 4) (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(creature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(creature);
    }

    // -----------------------------------------------------------------------
    // Resolution — power < 4 filter (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void SmiteTheMonstrous_ThreeThree_NotDestroyed()
    {
        // 3/3 — power 3 < 4, illegal target; effect does nothing at resolution.
        var creature = NewControlledCreature(_bob, "Watchwolf", "{G}{W}", power: 3, toughness: 3);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            "Smite the Monstrous cannot destroy a creature with power less than 4 (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(creature);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(creature);
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void SmiteTheMonstrous_TargetNotOnBattlefield_DoesNothing()
    {
        // Target leaves the battlefield before Smite the Monstrous resolves.
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}", power: 4, toughness: 5);

        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        // Zone unchanged by the resolve; creature is already in graveyard.
        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Smite the Monstrous does nothing when the target is no longer on the battlefield (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = SmiteTheMonstrousFactory.BuildDefinition(targetResolver: t => t);
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

    private static Creature NewControlledCreature(Player owner, string name, string cost,
        int power = 1, int toughness = 1)
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
