using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="KaheeraTheOrphanguardFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, Cat + Beast subtypes, 3/2,
///   Legendary, owner/controller).
/// - NamedCardFactory dispatch.
/// - Printed Vigilance keyword (CR 702.20).
/// - Multi-subtype LordStaticEffect: other Cat / Elemental / Nightmare /
///   Dinosaur / Beast creatures controller controls get +1/+1.
/// - Non-listed creature (e.g. Goblin) NOT pumped.
/// - Opponent's eligible creature NOT pumped.
/// - Kaheera doesn't self-pump despite being a Cat Beast (includeSelf:
///   false).
/// - LTB lifts the bonus.
/// - Two Kaheeras stack.
/// - Companion deck-construction predicate accepts / rejects starting
///   decks per CR 702.139.
/// </summary>
public class KaheeraTheOrphanguardTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Kaheera_Identity()
    {
        var c = KaheeraTheOrphanguardFactory.Create(_alice);

        c.Name.Should().Be("Kaheera, the Orphanguard");
        c.ManaCost.Should().Be("{1}{G}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Kaheera_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Kaheera, the Orphanguard", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Kaheera, the Orphanguard");
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
    }

    [Fact]
    public void Kaheera_HasPrintedVigilance()
    {
        var c = KaheeraTheOrphanguardFactory.Create(_alice);
        c.Zone = ZoneType.Battlefield;

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Vigilance",
            "CR 702.20 — Vigilance is the printed keyword on Kaheera.");
    }

    [Theory]
    [InlineData(CardSubtype.Cat)]
    [InlineData(CardSubtype.Elemental)]
    [InlineData(CardSubtype.Nightmare)]
    [InlineData(CardSubtype.Dinosaur)]
    [InlineData(CardSubtype.Beast)]
    public void Kaheera_BuffsEligibleSubtype_Plus1Plus1(CardSubtype subtype)
    {
        var svc = new ContinuousEffectsService();

        var other = new Creature("Test Creature", "1G", 2, 2,
            subtypes: new[] { subtype })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var kaheera = KaheeraTheOrphanguardFactory.Create(_alice, svc);
        kaheera.Zone = ZoneType.Battlefield;
        kaheera.ActiveEffects = svc;

        other.GetPower().Should().Be(3,
            $"a {subtype} creature is on the eligible list (2 → 3 power).");
        other.GetToughness().Should().Be(3);
    }

    [Fact]
    public void Kaheera_DoesNotPump_NonEligibleSubtype()
    {
        var svc = new ContinuousEffectsService();

        var goblin = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var kaheera = KaheeraTheOrphanguardFactory.Create(_alice, svc);
        kaheera.Zone = ZoneType.Battlefield;
        kaheera.ActiveEffects = svc;

        goblin.GetPower().Should().Be(1,
            "Kaheera only buffs Cat / Elemental / Nightmare / Dinosaur / Beast.");
        goblin.GetToughness().Should().Be(1);
    }

    [Fact]
    public void Kaheera_DoesNotPump_OpponentCat()
    {
        var svc = new ContinuousEffectsService();

        var oppCat = new Creature("Savannah Lions", "W", 2, 1,
            subtypes: new[] { CardSubtype.Cat })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var kaheera = KaheeraTheOrphanguardFactory.Create(_alice, svc);
        kaheera.Zone = ZoneType.Battlefield;
        kaheera.ActiveEffects = svc;

        oppCat.GetPower().Should().Be(2,
            "Kaheera's static is scoped to its controller's creatures (CR 109.5 — 'you').");
        oppCat.GetToughness().Should().Be(1);
    }

    [Fact]
    public void Kaheera_DoesNotSelfPump()
    {
        // includeSelf: false — Kaheera is itself a Cat Beast but its
        // "Other ... creatures" clause excludes self.
        var svc = new ContinuousEffectsService();

        var kaheera = KaheeraTheOrphanguardFactory.Create(_alice, svc);
        kaheera.Zone = ZoneType.Battlefield;
        kaheera.ActiveEffects = svc;

        kaheera.GetPower().Should().Be(3, "Kaheera doesn't self-buff via 'Other'.");
        kaheera.GetToughness().Should().Be(2);
    }

    [Fact]
    public void Kaheera_LTB_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var cat = new Creature("Savannah Lions", "W", 2, 1,
            subtypes: new[] { CardSubtype.Cat })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var kaheera = KaheeraTheOrphanguardFactory.Create(_alice, svc);
        kaheera.Zone = ZoneType.Battlefield;
        kaheera.ActiveEffects = svc;

        cat.GetPower().Should().Be(3);
        cat.GetToughness().Should().Be(2);

        kaheera.SetZone(ZoneType.Graveyard);

        cat.GetPower().Should().Be(2, "bonus lifts on LTB");
        cat.GetToughness().Should().Be(1);
    }

    [Fact]
    public void TwoKaheeras_StackPlus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var beast = new Creature("Krosan Tusker", "4GG", 6, 5,
            subtypes: new[] { CardSubtype.Beast })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var k1 = KaheeraTheOrphanguardFactory.Create(_alice, svc);
        k1.Zone = ZoneType.Battlefield;
        k1.ActiveEffects = svc;

        var k2 = KaheeraTheOrphanguardFactory.Create(_alice, svc);
        k2.Zone = ZoneType.Battlefield;
        k2.ActiveEffects = svc;

        beast.GetPower().Should().Be(8,
            "two Kaheeras stack +1/+1 — 6 base + 2 from two lords = 8.");
        beast.GetToughness().Should().Be(7);

        // Each Kaheera (Cat Beast) gets buffed by the OTHER one.
        k1.GetPower().Should().Be(4, "the other Kaheera's static applies (it's a Cat Beast).");
        k2.GetPower().Should().Be(4);
    }

    [Fact]
    public void Kaheera_CompanionRestriction_AllEligibleCreatures_Passes()
    {
        var deck = new List<ICard>
        {
            new Creature("Savannah Lions", "W", 2, 1, subtypes: new[] { CardSubtype.Cat }),
            new Creature("Lightning Elemental", "3R", 4, 1,
                subtypes: new[] { CardSubtype.Elemental }),
            new Creature("Nightmare", "5B", 0, 0,
                subtypes: new[] { CardSubtype.Nightmare }),
            new Creature("Ripjaw Raptor", "2GG", 4, 5,
                subtypes: new[] { CardSubtype.Dinosaur }),
            new Creature("Krosan Tusker", "4GG", 6, 5,
                subtypes: new[] { CardSubtype.Beast }),
        };

        KaheeraTheOrphanguardFactory.CompanionRestriction.IsSatisfiedBy(deck)
            .Should().BeTrue();
    }

    [Fact]
    public void Kaheera_CompanionRestriction_OneBadCreature_Fails()
    {
        var deck = new List<ICard>
        {
            new Creature("Savannah Lions", "W", 2, 1, subtypes: new[] { CardSubtype.Cat }),
            // Goblin is NOT in the eligible list — should fail.
            new Creature("Mogg Fanatic", "R", 1, 1, subtypes: new[] { CardSubtype.Goblin }),
        };

        KaheeraTheOrphanguardFactory.CompanionRestriction.IsSatisfiedBy(deck)
            .Should().BeFalse();
    }

    [Fact]
    public void Kaheera_CompanionRestriction_NonCreaturesUnconstrained()
    {
        // "Each CREATURE card" — non-creatures don't have to match.
        var deck = new List<ICard>
        {
            new Creature("Savannah Lions", "W", 2, 1, subtypes: new[] { CardSubtype.Cat }),
            // Plains is a Land, not a Creature — unconstrained.
            new Land("Plains", supertypes: new[] { CardSupertype.Basic },
                subtypes: new[] { CardSubtype.Plains }),
        };

        KaheeraTheOrphanguardFactory.CompanionRestriction.IsSatisfiedBy(deck)
            .Should().BeTrue();
    }
}
