using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for Sol Ring (Limited Edition Alpha, {1}).
///
/// Oracle text:
///   "{T}: Add {C}{C}."
///
/// CR 605 — mana ability (no stack, no targets). CR 107.4c — engine
/// buckets {C} into the generic slot via <see cref="ValueObjects.ManaCost.Parse"/>.
/// </summary>
public class SolRingTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SolRing_Identity()
    {
        var ring = SolRingFactory.Create(_alice);

        ring.Name.Should().Be("Sol Ring");
        ring.ManaCost.Should().Be("{1}");
        ring.HasType(CardType.Artifact).Should().BeTrue();
        ring.Supertypes.Should().BeEmpty("the canonical Modern-legal Sol Ring printing is plain Artifact (not Legendary)");
        ring.Owner.Should().BeSameAs(_alice);
        ring.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SolRing_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Sol Ring", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Sol Ring");
        card.ManaCost.Should().Be("{1}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "Sol Ring prints exactly one mana ability — {T}: Add {C}{C}");
    }

    [Fact]
    public void SolRing_Tap_AddsTwoColorless()
    {
        var ring = SolRingFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ring);
        ring.SetZone(ZoneType.Battlefield);

        var ability = ring.Abilities.OfType<ManaAbility>().Single();
        var produced = ability.Activate();

        // {C}{C} buckets as +2 generic per CR 107.4c (engine has no
        // dedicated colourless slot — see ManaCost.Parse).
        produced.Generic.Should().Be(2);
        produced.TotalValue.Should().Be(2);
        ring.IsTapped.Should().BeTrue("activating the tap mana ability taps the source");
    }

    [Fact]
    public void SolRing_CannotActivate_WhenTapped()
    {
        var ring = SolRingFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ring);
        ring.SetZone(ZoneType.Battlefield);

        var ability = ring.Abilities.OfType<ManaAbility>().Single();
        ability.Activate(); // first activation taps

        ability.CanActivate().Should().BeFalse("an already-tapped permanent cannot pay {T} again");
    }
}
