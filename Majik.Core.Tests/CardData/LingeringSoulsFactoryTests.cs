using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="LingeringSoulsFactory"/> (Innistrad, {2}{B}).
///
/// Covers:
/// - Identity (Sorcery {2}{B}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Resolve effect creates exactly two 1/1 white Spirit creature tokens
///   with Flying.
/// - Flashback alt-cost surfaced as {1}{W} via the oracle binder
///   (<see cref="FlashbackOracleParser"/>).
/// - Flashback cast from graveyard: same resolve effect; cost
///   <c>OnResolved</c> exiles the card from graveyard (CR 702.34b).
/// </summary>
public class LingeringSoulsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LingeringSouls_Identity()
    {
        var ls = LingeringSoulsFactory.Create(_alice);

        ls.Name.Should().Be("Lingering Souls");
        ls.ManaCost.Should().Be("{2}{B}");
        ls.HasType(CardType.Sorcery).Should().BeTrue();
        ls.Owner.Should().BeSameAs(_alice);
        ls.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LingeringSouls_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Lingering Souls", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Lingering Souls");
        card.ManaCost.Should().Be("{2}{B}");
    }

    [Fact]
    public void Resolve_CreatesTwo_WhiteSpiritTokens_WithFlying()
    {
        var effects = LingeringSoulsFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        var spirits = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Spirit")
            .ToList();

        spirits.Should().HaveCount(2);
        spirits.Should().AllSatisfy(s =>
        {
            s.BasePower.Should().Be(1);
            s.BaseToughness.Should().Be(1);
            s.HasType(CardType.Creature).Should().BeTrue();
            s.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
            s.Abilities.OfType<KeywordAbility>().Should().Contain(
                k => k.Keyword == "Flying");
            Majik.Core.Cards.CardColors.GetColors(s).Should().BeEquivalentTo(
                new[] { ManaColor.White });
            s.Controller.Should().BeSameAs(_alice);
        });
    }

    [Fact]
    public void FlashbackCost_ParsedFromOracle_Is1W()
    {
        var fb = LingeringSoulsFactory.BuildFlashbackCost();

        fb.AlternativeManaCost.Should().Be(ManaCost.Parse("1W"));
        fb.Description.Should().Contain("Flashback");
    }

    [Fact]
    public void FlashbackCost_CanCast_FromGraveyard_NotFromHand()
    {
        var ls = LingeringSoulsFactory.Create(_alice);
        var fb = LingeringSoulsFactory.BuildFlashbackCost();

        ls.SetZone(ZoneType.Hand);
        fb.CanCastFor(ls, _alice).Should().BeFalse(
            "Flashback is only castable from graveyard (CR 702.34a)");

        _alice.Zones.Graveyard.AddCard(ls);
        ls.SetZone(ZoneType.Graveyard);
        fb.CanCastFor(ls, _alice).Should().BeTrue();
    }

    [Fact]
    public void FlashbackCast_FromGraveyard_AppliesResolveEffect_ThenExiles()
    {
        var ls = LingeringSoulsFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(ls);
        ls.SetZone(ZoneType.Graveyard);

        var fb = LingeringSoulsFactory.BuildFlashbackCost();
        fb.CanCastFor(ls, _alice).Should().BeTrue();

        // Run the printed resolve effect — same effect for printed cast and
        // flashback cast (CR 702.34a; the cost is the only difference).
        foreach (var e in LingeringSoulsFactory.BuildResolveEffect(_alice)) e.Execute();

        // Then flashback's post-resolve hook fires — card exiles from
        // graveyard (CR 702.34b).
        fb.OnResolved(ls, _alice);

        ls.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(ls);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(ls);

        // Resolve still landed the 2 Spirit tokens.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Spirit")
            .Should().HaveCount(2);
    }
}
