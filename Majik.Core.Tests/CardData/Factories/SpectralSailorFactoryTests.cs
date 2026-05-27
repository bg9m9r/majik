using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Spectral Sailor (Core Set 2020, {U}).
///
/// Covers:
///   - Card shape: name, type, Spirit + Pirate subtypes, P/T 1/1, mana
///     cost, owner / controller wiring.
///   - Flash + Flying keyword markers.
///   - Activated ability {3}{U}: Draw a card — cost composition + draw
///     resolution (Library → Hand zone-move, empty-library safety).
///   - NamedCardFactory dispatch routes the card name to this factory.
/// </summary>
public class SpectralSailorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SpectralSailor_IsCreature_SpiritPirate_1_1_AtCostU()
    {
        var c = SpectralSailorFactory.Create(_alice);

        c.Name.Should().Be("Spectral Sailor");
        c.ManaCost.Should().Be("{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpectralSailor_HasFlashAndFlying()
    {
        var c = SpectralSailorFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Flying");
    }

    [Fact]
    public void SpectralSailor_HasActivatedDrawAbility_At3U()
    {
        var c = SpectralSailorFactory.Create(_alice);

        var activated = c.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().ContainSingle("only the {3}{U}: Draw a card activated ability is wired");

        var ability = activated.Single();
        ability.Costs.Should().ContainSingle();
        var manaCost = ability.Costs.Single().Should().BeOfType<ManaCostCost>().Subject;
        // ManaCostCost.Cost is the parsed ManaCost value object — assert
        // the structural shape (generic 3, blue 1) rather than the printed
        // string (Description currently flattens to "3U" without braces).
        manaCost.Cost.Generic.Should().Be(3);
        manaCost.Cost.Blue.Should().Be(1);
        manaCost.Cost.TotalValue.Should().Be(4);
    }

    [Fact]
    public void SpectralSailor_DrawAbility_MovesTopOfLibraryToHand()
    {
        var top = new Sorcery("Top Card", "{1}");
        top.SetOwner(_alice);
        top.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(top);

        var c = SpectralSailorFactory.Create(_alice);
        var draw = c.Abilities.OfType<ActivatedAbility>().Single();

        // Execute the effect body directly (the activator path is exercised
        // by the engine-wide cost / target tests — here we want pure
        // resolution semantics).
        foreach (var e in draw.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void SpectralSailor_DrawAbility_EmptyLibrary_IsNoOp()
    {
        // No cards in Alice's library.
        var c = SpectralSailorFactory.Create(_alice);
        var draw = c.Abilities.OfType<ActivatedAbility>().Single();

        var act = () =>
        {
            foreach (var e in draw.Effects) e.Execute();
        };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void SpectralSailor_NamedCardFactory_Dispatch()
    {
        var card = NamedCardFactory.Create("Spectral Sailor", _alice);

        card.Should().NotBeNull();
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Spectral Sailor");
        ((Creature)card).Power.Should().Be(1);
        ((Creature)card).Toughness.Should().Be(1);
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        card.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
    }
}
