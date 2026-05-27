using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Plummet (Magic 2012, {1}{G}, Instant).
///
/// Oracle text: "Destroy target creature with flying."
///
/// Covers:
///   - Card identity (Instant, {1}{G}, green, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a creature with Flying (moves to owner's graveyard, CR 701.7).
///   - No-op on a creature without Flying (CR 608.2b — illegal-target filter
///     at resolution).
///   - No-op on an off-battlefield target (CR 608.2b).
/// </summary>
public class PlummetFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Plummet_IsInstant_AtCost1G()
    {
        var card = PlummetFactory.Create(_alice);

        card.Name.Should().Be("Plummet");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Plummet_IsGreen()
    {
        var card = PlummetFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.Green,
            "Plummet has a {G} pip in its mana cost (CR 105)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Plummet()
    {
        var card = NamedCardFactory.Create("Plummet", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Plummet");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{G}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys a creature with Flying
    // -----------------------------------------------------------------------

    [Fact]
    public void Plummet_DestroysFlyingCreature()
    {
        var bird = NewFlyingCreature(_bob, "Ornithopter", "{0}");

        Resolve(bird);

        bird.Zone.Should().Be(ZoneType.Graveyard,
            "Plummet destroys a creature with Flying (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bird);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bird);
    }

    // -----------------------------------------------------------------------
    // Resolution — no-op on a creature without Flying
    // -----------------------------------------------------------------------

    [Fact]
    public void Plummet_NonFlyingCreature_NotDestroyed()
    {
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "Plummet cannot destroy a creature without Flying (CR 608.2b — illegal target)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bear);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void Plummet_TargetNotOnBattlefield_DoesNothing()
    {
        var bird = NewFlyingCreature(_bob, "Ornithopter", "{0}");

        // Simulate target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(bird);
        bird.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bird);

        ResolveRaw(bird);

        // Zone unchanged by the resolve. CR 608.2b — illegal target → no-op.
        bird.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = PlummetFactory.BuildDefinition(targetResolver: t => t);
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

    private static Creature NewControlledCreature(
        Player owner,
        string name,
        string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Creature NewFlyingCreature(
        Player owner,
        string name,
        string cost)
    {
        var c = NewControlledCreature(owner, name, cost);
        c.AddAbility(new KeywordAbility("Flying", c, owner));
        return c;
    }
}
