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
/// Unit tests for <see cref="AzoriusGuildgateFactory"/> — the W/U Guildgate.
/// Oracle text:
///   "This land enters tapped.
///    {T}: Add {W} or {U}."
///
/// Covers:
/// - Identity (Land + the printed Gate subtype, CR 205.3m).
/// - Two mana abilities producing {W} and {U} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Triome factories).
/// </summary>
[Trait("Color", "C")]
public class AzoriusGuildgateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    [Fact]
    public void AzoriusGuildgate_HasTwoManaAbilities_ProducingWhiteAndBlue()
    {
        var land = (Land)NamedCardFactory.Create("Azorius Guildgate", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Guildgate taps for {W} or {U}");
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void AzoriusGuildgate_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Azorius Guildgate", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Guildgates have no cycling, unlike the Triomes");
    }
}
