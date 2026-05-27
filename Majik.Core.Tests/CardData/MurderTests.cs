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
/// Tests for Murder (Magic 2013, {1}{B}{B}, Instant).
///
/// Oracle text: "Destroy target creature."
///
/// Covers:
///   - Card identity (Instant, {1}{B}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a nonblack creature (moves to owner's graveyard, CR 701.7).
///   - Destroys a black creature — unconditional, no colour filter (CR 701.7).
///   - Destroys a multicolour creature with black pip — still destroyed.
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class MurderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Murder_IsInstant_AtCost1BB()
    {
        var card = MurderFactory.Create(_alice);

        card.Name.Should().Be("Murder");
        card.ManaCost.Should().Be("{1}{B}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Murder()
    {
        var card = NamedCardFactory.Create("Murder", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Murder");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys nonblack creature (unconditional removal)
    // -----------------------------------------------------------------------

    [Fact]
    public void Murder_DestroysNonblackCreature()
    {
        // Red creature — nonblack; Murder has no colour restriction.
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Murder destroys any creature unconditionally (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys black creature (Murder has no colour filter)
    // -----------------------------------------------------------------------

    [Fact]
    public void Murder_BlackCreature_IsDestroyed()
    {
        // Mono-black creature — Murder is unconditional, unlike Doom Blade.
        var imp = NewControlledCreature(_bob, "Putrid Imp", "{B}");

        Resolve(imp);

        imp.Zone.Should().Be(ZoneType.Graveyard,
            "Murder destroys any creature including black ones (no colour filter)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(imp);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(imp);
    }

    [Fact]
    public void Murder_MulticolorCreatureWithBlackPip_IsDestroyed()
    {
        // BR creature — has a {B} pip; Murder still destroys it.
        var demon = NewControlledCreature(_bob, "Blood Crypt Demon", "{B}{R}");

        Resolve(demon);

        demon.Zone.Should().Be(ZoneType.Graveyard,
            "Murder destroys multicolour creatures with black pips (CR 701.7, no restriction)");
    }

    [Fact]
    public void Murder_ColorlessCreature_IsDestroyed()
    {
        // Colorless creature — Murder destroys any creature.
        var eldrazi = NewControlledCreature(_bob, "Eldrazi Mimic", "{2}");

        Resolve(eldrazi);

        eldrazi.Zone.Should().Be(ZoneType.Graveyard,
            "Murder destroys colorless creatures (CR 701.7)");
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void Murder_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        // Zone unchanged by the resolve. CR 608.2b — illegal target → effect does nothing.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Murder_NonCreatureTarget_DoesNothing()
    {
        // Targeting a non-creature object (e.g. a raw object token) — no-op.
        var notACreature = new object();

        ResolveRaw(notACreature);

        // No exception thrown; the effect resolves cleanly with no game change.
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = MurderFactory.BuildDefinition(targetResolver: t => t);
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
