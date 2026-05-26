using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AnointedProcessionFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, owner/controller wiring).
/// - <see cref="NamedCardFactory"/> dispatch.
///
/// Token-creation doubling itself is deferred until a
/// <c>TokenCreationIntent</c> primitive exists (see factory xmldoc); only
/// card-shape coverage lives here.
/// </summary>
public class AnointedProcessionTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AnointedProcession_Identity()
    {
        var card = AnointedProcessionFactory.Create(_alice);

        card.Name.Should().Be("Anointed Procession");
        card.ManaCost.Should().Be("{3}{W}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeFalse(
            "Anointed Procession is a pure Enchantment, not a creature.");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AnointedProcession_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Anointed Procession", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Anointed Procession");
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }
}
