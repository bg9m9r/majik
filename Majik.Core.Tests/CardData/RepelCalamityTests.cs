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
/// Tests for Repel Calamity ({1}{W}, Instant).
///
/// Oracle text: "Destroy target creature with power or toughness 4 or greater."
///
/// Covers the card's UNIQUE behaviour — the power-OR-toughness predicate that
/// distinguishes it from Smite the Monstrous (power only):
///   - Card identity (Instant, {1}{W}).
///   - Destroys a 4/1 creature (power 4 ≥ 4) → graveyard (CR 701.7).
///   - Destroys a 1/4 creature (toughness 4 ≥ 4, power 1) → graveyard — the
///     OR branch Smite the Monstrous would miss.
///   - No-op on a 3/3 creature (power 3 AND toughness 3, both &lt; 4,
///     CR 608.2b illegal-target filter).
///   - No-op on target not on battlefield (CR 608.2b).
///
/// (NamedCardFactory dispatch + well-formedness are asserted for every
/// implemented card by CardFactoryContractTests — no per-card dispatch test.)
/// </summary>
[Trait("Color", "W")]
public class RepelCalamityTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void RepelCalamity_IsInstant_AtCost1W()
    {
        var card = RepelCalamityFactory.Create(_alice);

        card.Name.Should().Be("Repel Calamity");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — power ≥ 4 branch
    // -----------------------------------------------------------------------

    [Fact]
    public void RepelCalamity_Destroys_HighPower_Creature()
    {
        // 4/1 — power 4 ≥ 4, legal target via the power branch.
        var creature = NewControlledCreature(_bob, "Ball Lightning", "{R}{R}{R}", power: 4, toughness: 1);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Repel Calamity destroys a creature with power 4 or greater (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(creature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(creature);
    }

    // -----------------------------------------------------------------------
    // Resolution — toughness ≥ 4 branch (the OR that Smite would miss)
    // -----------------------------------------------------------------------

    [Fact]
    public void RepelCalamity_Destroys_HighToughness_LowPower_Creature()
    {
        // 1/4 — power 1 < 4 BUT toughness 4 ≥ 4, legal target via the
        // toughness branch (this is what distinguishes Repel Calamity from
        // a power-only destroy like Smite the Monstrous).
        var creature = NewControlledCreature(_bob, "Wall of Omens", "{1}{W}", power: 1, toughness: 4);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Repel Calamity destroys a creature with toughness 4 or greater even if power < 4 (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(creature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(creature);
    }

    // -----------------------------------------------------------------------
    // Resolution — both stats < 4 filter (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void RepelCalamity_ThreeThree_NotDestroyed()
    {
        // 3/3 — power 3 AND toughness 3, both < 4; illegal target, no-op.
        var creature = NewControlledCreature(_bob, "Watchwolf", "{G}{W}", power: 3, toughness: 3);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            "Repel Calamity cannot destroy a creature whose power and toughness are both less than 4 (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(creature);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(creature);
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void RepelCalamity_TargetNotOnBattlefield_DoesNothing()
    {
        // Target leaves the battlefield before Repel Calamity resolves.
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}", power: 4, toughness: 5);

        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Repel Calamity does nothing when the target is no longer on the battlefield (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = RepelCalamityFactory.BuildDefinition(targetResolver: t => t);
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
