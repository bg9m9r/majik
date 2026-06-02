using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="NettleDroneFactory"/> (Oath of the Gatewatch,
/// {2}{R}).
///
/// Creature — Eldrazi Drone 3/1 (colorless — Devoid). Oracle text (verified
/// against Scryfall):
///   "Devoid (This card has no color.)
///    {T}: This creature deals 1 damage to each opponent.
///    Whenever you cast a colorless spell, untap this creature."
///
/// Covers:
///   - Identity (Eldrazi Drone 3/1 at {2}{R}, colorless via Devoid).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Devoid keyword marker + colorlessness despite the {R} pip.
///   - {T}: 1 damage to each opponent (resolver-injected burn).
///   - Untap-on-colorless-spell-cast trigger attached structurally.
/// </summary>
[Trait("Color", "C")]
public class NettleDroneFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void NettleDrone_Identity()
    {
        var c = NettleDroneFactory.Create(_alice);

        c.Name.Should().Be("Nettle Drone");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // CR 702.114 — Devoid. The card is colorless despite the {R} pip.
        CardColors.GetColors(c).Should().BeEmpty(
            "Devoid makes Nettle Drone colorless regardless of the {R} pip");
    }

    [Fact]
    public void NettleDrone_DispatchesThroughNamedFactory()
    {
        var card = NamedCardFactory.Create("Nettle Drone", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Nettle Drone");
    }

    [Fact]
    public void NettleDrone_HasDevoidKeywordMarker()
    {
        var c = NettleDroneFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == NettleDroneFactory.DevoidKeyword,
                "Devoid is attached as a keyword marker for ability-scan discoverability");
    }

    [Fact]
    public void NettleDrone_HasTapBurnActivatedAbility_AndUntapTrigger()
    {
        var c = NettleDroneFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>()
            .Should().HaveCount(1, "the {T}: deal 1 to each opponent ability");
        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the untap-on-colorless-cast trigger");
    }

    [Fact]
    public void TapBurn_DealsOneDamageToEachOpponent()
    {
        var card = NettleDroneFactory.Create(
            _alice, triggers: null, opponentResolver: () => new[] { _bob });

        var burn = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in burn.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19,
            "{T} deals 1 damage to each opponent (CR 119 — damage is life loss)");
    }

    [Fact]
    public void TapBurn_WithoutResolver_NoOps()
    {
        var card = NettleDroneFactory.Create(
            _alice, triggers: null, opponentResolver: null);

        var burn = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in burn.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no opponent resolver → burn half no-ops");
    }

    [Fact]
    public void UntapTrigger_WatchesSpellCastEvent()
    {
        var card = NettleDroneFactory.Create(_alice);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition.EventType
            .Should().Be(typeof(Majik.Core.Domain.DomainEvents.SpellCastEvent),
                "the untap clause triggers on casting a colorless spell");
    }
}
