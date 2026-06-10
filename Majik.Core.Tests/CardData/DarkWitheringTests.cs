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
/// Tests for Dark Withering (Torment, {4}{B}{B}, Instant).
///
/// Oracle text: "Destroy target nonblack creature. Madness {B}"
///
/// Madness ({B}) is intrinsic — handled engine-wide via MadnessCatalog + the
/// central discard funnel (covered by MadnessDiscardFunnelTests), so it is NOT
/// exercised here. These tests cover only the spell body:
///   - Card identity (Instant, {4}{B}{B}, owner / controller).
///   - Destroys a nonblack creature (moves to owner's graveyard, CR 701.7).
///   - Colorless creature is nonblack → destroyed (CR 105).
///   - Black / black-pip creature target → no-op at resolution (CR 105 +
///     CR 608.2b illegal-target).
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "B")]
public class DarkWitheringTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void DarkWithering_IsInstant_AtCost4BB()
    {
        var card = DarkWitheringFactory.Create(_alice);

        card.Name.Should().Be("Dark Withering");
        card.ManaCost.Should().Be("{4}{B}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys nonblack creature
    // -----------------------------------------------------------------------

    [Fact]
    public void DarkWithering_DestroysNonblackCreature()
    {
        // Red creature — nonblack, legal target.
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Dark Withering destroys the nonblack target (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void DarkWithering_ColorlessCreature_IsNonblack_Destroyed()
    {
        // Colorless creature (e.g. Eldrazi) — no {B} pip, still nonblack.
        var eldrazi = NewControlledCreature(_bob, "Eldrazi Mimic", "{2}");

        Resolve(eldrazi);

        eldrazi.Zone.Should().Be(ZoneType.Graveyard,
            "Colorless creatures are nonblack (CR 105) and legal Dark Withering targets");
    }

    // -----------------------------------------------------------------------
    // Resolution — black creature filter
    // -----------------------------------------------------------------------

    [Fact]
    public void DarkWithering_BlackCreature_NotDestroyed()
    {
        // Mono-black creature — illegal target (CR 105 + CR 608.2b).
        var imp = NewControlledCreature(_bob, "Putrid Imp", "{B}");

        Resolve(imp);

        imp.Zone.Should().Be(ZoneType.Battlefield,
            "Dark Withering cannot destroy a black creature (CR 105 nonblack filter)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(imp);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(imp);
    }

    [Fact]
    public void DarkWithering_MulticolorCreatureWithBlackPip_NotDestroyed()
    {
        // BR creature — has a {B} pip, so it counts as black (CR 105.2a).
        var demon = NewControlledCreature(_bob, "Kolaghan Demon", "{B}{R}");

        Resolve(demon);

        demon.Zone.Should().Be(ZoneType.Battlefield,
            "A creature with a {B} pip is black (CR 105.2a) and immune to Dark Withering");
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void DarkWithering_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        // Zone unchanged by the resolve (no double-move / exception).
        // CR 608.2b — illegal target → effect does nothing.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = DarkWitheringFactory.BuildDefinition(targetResolver: t => t);
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
