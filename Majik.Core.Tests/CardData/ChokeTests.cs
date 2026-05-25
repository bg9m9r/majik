using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Choke (Stronghold, {2}{G}, Enchantment).
///
/// Oracle: "Islands don't untap during their controllers' untap steps."
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///
/// The printed "Islands don't untap" static is NOT covered here — see
/// <see cref="ChokeFactory"/>'s class xmldoc for the deferred-rider
/// rationale (no untap-step filter / "doesn't untap" primitive in the
/// engine yet; same gap <see cref="ManaVaultFactory"/> documents).
/// Tests for the static body land alongside the engine surface that
/// implements it.
/// </summary>
public class ChokeTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Choke_IsEnchantment_At2G()
    {
        var c = ChokeFactory.Create(_alice);

        c.Name.Should().Be("Choke");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Choke()
    {
        var card = NamedCardFactory.Create("Choke", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Choke");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{G}");
        card.Owner.Should().BeSameAs(_alice);
    }
}
