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
/// Unit tests for <see cref="BorosGuildgateFactory"/> — the R/W Guildgate.
/// Oracle text:
///   "This land enters tapped.
///    {T}: Add {R} or {W}."
///
/// Covers:
/// - Identity (Land + the printed Gate subtype, CR 205.3m).
/// - Two mana abilities producing {R} and {W} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the other Guildgate factories).
/// </summary>
[Trait("Color", "C")]
public class BorosGuildgateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    [Fact]
    public void BorosGuildgate_HasTwoManaAbilities_ProducingRedAndWhite()
    {
        var land = (Land)NamedCardFactory.Create("Boros Guildgate", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Guildgate taps for {R} or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void BorosGuildgate_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Boros Guildgate", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Guildgates have no cycling, unlike the Triomes");
    }
}
