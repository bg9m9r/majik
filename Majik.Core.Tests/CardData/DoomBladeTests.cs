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
/// Tests for Doom Blade (Magic 2010, {1}{B}, Instant).
///
/// Oracle text: "Destroy target nonblack creature."
///
/// Covers:
///   - Card identity (Instant, {1}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a nonblack creature (moves to owner's graveyard, CR 701.7).
///   - Black creature target → no-op at resolution (CR 105 colour filter +
///     CR 608.2b illegal-target).
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class DoomBladeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DoomBlade_IsInstant_AtCost1B()
    {
        var card = DoomBladeFactory.Create(_alice);

        card.Name.Should().Be("Doom Blade");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DoomBlade()
    {
        var card = NamedCardFactory.Create("Doom Blade", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Doom Blade");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys nonblack creature
    // -----------------------------------------------------------------------

    [Fact]
    public void DoomBlade_DestroysNonblackCreature()
    {
        // Red creature — nonblack, legal target.
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Doom Blade destroys the nonblack target (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void DoomBlade_ColorlessCreature_IsNonblack_Destroyed()
    {
        // Colorless creature (e.g. Eldrazi) — no {B} pip, still nonblack.
        var eldrazi = NewControlledCreature(_bob, "Eldrazi Mimic", "{2}");

        Resolve(eldrazi);

        eldrazi.Zone.Should().Be(ZoneType.Graveyard,
            "Colorless creatures are nonblack (CR 105) and legal Doom Blade targets");
    }

    // -----------------------------------------------------------------------
    // Resolution — black creature filter
    // -----------------------------------------------------------------------

    [Fact]
    public void DoomBlade_BlackCreature_NotDestroyed()
    {
        // Mono-black creature — illegal target (CR 105 + CR 608.2b).
        var imp = NewControlledCreature(_bob, "Putrid Imp", "{B}");

        Resolve(imp);

        imp.Zone.Should().Be(ZoneType.Battlefield,
            "Doom Blade cannot destroy a black creature (CR 105 nonblack filter)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(imp);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(imp);
    }

    [Fact]
    public void DoomBlade_MulticolorCreatureWithBlackPip_NotDestroyed()
    {
        // BR creature — has a {B} pip, so it counts as black (CR 105.2a).
        var terminate = NewControlledCreature(_bob, "Blood Crypt Demon", "{B}{R}");

        Resolve(terminate);

        terminate.Zone.Should().Be(ZoneType.Battlefield,
            "A creature with a {B} pip is black (CR 105.2a) and immune to Doom Blade");
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void DoomBlade_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        // Zone unchanged by the resolve (no double-move into graveyard /
        // exception). CR 608.2b — illegal target → effect does nothing.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = DoomBladeFactory.BuildDefinition(targetResolver: t => t);
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
