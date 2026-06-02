using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Nighthawk Scavenger — Creature — Vampire Rogue
/// {1}{B}{B}, 1+*/3.
///   "Flying, deathtouch, lifelink
///    Nighthawk Scavenger's power is equal to 1 plus the number of card
///    types among cards in your opponents' graveyards."
///
/// Combines two existing patterns:
///   * Keyword shell (Flying / Deathtouch / Lifelink) — cf. Vampire Nighthawk.
///   * Layer 7a characteristic-defining power (CR 604.3 / 613.2) via
///     <see cref="CdaPowerToughnessEffect"/> — cf. Tarmogoyf, but the count
///     is restricted to OPPONENTS' graveyards and the base is 1, and the
///     toughness is the fixed printed 3 (not a CDA).
/// </summary>
[Trait("Color", "B")]
public class NighthawkScavengerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;

    public NighthawkScavengerTests()
    {
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
    }

    // Nighthawk Scavenger counts only the controller's OPPONENTS' graveyards.
    // For Alice's copy, that is Bob's graveyard.
    private Func<IEnumerable<ICard>> BobGraveyard => () => _bob.Zones.Graveyard.GetCards();

    private Creature WireScavenger(Player owner)
    {
        var c = NighthawkScavengerFactory.Create(owner, _effects, _bus, BobGraveyard);
        c.ActiveEffects = _effects;
        return c;
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void NighthawkScavenger_Identity()
    {
        var c = NighthawkScavengerFactory.Create(_alice);

        c.Name.Should().Be("Nighthawk Scavenger");
        c.ManaCost.Should().Be("{1}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NighthawkScavenger_IsBlack()
    {
        var c = NighthawkScavengerFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Black,
            "Nighthawk Scavenger has {B}{B} pips in its mana cost");
    }

    [Fact]
    public void NighthawkScavenger_ManaValueIsThree()
    {
        var c = NighthawkScavengerFactory.Create(_alice);

        // {1}{B}{B} → generic 1 + two coloured pips = mana value 3 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_NighthawkScavenger()
    {
        var c = NamedCardFactory.Create("Nighthawk Scavenger", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Nighthawk Scavenger");
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Keyword shell (CR 702.9 / 702.2 / 702.15)
    // -----------------------------------------------------------------------

    [Fact]
    public void NighthawkScavenger_HasFlyingDeathtouchLifelink()
    {
        var c = NighthawkScavengerFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying", "CR 702.9");
        keywords.Should().Contain("Deathtouch", "CR 702.2");
        keywords.Should().Contain("Lifelink", "CR 702.15");
        keywords.Should().HaveCount(3, "Flying, Deathtouch, Lifelink are the only printed keywords");
    }

    [Fact]
    public void NighthawkScavenger_NoTriggeredOrActivatedAbilities()
    {
        var c = NighthawkScavengerFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Layer 7a — CDA power = 1 + opponents'-graveyard card-type count
    // -----------------------------------------------------------------------

    [Fact]
    public void NighthawkScavenger_NoOpponentGraveyardCards_Is_1_3()
    {
        var c = WireScavenger(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        // 1 + 0 card types = power 1; toughness fixed at 3.
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(3);
    }

    [Fact]
    public void NighthawkScavenger_OneInstantInOpponentGraveyard_Is_2_3()
    {
        var c = WireScavenger(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        var bolt = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        bolt.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(bolt);

        // 1 + 1 type = power 2; toughness stays 3.
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(3);
    }

    [Fact]
    public void NighthawkScavenger_FiveCardTypesInOpponentGraveyard_Is_6_3()
    {
        var c = WireScavenger(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        var creatureCard = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        var instantCard = new Card("Counterspell", "UU", new[] { CardType.Instant });
        var sorceryCard = new Card("Wrath of God", "2WW", new[] { CardType.Sorcery });
        var artifactCard = new Card("Sol Ring", "1", new[] { CardType.Artifact });
        var enchantmentCard = new Card("Pacifism", "1W", new[] { CardType.Enchantment });
        // Duplicate instant — distinct-type count must not double-count.
        var duplicateInstant = new Card("Bolt", "R", new[] { CardType.Instant });

        foreach (var card in new[]
                 { creatureCard, instantCard, sorceryCard, artifactCard, enchantmentCard, duplicateInstant })
        {
            card.SetOwner(_bob);
            _bob.Zones.Graveyard.AddCard(card);
        }

        // 1 + 5 distinct types = power 6; toughness stays 3.
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(3);
    }

    [Fact]
    public void NighthawkScavenger_IgnoresControllersOwnGraveyard()
    {
        var c = WireScavenger(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Cards in Alice's OWN graveyard must NOT count — only opponents'.
        var mine = new Card("My Sorcery", "2", new[] { CardType.Sorcery });
        mine.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(mine);

        c.Power.Should().Be(1, "the controller's own graveyard does not count");
        c.Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Layer ordering — 7a sets, 7c stacks on top
    // -----------------------------------------------------------------------

    [Fact]
    public void NighthawkScavenger_PlusOneCounter_Stacks_OnTopOf_Cda()
    {
        var c = WireScavenger(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        // One creature in opponent's graveyard → CDA power = 2 (1+1).
        var dead = new Card("Bear", "1G", new[] { CardType.Creature });
        dead.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(dead);

        // +1/+1 counter (CR 613.7 postlude) runs after 7a.
        c.Counters.Add(CounterType.PlusOnePlusOne);

        c.Power.Should().Be(3);
        c.Toughness.Should().Be(4);
    }
}
