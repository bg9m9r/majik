using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Anthem of Champions (Modern Horizons 3, {G}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Creatures you control get +1/+1."
///
/// Covers (the card's UNIQUE behaviour + one identity assert):
///   - Identity: name, Enchantment type, mana cost {G}{W}.
///   - Anthem: +1/+1 to EVERY creature the controller controls, regardless of
///     colour (the colour-agnostic Glorious Anthem shape — Honor of the Pure
///     without the colour gate).
///   - Opponent's creatures are NOT buffed ("you control").
///   - LTB lifts the bonus (IsActive gate).
/// (CardFactoryContractTests already asserts NamedCardFactory dispatch +
/// well-formedness, so no dispatch test here.)
/// </summary>
[Trait("Color", "M")]
public class AnthemOfChampionsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void AnthemOfChampions_IsEnchantment_AtCostGW()
    {
        var c = AnthemOfChampionsFactory.Create(_alice);

        c.Name.Should().Be("Anthem of Champions");
        c.ManaCost.Should().Be("{G}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AnthemOfChampions_BuffsAllControllersCreatures_RegardlessOfColor()
    {
        var svc = new ContinuousEffectsService();

        // A non-white creature must still be buffed — unlike Honor of the Pure,
        // there is no colour gate.
        var greenBear = MakeCreature("Bear", _alice, svc, 2, 2, "G");
        var redGoblin = MakeCreature("Goblin", _alice, svc, 1, 1, "R");

        var anthem = AnthemOfChampionsFactory.Create(_alice, svc);
        anthem.SetZone(ZoneType.Battlefield);
        anthem.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(anthem);

        greenBear.GetPower().Should().Be(3,
            "Anthem of Champions grants +1/+1 to every creature the controller controls");
        greenBear.GetToughness().Should().Be(3);
        redGoblin.GetPower().Should().Be(2);
        redGoblin.GetToughness().Should().Be(2);
    }

    [Fact]
    public void AnthemOfChampions_DoesNotBuffOpponentCreatures()
    {
        var svc = new ContinuousEffectsService();

        var bobBear = MakeCreature("Bob's Bear", _bob, svc, 2, 2, "G");

        var anthem = AnthemOfChampionsFactory.Create(_alice, svc);
        anthem.SetZone(ZoneType.Battlefield);
        anthem.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(anthem);

        bobBear.GetPower().Should().Be(2,
            "Anthem of Champions keys on 'you control' — opponent's creatures are unaffected");
        bobBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void AnthemOfChampions_LeavingBattlefield_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var bear = MakeCreature("Bear", _alice, svc, 2, 2, "G");

        var anthem = AnthemOfChampionsFactory.Create(_alice, svc);
        anthem.SetZone(ZoneType.Battlefield);
        anthem.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(anthem);

        bear.GetPower().Should().Be(3);

        // Anthem of Champions leaves the battlefield — IsActive gate flips false.
        anthem.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(anthem);
        _alice.Zones.Graveyard.AddCard(anthem);

        bear.GetPower().Should().Be(2,
            "the anthem's IsActive gates on the source being on the battlefield");
        bear.GetToughness().Should().Be(2);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Creature MakeCreature(string name, Player owner,
        ContinuousEffectsService svc, int p, int t, string manaColorPip)
    {
        var c = new Creature(name, $"{{{manaColorPip}}}", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        c.ActiveEffects = svc;
        return c;
    }
}
