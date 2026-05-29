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
/// Unit tests for <see cref="SimicGuildgateFactory"/> — the G/U Guildgate.
/// Oracle text:
///   "This land enters tapped.
///    {T}: Add {G} or {U}."
///
/// Covers:
/// - Identity (Land + the printed Gate subtype, CR 205.3m).
/// - Two mana abilities producing {G} and {U} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the other Guildgate factories).
/// </summary>
public class SimicGuildgateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SimicGuildgate_Dispatch_ReturnsLandWithGateSubtype()
    {
        var card = NamedCardFactory.Create("Simic Guildgate", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Simic Guildgate");
        card.HasSubtype(CardSubtype.Gate).Should().BeTrue();
    }

    [Fact]
    public void SimicGuildgate_HasTwoManaAbilities_ProducingGreenAndBlue()
    {
        var land = (Land)NamedCardFactory.Create("Simic Guildgate", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Guildgate taps for {G} or {U}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void SimicGuildgate_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Simic Guildgate", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Guildgates have no cycling, unlike the Triomes");
    }
}
