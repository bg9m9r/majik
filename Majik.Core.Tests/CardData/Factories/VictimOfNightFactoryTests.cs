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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Victim of Night (Innistrad, {B}{B}, Instant).
///
/// Oracle text: "Destroy target non-Vampire, non-Werewolf, non-Zombie creature."
///
/// Covers:
///   - Card identity (Instant, {B}{B}, black, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a plain creature (moves to owner's graveyard, CR 701.7).
///   - No-op on a Zombie (CR 608.2b illegal-target filter at resolution).
///   - No-op on a Vampire (CR 608.2b).
///   - No-op on a Werewolf (CR 608.2b).
///   - No-op on an off-battlefield target (CR 608.2b).
/// </summary>
public class VictimOfNightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void VictimOfNight_IsInstant_AtCostBB()
    {
        var card = VictimOfNightFactory.Create(_alice);

        card.Name.Should().Be("Victim of Night");
        card.ManaCost.Should().Be("{B}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VictimOfNight_IsBlack()
    {
        var card = VictimOfNightFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.Black,
            "Victim of Night has two {B} pips in its mana cost (CR 105)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VictimOfNight()
    {
        var card = NamedCardFactory.Create("Victim of Night", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Victim of Night");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{B}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys a plain creature
    // -----------------------------------------------------------------------

    [Fact]
    public void VictimOfNight_DestroysPlainCreature()
    {
        // A generic 2/2 creature with no special subtypes.
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Victim of Night destroys a creature that is not a Vampire, Werewolf, or Zombie (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Resolution — no-op on Zombie
    // -----------------------------------------------------------------------

    [Fact]
    public void VictimOfNight_ZombieCreature_NotDestroyed()
    {
        var zombie = NewControlledCreature(_bob, "Gravecrawler", "{B}",
            CardSubtype.Zombie);

        Resolve(zombie);

        zombie.Zone.Should().Be(ZoneType.Battlefield,
            "Victim of Night cannot destroy a Zombie (CR 608.2b — illegal target)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(zombie);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(zombie);
    }

    // -----------------------------------------------------------------------
    // Resolution — no-op on Vampire
    // -----------------------------------------------------------------------

    [Fact]
    public void VictimOfNight_VampireCreature_NotDestroyed()
    {
        var vampire = NewControlledCreature(_bob, "Vampire Nighthawk", "{1}{B}{B}",
            CardSubtype.Vampire);

        Resolve(vampire);

        vampire.Zone.Should().Be(ZoneType.Battlefield,
            "Victim of Night cannot destroy a Vampire (CR 608.2b — illegal target)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(vampire);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(vampire);
    }

    // -----------------------------------------------------------------------
    // Resolution — no-op on Werewolf
    // -----------------------------------------------------------------------

    [Fact]
    public void VictimOfNight_WerewolfCreature_NotDestroyed()
    {
        var werewolf = NewControlledCreature(_bob, "Tovolar's Huntmaster", "{4}{R}{G}",
            CardSubtype.Werewolf);

        Resolve(werewolf);

        werewolf.Zone.Should().Be(ZoneType.Battlefield,
            "Victim of Night cannot destroy a Werewolf (CR 608.2b — illegal target)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(werewolf);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(werewolf);
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void VictimOfNight_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate target leaving the battlefield before resolution.
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
        var def = VictimOfNightFactory.BuildDefinition(targetResolver: t => t);
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
