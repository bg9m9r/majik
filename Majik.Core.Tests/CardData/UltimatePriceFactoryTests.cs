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
/// Tests for Ultimate Price (Magic Origins, {1}{B}, Instant).
///
/// Oracle text: "Destroy target monocolored creature."
///
/// Covers:
///   - Card identity (Instant, {1}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a monocolored creature (e.g. {G} 2/2 → graveyard, CR 701.7).
///   - Multicolor creature ({G}{W}) → no-op at resolution (CR 105 + CR 608.2b).
///   - Colorless artifact creature ({2}) → no-op (0 colors ≠ 1, CR 608.2b).
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class UltimatePriceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void UltimatePrice_IsInstant_AtCost1B()
    {
        var card = UltimatePriceFactory.Create(_alice);

        card.Name.Should().Be("Ultimate Price");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_UltimatePrice()
    {
        var card = NamedCardFactory.Create("Ultimate Price", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Ultimate Price");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys monocolored creature
    // -----------------------------------------------------------------------

    [Fact]
    public void UltimatePrice_DestroysMonocolorGreenCreature()
    {
        // Mono-green creature — exactly 1 colour, legal target.
        var elf = NewControlledCreature(_bob, "Llanowar Elves", "{G}");

        Resolve(elf);

        elf.Zone.Should().Be(ZoneType.Graveyard,
            "Ultimate Price destroys a monocolored creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(elf);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(elf);
    }

    [Fact]
    public void UltimatePrice_DestroysMonocolorBlackCreature()
    {
        // Mono-black creature — also monocolored, so it IS a legal target
        // (unlike Doom Blade, Ultimate Price can target black creatures).
        var imp = NewControlledCreature(_bob, "Putrid Imp", "{B}");

        Resolve(imp);

        imp.Zone.Should().Be(ZoneType.Graveyard,
            "Ultimate Price can destroy mono-black creatures (1 colour = monocolored)");
    }

    // -----------------------------------------------------------------------
    // Resolution — multicolor creature (≥2 colours) → no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void UltimatePrice_MulticolorCreature_NotDestroyed()
    {
        // GW creature — 2 colours, NOT monocolored.
        var knight = NewControlledCreature(_bob, "Knight of the Reliquary", "{1}{G}{W}");

        Resolve(knight);

        knight.Zone.Should().Be(ZoneType.Battlefield,
            "A multicolor creature has ≥2 colours, so it is not monocolored (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(knight);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(knight);
    }

    [Fact]
    public void UltimatePrice_MulticolorBRCreature_NotDestroyed()
    {
        // BR creature — 2 colours, NOT monocolored.
        var demon = NewControlledCreature(_bob, "Blood Crypt Demon", "{B}{R}");

        Resolve(demon);

        demon.Zone.Should().Be(ZoneType.Battlefield,
            "A BR creature has 2 colours, so it is not monocolored (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Resolution — colorless creature (0 colours) → no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void UltimatePrice_ColorlessCreature_NotDestroyed()
    {
        // Colorless artifact creature — 0 colours, NOT monocolored.
        var golem = NewControlledCreature(_bob, "Memnite", "{0}");

        Resolve(golem);

        golem.Zone.Should().Be(ZoneType.Battlefield,
            "A colorless creature has 0 colours, so it is not monocolored (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(golem);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(golem);
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target → no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void UltimatePrice_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        // Zone unchanged — no double-move into graveyard / exception.
        // CR 608.2b — illegal target → effect does nothing.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = UltimatePriceFactory.BuildDefinition(targetResolver: t => t);
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
        var c = new Creature(name, cost, 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
