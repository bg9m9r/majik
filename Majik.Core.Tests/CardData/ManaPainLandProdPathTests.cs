using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end PROD-path coverage: these four mana-pain lands are never routed
/// through their (test-only) named factories — they build through the binder
/// chain. These tests drive <see cref="ScryfallCardFactory"/> against the real
/// embedded seed oracle text so a regression in the binder regexes (or a seed
/// errata that changes the wording) surfaces here.
/// </summary>
public class ManaPainLandProdPathTests
{
    private readonly EmbeddedCardRepository _repo = new();
    private readonly ScryfallCardFactory _factory;
    private readonly Player _alice = new("Alice", 20);

    public ManaPainLandProdPathTests()
    {
        _factory = new ScryfallCardFactory(_repo);
    }

    [Fact]
    public void CityOfBrass_ProdPath_HasAnyColorManaAndBecomesTappedTrigger()
    {
        var card = _factory.Create("City of Brass", _alice);

        // Five plain any-colour mana abilities (no folded pain — the pain is a
        // separate becomes-tapped trigger in the binder path).
        card.Abilities.OfType<IManaAbility>()
            .Select(m => m.ManaGenerated.ToString())
            .Should().BeEquivalentTo(
                new[] { "W", "U", "B", "R", "G" }.Select(c => ManaCost.Parse(c).ToString()));

        // The becomes-tapped → 1 damage trigger is present.
        card.Abilities.OfType<ITriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ManaConfluence_ProdPath_HasFivePayLifeAnyColorManaAbilities()
    {
        var card = _factory.Create("Mana Confluence", _alice);

        var mana = card.Abilities.OfType<IManaAbility>().ToList();
        mana.Select(m => m.ManaGenerated.ToString())
            .Should().BeEquivalentTo(
                new[] { "W", "U", "B", "R", "G" }.Select(c => ManaCost.Parse(c).ToString()));

        // Activating one loses 1 life (the pay-life cost).
        var before = _alice.LifeTotal;
        mana.First().Activate();
        _alice.LifeTotal.Should().Be(before - 1);
    }

    [Fact]
    public void ForbiddenOrchard_ProdPath_HasAnyColorManaAndTapForManaTrigger()
    {
        var card = _factory.Create("Forbidden Orchard", _alice);

        card.Abilities.OfType<IManaAbility>().Should().HaveCount(5);
        card.Abilities.OfType<ITriggeredAbility>().Should().HaveCount(1,
            because: "the tap-for-mana reflexive Spirit trigger is bound");
    }

    [Fact]
    public void GemstoneMine_ProdPath_HasFiveCounterManaAndEtbTrigger()
    {
        var card = _factory.Create("Gemstone Mine", _alice);

        card.Abilities.OfType<IManaAbility>().Should().HaveCount(5,
            because: "exactly five counter-cost any-colour abilities — no free bare-{T} one");
        card.Abilities.OfType<ITriggeredAbility>().Should().HaveCount(1,
            because: "the enters-with-three-mining-counters ETB trigger is bound");
    }
}
