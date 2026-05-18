using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

public class CyclingTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Cycle_DiscardsSelf_DrawsOne_WhenManaPaid()
    {
        var card = new Card("Brainstorm", "1U");
        card.Owner = _alice; card.Zone = ZoneType.Hand;
        _alice.Zones.Hand.AddCard(card);

        var topOfLibrary = new Card("Top", "1");
        topOfLibrary.Owner = _alice; topOfLibrary.Zone = ZoneType.Library;
        _alice.Zones.Library.AddCard(topOfLibrary);

        _alice.AddManaToPool(ManaCost.Parse("1U"));
        var cycle = new CyclingAbility(card, ManaCost.Parse("1U"));

        cycle.Activate(_alice).Should().BeTrue();

        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Hand.GetCards().Should().Contain(topOfLibrary);
    }

    [Fact]
    public void Cycle_NotInHand_Fails()
    {
        var card = new Card("Brainstorm", "1U") { Owner = _alice };
        card.Zone = ZoneType.Library;
        _alice.Zones.Library.AddCard(card);
        _alice.AddManaToPool(ManaCost.Parse("1U"));

        new CyclingAbility(card, ManaCost.Parse("1U"))
            .Activate(_alice).Should().BeFalse();
    }

    [Fact]
    public void Cycle_InsufficientMana_Fails()
    {
        var card = new Card("Brainstorm", "1U") { Owner = _alice };
        card.Zone = ZoneType.Hand;
        _alice.Zones.Hand.AddCard(card);

        new CyclingAbility(card, ManaCost.Parse("1U"))
            .Activate(_alice).Should().BeFalse();

        card.Zone.Should().Be(ZoneType.Hand); // not discarded
    }

    [Fact]
    public void Cycle_EmptyLibrary_FlagsDeckOut()
    {
        var card = new Card("Brainstorm", "1U") { Owner = _alice };
        card.Zone = ZoneType.Hand;
        _alice.Zones.Hand.AddCard(card);
        _alice.AddManaToPool(ManaCost.Parse("1U"));

        new CyclingAbility(card, ManaCost.Parse("1U"))
            .Activate(_alice).Should().BeTrue();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }
}
