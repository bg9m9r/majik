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
/// Tests for Cast Down (Dominaria, {1}{B}, Instant).
///
/// Oracle text: "Destroy target nonlegendary creature."
///
/// Covers:
///   - Card identity (Instant, {1}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a nonlegendary creature (moves to owner's graveyard, CR 701.7).
///   - Legendary creature target → no-op at resolution
///     (CR 205.4a Legendary supertype + CR 608.2b illegal-target).
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class CastDownTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CastDown_IsInstant_AtCost1B()
    {
        var card = CastDownFactory.Create(_alice);

        card.Name.Should().Be("Cast Down");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CastDown()
    {
        var card = NamedCardFactory.Create("Cast Down", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Cast Down");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys nonlegendary creature
    // -----------------------------------------------------------------------

    [Fact]
    public void CastDown_DestroysNonlegendaryCreature()
    {
        // Plain creature — no Legendary supertype, legal target.
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Cast Down destroys the nonlegendary target (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void CastDown_BlackCreature_Destroyed()
    {
        // Unlike Doom Blade, Cast Down has no colour restriction —
        // a mono-black nonlegendary creature is a legal target.
        var imp = NewControlledCreature(_bob, "Putrid Imp", "{B}");

        Resolve(imp);

        imp.Zone.Should().Be(ZoneType.Graveyard,
            "Cast Down's only target restriction is the Legendary supertype");
    }

    // -----------------------------------------------------------------------
    // Resolution — legendary filter
    // -----------------------------------------------------------------------

    [Fact]
    public void CastDown_LegendaryCreature_NotDestroyed()
    {
        // Legendary creature (e.g. Thalia, Guardian of Thraben) — illegal target.
        var thalia = new Creature(
            "Thalia, Guardian of Thraben",
            "{1}{W}",
            2,
            1,
            supertypes: new[] { CardSupertype.Legendary });
        thalia.SetOwner(_bob);
        thalia.SetController(_bob);
        thalia.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(thalia);

        Resolve(thalia);

        thalia.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        thalia.Zone.Should().Be(ZoneType.Battlefield,
            "Cast Down cannot destroy a Legendary creature (CR 205.4a)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(thalia);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(thalia);
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void CastDown_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

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
        var def = CastDownFactory.BuildDefinition(targetResolver: t => t);
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
