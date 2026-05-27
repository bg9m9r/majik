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
/// Tests for Terror (Alpha / various reprints, {1}{B}, Instant).
///
/// Oracle text: "Destroy target nonartifact, nonblack creature.
///               It can't be regenerated."
///
/// Covers:
///   - Card identity (Instant, {1}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a nonblack, nonartifact creature (CR 701.7).
///   - Black creature target → no-op at resolution (CR 105 + CR 608.2b).
///   - Artifact creature target → no-op at resolution (CR 608.2b).
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class TerrorTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Terror_IsInstant_AtCost1B()
    {
        var card = TerrorFactory.Create(_alice);

        card.Name.Should().Be("Terror");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Terror()
    {
        var card = NamedCardFactory.Create("Terror", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Terror");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys a nonblack, nonartifact creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Terror_DestroysNonblackNonartifactCreature()
    {
        // Green creature — nonblack, nonartifact: legal target.
        var tarmogoyf = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        Resolve(tarmogoyf);

        tarmogoyf.Zone.Should().Be(ZoneType.Graveyard,
            "Terror destroys a nonblack nonartifact creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(tarmogoyf);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(tarmogoyf);
    }

    [Fact]
    public void Terror_ColorlessNonartifactCreature_IsNonblack_Destroyed()
    {
        // Colorless non-artifact creature — no {B} pip, not an artifact.
        var eldrazi = NewControlledCreature(_bob, "Eldrazi Mimic", "{2}");

        Resolve(eldrazi);

        eldrazi.Zone.Should().Be(ZoneType.Graveyard,
            "Colorless non-artifact creatures are nonblack nonartifact (CR 105) and legal Terror targets");
    }

    // -----------------------------------------------------------------------
    // Resolution — black creature filter
    // -----------------------------------------------------------------------

    [Fact]
    public void Terror_BlackCreature_NotDestroyed()
    {
        // Mono-black creature — illegal target (CR 105 + CR 608.2b).
        var imp = NewControlledCreature(_bob, "Putrid Imp", "{B}");

        Resolve(imp);

        imp.Zone.Should().Be(ZoneType.Battlefield,
            "Terror cannot destroy a black creature (CR 105 nonblack filter)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(imp);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(imp);
    }

    [Fact]
    public void Terror_MulticolorCreatureWithBlackPip_NotDestroyed()
    {
        // BR creature — has a {B} pip, so it counts as black (CR 105.2a).
        var demon = NewControlledCreature(_bob, "Kolaghan Demon", "{B}{R}");

        Resolve(demon);

        demon.Zone.Should().Be(ZoneType.Battlefield,
            "A creature with a {B} pip is black (CR 105.2a) and immune to Terror");
    }

    // -----------------------------------------------------------------------
    // Resolution — artifact creature filter
    // -----------------------------------------------------------------------

    [Fact]
    public void Terror_ArtifactCreature_NotDestroyed()
    {
        // Artifact creature — Terror says "nonartifact" so this is illegal.
        var myr = NewControlledArtifactCreature(_bob, "Myr Battlesphere", "{7}");

        Resolve(myr);

        myr.Zone.Should().Be(ZoneType.Battlefield,
            "Terror cannot destroy an artifact creature (nonartifact restriction)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(myr);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(myr);
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void Terror_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Goblin Guide", "{R}");

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
        var def = TerrorFactory.BuildDefinition(targetResolver: t => t);
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

    /// <summary>
    /// Creates an artifact creature by constructing a <see cref="Creature"/>
    /// and then adding the <see cref="CardType.Artifact"/> type so Terror's
    /// nonartifact filter rejects it.
    /// </summary>
    private static Creature NewControlledArtifactCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 4, 7);
        c.AddCardType(CardType.Artifact);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
