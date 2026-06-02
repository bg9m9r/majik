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
/// Unit tests for <see cref="GolgariGuildgateFactory"/> — the Return to
/// Ravnica / reprint Golgari Guildgate. Oracle text:
///   "This land enters tapped.
///    {T}: Add {B} or {G}."
///
/// Covers:
/// - Identity (Land — Gate; CR 305.6 the printed land subtype).
/// - Two mana abilities producing {B} and {G} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - No cycling / no other activated abilities (Guildgates are not cycling
///   lands, unlike the Triome analogue this factory was modelled on).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as the Triome factories).
/// </summary>
[Trait("Color", "C")]
public class GolgariGuildgateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    [Fact]
    public void GolgariGuildgate_HasTwoManaAbilities_ProducingBG()
    {
        var land = (Land)NamedCardFactory.Create("Golgari Guildgate", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Guildgate taps for {B} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void GolgariGuildgate_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Golgari Guildgate", _alice);

        // Guildgates have only the {T} mana ability (a ManaAbility, not an
        // ActivatedAbility) — no cycling, no other activated abilities.
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
