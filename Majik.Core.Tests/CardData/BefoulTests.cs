using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Befoul (Invasion / reprints, {2}{B}{B}, Sorcery).
///
/// Oracle text: "Destroy target land or nonblack creature. It can't be regenerated."
///
/// Covers:
///   - Card identity (Sorcery, {2}{B}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a target land → graveyard (CR 701.7, DestroyNoRegeneration).
///   - Destroys a nonblack creature → graveyard (CR 701.7, DestroyNoRegeneration).
///   - No-op on a black creature target (CR 105 colour filter + CR 608.2b).
///   - No-op when target is not on the battlefield (CR 608.2b).
/// </summary>
public class BefoulTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Befoul_IsSorcery_AtCost2BB()
    {
        var card = BefoulFactory.Create(_alice);

        card.Name.Should().Be("Befoul");
        card.ManaCost.Should().Be("{2}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Befoul_IsBlack()
    {
        var card = BefoulFactory.Create(_alice);

        // Befoul has two {B} pips — it is a black card (CR 105.2a).
        CardColors.GetColors(card).Should().Contain(ManaColor.Black,
            "Befoul has {B}{B} in its mana cost (CR 105.2a)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Befoul()
    {
        var card = NamedCardFactory.Create("Befoul", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Befoul");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys target land
    // -----------------------------------------------------------------------

    [Fact]
    public void Befoul_DestroysTargetLand()
    {
        var land = NewControlledLand(_bob, "Swamp");

        Resolve(land);

        land.Zone.Should().Be(ZoneType.Graveyard,
            "Befoul destroys any land target (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(land);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(land);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys nonblack creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Befoul_DestroysNonblackCreature()
    {
        // Red creature — nonblack, legal target.
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Befoul destroys the nonblack creature target (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void Befoul_ColorlessCreature_IsNonblack_Destroyed()
    {
        // Colorless creature (e.g. Eldrazi) — no {B} pip, nonblack (CR 105).
        var eldrazi = NewControlledCreature(_bob, "Eldrazi Mimic", "{2}");

        Resolve(eldrazi);

        eldrazi.Zone.Should().Be(ZoneType.Graveyard,
            "Colorless creatures are nonblack (CR 105) and legal Befoul targets");
    }

    // -----------------------------------------------------------------------
    // Resolution — black creature filter (no-op)
    // -----------------------------------------------------------------------

    [Fact]
    public void Befoul_BlackCreature_NotDestroyed()
    {
        // Mono-black creature — illegal nonblack-creature path; not a land either.
        var imp = NewControlledCreature(_bob, "Putrid Imp", "{B}");

        Resolve(imp);

        imp.Zone.Should().Be(ZoneType.Battlefield,
            "Befoul cannot destroy a black creature (CR 105 nonblack filter + CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(imp);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(imp);
    }

    [Fact]
    public void Befoul_MulticolorCreatureWithBlackPip_NotDestroyed()
    {
        // BR creature — has a {B} pip, so it counts as black (CR 105.2a).
        var demon = NewControlledCreature(_bob, "Blood Crypt Demon", "{B}{R}");

        Resolve(demon);

        demon.Zone.Should().Be(ZoneType.Battlefield,
            "A creature with a {B} pip is black (CR 105.2a) and cannot be targeted as a nonblack creature");
    }

    // -----------------------------------------------------------------------
    // Resolution — target not on battlefield (no-op)
    // -----------------------------------------------------------------------

    [Fact]
    public void Befoul_LandTargetNotOnBattlefield_DoesNothing()
    {
        var land = NewControlledLand(_bob, "Forest");

        // Simulate land leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(land);
        land.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(land);

        ResolveRaw(land);

        // Zone unchanged by resolve — CR 608.2b illegal target → no-op.
        land.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Befoul_CreatureTargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        // Zone unchanged by resolve — CR 608.2b illegal target → no-op.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = BefoulFactory.BuildDefinition(targetResolver: t => t);
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

    private static Land NewControlledLand(Player owner, string name)
    {
        var l = new Land(name);
        l.SetOwner(owner);
        l.SetController(owner);
        l.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(l);
        return l;
    }
}
