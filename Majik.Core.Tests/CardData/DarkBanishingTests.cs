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
/// Tests for Dark Banishing (Fallen Empires, {2}{B}, Instant).
///
/// Oracle text: "Destroy target nonblack creature. It can't be regenerated."
///
/// Covers:
///   - Card identity (Instant, {2}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a nonblack creature (moves to owner's graveyard, CR 701.7).
///   - Black creature target → no-op at resolution (CR 105 colour filter +
///     CR 608.2b illegal-target).
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class DarkBanishingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DarkBanishing_IsInstant_AtCost2B()
    {
        var card = DarkBanishingFactory.Create(_alice);

        card.Name.Should().Be("Dark Banishing");
        card.ManaCost.Should().Be("{2}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DarkBanishing()
    {
        var card = NamedCardFactory.Create("Dark Banishing", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Dark Banishing");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys nonblack creature (no-regen path)
    // -----------------------------------------------------------------------

    [Fact]
    public void DarkBanishing_DestroysNonblackCreature()
    {
        // Red creature — nonblack, legal target.
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Dark Banishing destroys the nonblack target (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void DarkBanishing_ColorlessCreature_IsNonblack_Destroyed()
    {
        // Colorless creature (e.g. Eldrazi) — no {B} pip, still nonblack.
        var eldrazi = NewControlledCreature(_bob, "Eldrazi Mimic", "{2}");

        Resolve(eldrazi);

        eldrazi.Zone.Should().Be(ZoneType.Graveyard,
            "Colorless creatures are nonblack (CR 105) and legal Dark Banishing targets");
    }

    // -----------------------------------------------------------------------
    // Resolution — black creature filter
    // -----------------------------------------------------------------------

    [Fact]
    public void DarkBanishing_BlackCreature_NotDestroyed()
    {
        // Mono-black creature — illegal target (CR 105 + CR 608.2b).
        var imp = NewControlledCreature(_bob, "Putrid Imp", "{B}");

        Resolve(imp);

        imp.Zone.Should().Be(ZoneType.Battlefield,
            "Dark Banishing cannot destroy a black creature (CR 105 nonblack filter)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(imp);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(imp);
    }

    [Fact]
    public void DarkBanishing_MulticolorCreatureWithBlackPip_NotDestroyed()
    {
        // BR creature — has a {B} pip, so it counts as black (CR 105.2a).
        var demon = NewControlledCreature(_bob, "Blood Crypt Demon", "{B}{R}");

        Resolve(demon);

        demon.Zone.Should().Be(ZoneType.Battlefield,
            "A creature with a {B} pip is black (CR 105.2a) and immune to Dark Banishing");
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void DarkBanishing_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        // Zone unchanged by the resolve. CR 608.2b — illegal target → no-op.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = DarkBanishingFactory.BuildDefinition(targetResolver: t => t);
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
