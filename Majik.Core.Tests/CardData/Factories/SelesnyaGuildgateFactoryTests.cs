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
/// Unit tests for <see cref="SelesnyaGuildgateFactory"/> — the G/W Guildgate.
/// Oracle text:
///   "This land enters tapped.
///    {T}: Add {G} or {W}."
///
/// Covers:
/// - Identity (Land + the printed Gate subtype, CR 205.3m).
/// - Two mana abilities producing {G} and {W} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the other Guildgate factories).
/// </summary>
public class SelesnyaGuildgateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SelesnyaGuildgate_Dispatch_ReturnsLandWithGateSubtype()
    {
        var card = NamedCardFactory.Create("Selesnya Guildgate", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Selesnya Guildgate");
        card.HasSubtype(CardSubtype.Gate).Should().BeTrue();
    }

    [Fact]
    public void SelesnyaGuildgate_HasTwoManaAbilities_ProducingGreenAndWhite()
    {
        var land = (Land)NamedCardFactory.Create("Selesnya Guildgate", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Guildgate taps for {G} or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void SelesnyaGuildgate_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Selesnya Guildgate", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Guildgates have no cycling, unlike the Triomes");
    }
}
