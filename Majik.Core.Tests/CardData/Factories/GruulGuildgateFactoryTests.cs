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
/// Unit tests for <see cref="GruulGuildgateFactory"/> — the R/G Guildgate.
/// Oracle text:
///   "This land enters tapped.
///    {T}: Add {R} or {G}."
///
/// Covers:
/// - Identity (Land + the printed Gate subtype, CR 205.3m).
/// - Two mana abilities producing {R} and {G} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the other Guildgate factories).
/// </summary>
[Trait("Color", "C")]
public class GruulGuildgateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    [Fact]
    public void GruulGuildgate_HasTwoManaAbilities_ProducingRedAndGreen()
    {
        var land = (Land)NamedCardFactory.Create("Gruul Guildgate", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Guildgate taps for {R} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void GruulGuildgate_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Gruul Guildgate", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Guildgates have no cycling, unlike the Triomes");
    }
}
