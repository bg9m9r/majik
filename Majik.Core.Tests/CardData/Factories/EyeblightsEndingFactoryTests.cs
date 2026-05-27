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
/// Tests for Eyeblight's Ending (Lorwyn, {2}{B}, Instant).
///
/// Oracle text: "Destroy target non-Elf creature."
///
/// Covers:
///   - Card identity (Instant, {2}{B}, black, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a plain non-Elf creature (moves to owner's graveyard, CR 701.7).
///   - No-op on an Elf creature (CR 608.2b illegal-target filter at resolution).
///   - No-op on an off-battlefield target (CR 608.2b).
/// </summary>
public class EyeblightsEndingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EyeblightsEnding_IsInstant_AtCost2B()
    {
        var card = EyeblightsEndingFactory.Create(_alice);

        card.Name.Should().Be("Eyeblight's Ending");
        card.ManaCost.Should().Be("{2}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EyeblightsEnding_IsBlack()
    {
        var card = EyeblightsEndingFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.Black,
            "Eyeblight's Ending has a {B} pip in its mana cost (CR 105)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_EyeblightsEnding()
    {
        var card = NamedCardFactory.Create("Eyeblight's Ending", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Eyeblight's Ending");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys a plain non-Elf creature
    // -----------------------------------------------------------------------

    [Fact]
    public void EyeblightsEnding_DestroysNonElfCreature()
    {
        // A generic 2/2 creature with no Elf subtype.
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Eyeblight's Ending destroys a creature that is not an Elf (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Resolution — no-op on Elf creature
    // -----------------------------------------------------------------------

    [Fact]
    public void EyeblightsEnding_ElfCreature_NotDestroyed()
    {
        var elf = NewControlledCreature(_bob, "Llanowar Elves", "{G}",
            CardSubtype.Elf);

        Resolve(elf);

        elf.Zone.Should().Be(ZoneType.Battlefield,
            "Eyeblight's Ending cannot destroy an Elf (CR 608.2b — illegal target)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(elf);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(elf);
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void EyeblightsEnding_TargetNotOnBattlefield_DoesNothing()
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
        var def = EyeblightsEndingFactory.BuildDefinition(targetResolver: t => t);
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
