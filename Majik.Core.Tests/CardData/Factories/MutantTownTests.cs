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
/// Unit tests for <see cref="MutantTownFactory"/> (green/blue gain-life
/// tapland — same oracle shape as the Zendikar "Refuge" cycle).
///
/// G/U "gain land". Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {G} or {U}."
///
/// Same oracle shape as <see cref="AkoumRefugeFactory"/> (Zendikar B/R
/// Refuge) — only the colours / printing differ. Loaded from the embedded
/// JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Two single-colour mana abilities — {G} and {U} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - ETB effect: controller's life total rises by exactly 1 (CR 119.3).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the rest of the Refuge cycle.
/// Card identity / name / type / dispatch are asserted for every implemented
/// card automatically by CardFactoryContractTests.
/// </summary>
[Trait("Color", "M")]
public class MutantTownTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MutantTown_HasManaAbility_ForGreen()
    {
        var land = (Land)NamedCardFactory.Create("Mutant Town", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void MutantTown_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("Mutant Town", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void MutantTown_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Mutant Town", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void MutantTown_EtbEffect_GainsExactlyOneLife()
    {
        // CR 119.3 — "you gain 1 life" raises the controller's life total by 1.
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Mutant Town", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "Mutant Town's ETB gains its controller 1 life");
    }
}
