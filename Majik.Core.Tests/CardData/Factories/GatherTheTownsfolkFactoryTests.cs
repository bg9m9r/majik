using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GatherTheTownsfolkFactory"/>.
///
/// Oracle text ({1}{W} Sorcery, verified against Scryfall):
///   "Create two 1/1 white Human creature tokens.
///    Fateful hour — If you have 5 or less life, create five of those tokens
///    instead."
///
/// Covers:
/// - Card identity (Sorcery, {1}{W}, white, CMC 2, owner/controller).
/// - SpellDefinition shape — no modes, no X, no target requests.
/// - Default branch: two 1/1 white Human tokens when life > 5 (CR 111 / 111.4).
/// - Fateful-hour branch: five tokens "instead" when life <= 5 (CR 119.4) —
///   the boundary at exactly 5 life fires the fateful-hour branch.
/// - "Instead" is exclusive: two OR five, never seven (CR 119.4 replacement
///   wording — the larger count replaces the smaller).
/// </summary>
[Trait("Color", "W")]
public class GatherTheTownsfolkFactoryTests
{
    private static Player NewPlayer(string name, int life = 20) => new(name, life);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GatherTheTownsfolk_HasSorceryShape_White_AtCost1W()
    {
        var alice = NewPlayer("Alice");
        var card = GatherTheTownsfolkFactory.Create(alice);

        card.Name.Should().Be("Gather the Townsfolk");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GatherTheTownsfolk_SpellDefinition_HasNoTargets_NoModes_NoX()
    {
        var alice = NewPlayer("Alice");

        var def = GatherTheTownsfolkFactory.BuildSpellDefinition(alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Default branch — CR 111 / 111.4
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultBranch_CreatesTwoWhiteHumans_WhenLifeAbove5()
    {
        var alice = NewPlayer("Alice", life: 20);

        GatherTheTownsfolkFactory.IsFatefulHour(alice).Should().BeFalse();

        GatherTheTownsfolkFactory.BuildResolveEffect(alice).Single().Execute();

        var tokens = alice.Zones.Battlefield.GetCards().Cast<Creature>().ToList();
        tokens.Should().HaveCount(2, "creates two Human tokens when life > 5");
        foreach (var token in tokens)
        {
            token.Name.Should().Be("Human");
            token.IsToken.Should().BeTrue();
            token.BasePower.Should().Be(1);
            token.BaseToughness.Should().Be(1);
            token.HasSubtype(CardSubtype.Human).Should().BeTrue(
                "CR 111.4 — Human creature subtype");
            token.Controller.Should().BeSameAs(alice);
            CardColors.GetColors(token).Should().Contain(ManaColor.White,
                "CR 111.4 — the token is explicitly white");
        }
    }

    // -----------------------------------------------------------------------
    // Fateful-hour branch — CR 119.4
    // -----------------------------------------------------------------------

    [Fact]
    public void FatefulHour_CreatesFiveTokens_WhenLifeIs5()
    {
        var alice = NewPlayer("Alice", life: 5);

        GatherTheTownsfolkFactory.IsFatefulHour(alice).Should().BeTrue(
            "CR 119.4 — '5 or less life' includes exactly 5");

        GatherTheTownsfolkFactory.BuildResolveEffect(alice).Single().Execute();

        alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count(c => c.IsToken)
            .Should().Be(5, "fateful hour creates five 'instead' of two");
    }

    [Fact]
    public void FatefulHour_CreatesFiveTokens_WhenLifeBelow5()
    {
        var alice = NewPlayer("Alice", life: 1);

        GatherTheTownsfolkFactory.IsFatefulHour(alice).Should().BeTrue();

        GatherTheTownsfolkFactory.BuildResolveEffect(alice).Single().Execute();

        var tokens = alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).ToList();
        tokens.Should().HaveCount(5,
            "fateful hour creates five 'instead' of two — never seven (CR 119.4)");
        foreach (var token in tokens)
        {
            token.Name.Should().Be("Human");
            token.HasSubtype(CardSubtype.Human).Should().BeTrue();
            CardColors.GetColors(token).Should().Contain(ManaColor.White);
        }
    }

    [Fact]
    public void DefaultBranch_AtExactly6Life_CreatesTwo()
    {
        var alice = NewPlayer("Alice", life: 6);

        GatherTheTownsfolkFactory.IsFatefulHour(alice).Should().BeFalse(
            "6 life is above the 5-or-less threshold");

        GatherTheTownsfolkFactory.BuildResolveEffect(alice).Single().Execute();

        alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count(c => c.IsToken)
            .Should().Be(2, "not fateful hour at 6 life");
    }
}
