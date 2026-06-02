using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LyraDawnbringerFactory"/>.
///
/// Lyra Dawnbringer (Dominaria, {3}{W}{W}). Legendary Creature — Angel 5/5.
/// Oracle (verified against Scryfall):
///   "Flying
///    First strike
///    Lifelink
///    Other Angels you control get +1/+1 and have lifelink."
///
/// Coverage:
/// - Identity (name, type, Legendary supertype, Angel subtype, cost,
///   colour, mana value, P/T, owner/controller).
/// - NamedCardFactory dispatch.
/// - Flying / First strike / Lifelink keyword markers on Lyra herself.
/// - Lord static (CR 613.7c / 613.1f): other controller-Angels get +1/+1
///   AND gain Lifelink; self, opponent Angels, and non-Angels unaffected.
/// </summary>
[Trait("Color", "W")]
public class LyraDawnbringerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeAngel(Player owner, string name = "Serra Avenger")
    {
        var c = new Creature(name, "{W}{W}", 3, 3, subtypes: new[] { CardSubtype.Angel });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeNonAngel(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void LyraDawnbringer_Identity()
    {
        var c = LyraDawnbringerFactory.Create(_alice);

        c.Name.Should().Be("Lyra Dawnbringer");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.ManaCost.Should().Be("{3}{W}{W}");
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LyraDawnbringer_IsWhite()
    {
        var c = LyraDawnbringerFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Lyra Dawnbringer has {W}{W} pips in its mana cost");
    }

    [Fact]
    public void LyraDawnbringer_ManaValueIsFive()
    {
        var c = LyraDawnbringerFactory.Create(_alice);

        // {3}{W}{W} → generic 3 + two white pips = mana value 5 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(5);
    }
    // ── Keyword markers on Lyra herself ─────────────────────────────────

    [Fact]
    public void LyraDawnbringer_HasFlying()
    {
        var c = LyraDawnbringerFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue(
            "Lyra Dawnbringer prints Flying (CR 702.9).");
    }

    [Fact]
    public void LyraDawnbringer_HasFirstStrike()
    {
        var c = LyraDawnbringerFactory.Create(_alice);

        CombatAbilities.HasFirstStrike(c).Should().BeTrue(
            "Lyra Dawnbringer prints First strike (CR 702.7).");
    }

    [Fact]
    public void LyraDawnbringer_HasLifelink()
    {
        var c = LyraDawnbringerFactory.Create(_alice);

        CombatAbilities.HasLifelink(c).Should().BeTrue(
            "Lyra Dawnbringer prints Lifelink (CR 702.15).");
    }

    [Fact]
    public void LyraDawnbringer_HasThreePrintedKeywordMarkers()
    {
        var c = LyraDawnbringerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().BeEquivalentTo(new[] { "Flying", "First Strike", "Lifelink" },
                "Flying, First strike, and Lifelink are Lyra's only printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // ── Lord static: +1/+1 and Lifelink to other Angels you control ─────

    [Fact]
    public void LyraDawnbringer_BuffsOtherControllerAngel_Plus1Plus1AndLifelink()
    {
        var svc = new ContinuousEffectsService();

        var otherAngel = MakeAngel(_alice);
        otherAngel.ActiveEffects = svc;

        var lyra = LyraDawnbringerFactory.Create(_alice, svc);
        lyra.SetZone(ZoneType.Battlefield);
        lyra.ActiveEffects = svc;

        otherAngel.GetPower().Should().Be(4,
            "other Angels you control get +1/+1 (3 → 4 power, CR 613.7c).");
        otherAngel.GetToughness().Should().Be(4);
        CombatAbilities.HasLifelink(otherAngel).Should().BeTrue(
            "the anthem also grants Lifelink to other Angels you control (CR 613.1f).");
    }

    [Fact]
    public void LyraDawnbringer_DoesNotBuffItself()
    {
        var svc = new ContinuousEffectsService();

        var lyra = LyraDawnbringerFactory.Create(_alice, svc);
        lyra.SetZone(ZoneType.Battlefield);
        lyra.ActiveEffects = svc;

        lyra.GetPower().Should().Be(5,
            "printed 'Other Angels' excludes Lyra herself (CR 613.1g).");
        lyra.GetToughness().Should().Be(5);
    }

    [Fact]
    public void LyraDawnbringer_DoesNotBuffOpponentAngel()
    {
        var svc = new ContinuousEffectsService();

        var bobAngel = MakeAngel(_bob);
        bobAngel.ActiveEffects = svc;

        var lyra = LyraDawnbringerFactory.Create(_alice, svc);
        lyra.SetZone(ZoneType.Battlefield);
        lyra.ActiveEffects = svc;

        bobAngel.GetPower().Should().Be(3,
            "controller-scoped 'you control' lord — Bob's Angels are unaffected.");
        bobAngel.GetToughness().Should().Be(3);
        CombatAbilities.HasLifelink(bobAngel).Should().BeFalse(
            "Lifelink is granted only to Angels you control.");
    }

    [Fact]
    public void LyraDawnbringer_DoesNotBuffNonAngel()
    {
        var svc = new ContinuousEffectsService();

        var bears = MakeNonAngel(_alice);
        bears.ActiveEffects = svc;

        var lyra = LyraDawnbringerFactory.Create(_alice, svc);
        lyra.SetZone(ZoneType.Battlefield);
        lyra.ActiveEffects = svc;

        bears.GetPower().Should().Be(2, "the anthem only buffs Angels.");
        bears.GetToughness().Should().Be(2);
        CombatAbilities.HasLifelink(bears).Should().BeFalse();
    }
}
