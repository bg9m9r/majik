using FluentAssertions;
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
/// Tests for Shoot the Sheriff (Outlaws of Thunder Junction, {1}{B}, Instant).
///
/// Oracle text: "Destroy target non-outlaw creature. (Assassins, Mercenaries,
/// Pirates, Rogues, and Warlocks are outlaws. Everyone else is fair game.)"
///
/// Covers the card's UNIQUE behaviour — the non-outlaw destroy filter — plus a
/// single identity assert. NamedCardFactory dispatch + well-formedness are
/// covered for every implemented card by CardFactoryContractTests.
/// </summary>
[Trait("Color", "B")]
public class ShootTheSheriffFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity (exact mana cost — non-vanilla spell)
    // -----------------------------------------------------------------------

    [Fact]
    public void ShootTheSheriff_IsBlackInstant_AtCost1B()
    {
        var card = ShootTheSheriffFactory.Create(_alice);

        card.Name.Should().Be("Shoot the Sheriff");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Black,
            "Shoot the Sheriff has a {B} pip in its mana cost (CR 105)");
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys a plain non-outlaw creature
    // -----------------------------------------------------------------------

    [Fact]
    public void ShootTheSheriff_DestroysNonOutlawCreature()
    {
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Shoot the Sheriff destroys a creature that is not an outlaw (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Resolution — no-op on each of the five outlaw subtypes (CR 608.2b)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(CardSubtype.Assassin)]
    [InlineData(CardSubtype.Mercenary)]
    [InlineData(CardSubtype.Pirate)]
    [InlineData(CardSubtype.Rogue)]
    [InlineData(CardSubtype.Warlock)]
    public void ShootTheSheriff_OutlawCreature_NotDestroyed(CardSubtype outlaw)
    {
        var creature = NewControlledCreature(_bob, "An Outlaw", "{B}", outlaw);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            $"Shoot the Sheriff cannot destroy an outlaw ({outlaw}) — CR 608.2b illegal target");
        _bob.Zones.Battlefield.GetCards().Should().Contain(creature);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(creature);
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void ShootTheSheriff_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = ShootTheSheriffFactory.BuildDefinition(targetResolver: t => t);
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
        string cost,
        CardSubtype? subtype = null)
    {
        var subtypes = subtype.HasValue
            ? new[] { subtype.Value }
            : Array.Empty<CardSubtype>();

        var c = new Creature(name, cost, 1, 1, subtypes: subtypes);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
