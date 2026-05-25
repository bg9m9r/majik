using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="TriplicateSpiritsFactory"/> (Khans of Tarkir,
/// {4}{W}).
///
/// Covers:
/// - Identity (Sorcery {4}{W}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Convoke keyword marker attached.
/// - Resolve effect creates exactly three 1/1 white Spirit creature tokens
///   with Flying.
/// - <see cref="TriplicateSpiritsFactory.BuildAdditionalCost"/> returns a
///   <see cref="ConvokeAdditionalCost"/> referencing the supplied tap
///   selection.
/// </summary>
public class TriplicateSpiritsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TriplicateSpirits_Identity()
    {
        var ts = TriplicateSpiritsFactory.Create(_alice);

        ts.Name.Should().Be("Triplicate Spirits");
        ts.ManaCost.Should().Be("{4}{W}");
        ts.HasType(CardType.Sorcery).Should().BeTrue();
        ts.Owner.Should().BeSameAs(_alice);
        ts.Controller.Should().BeSameAs(_alice);

        ts.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Convoke",
                "Convoke keyword marker is attached so shape inspectors see the keyword");
    }

    [Fact]
    public void TriplicateSpirits_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Triplicate Spirits", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Triplicate Spirits");
        card.ManaCost.Should().Be("{4}{W}");
    }

    [Fact]
    public void Resolve_CreatesThree_WhiteSpiritTokens_WithFlying()
    {
        var effects = TriplicateSpiritsFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        var spirits = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Spirit")
            .ToList();

        spirits.Should().HaveCount(3,
            "Triplicate Spirits creates exactly three Spirit tokens");
        spirits.Should().AllSatisfy(s =>
        {
            s.BasePower.Should().Be(1);
            s.BaseToughness.Should().Be(1);
            s.HasType(CardType.Creature).Should().BeTrue();
            s.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
            s.Abilities.OfType<KeywordAbility>().Should().Contain(
                k => k.Keyword == "Flying", "Spirit tokens have flying");
            Majik.Core.Cards.CardColors.GetColors(s).Should().BeEquivalentTo(
                new[] { ManaColor.White }, "Spirit tokens are white (CR 111.4)");
            s.Controller.Should().BeSameAs(_alice,
                "tokens enter under the caster's control");
        });
    }

    [Fact]
    public void BuildAdditionalCost_ReturnsConvokeCostBoundToCard()
    {
        var ts = TriplicateSpiritsFactory.Create(_alice);

        // Build a couple of dummy untapped creatures the caster controls
        // (the additional cost itself does not validate state in this test —
        // CanPay does, and we're just exercising the wrapper here).
        var c1 = new Creature("Dummy1", "{1}{W}", 1, 1);
        var c2 = new Creature("Dummy2", "{1}{W}", 2, 2);
        c1.SetOwner(_alice); c1.SetController(_alice);
        c2.SetOwner(_alice); c2.SetController(_alice);

        var cost = TriplicateSpiritsFactory.BuildAdditionalCost(ts, new[] { c1, c2 });

        cost.Should().BeOfType<ConvokeAdditionalCost>();
        cost.Source.Should().BeSameAs(ts);
        cost.Chosen.Should().HaveCount(2)
            .And.ContainInOrder(c1, c2);
        cost.ReductionAmount.Should().Be(2);
    }
}
