using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FootHeadquartersFactory"/> — a White/Black
/// "gain land" in the Zendikar "Refuge" tapland shape. Oracle text (verified
/// against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {W} or {B}."
///
/// Same oracle shape as <see cref="AkoumRefugeFactory"/> (B/R Refuge) — only
/// the colours / printing differ. Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers the card's unique surface:
/// - Two single-colour mana abilities — {W} and {B} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - ETB effect: controller's life total rises by exactly 1 (CR 119.3).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by this
/// named-card factory — same posture as the rest of the Refuge cycle.
/// </summary>
[Trait("Color", "M")]
public class FootHeadquartersTests
{
    [Fact]
    public void FootHeadquarters_HasManaAbility_ForWhite()
    {
        var land = (Land)NamedCardFactory.Create("Foot Headquarters", new Player("Alice", 20));

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void FootHeadquarters_HasManaAbility_ForBlack()
    {
        var land = (Land)NamedCardFactory.Create("Foot Headquarters", new Player("Alice", 20));

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void FootHeadquarters_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Foot Headquarters", new Player("Alice", 20));
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void FootHeadquarters_EtbEffect_GainsExactlyOneLife()
    {
        // CR 119.3 — "you gain 1 life" raises the controller's life total by 1.
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Foot Headquarters", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "Foot Headquarters's ETB gains its controller 1 life");
    }
}
