using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HighlandForestFactory"/> — the R/G snow dual
/// land. Type line: <c>Snow Land — Mountain Forest</c>. Oracle text:
///   "({T}: Add {R} or {G}.)
///    This land enters tapped."
///
/// Covers:
/// - Identity (Land + Snow supertype, CR 205.4d, + the printed Mountain and
///   Forest subtypes, CR 205.3i).
/// - Two mana abilities producing {R} and {G} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as <see cref="AlpineMeadowFactory"/>).
/// </summary>
[Trait("Color", "C")]
public class HighlandForestFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    [Fact]
    public void HighlandForest_HasTwoManaAbilities_ProducingRedAndGreen()
    {
        var land = (Land)NamedCardFactory.Create("Highland Forest", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Highland Forest taps for {R} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void HighlandForest_HasNoNonManaActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Highland Forest", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("the snow dual lands have no activated abilities");
    }
}
