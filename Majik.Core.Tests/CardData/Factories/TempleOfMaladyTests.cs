using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TempleOfMaladyFactory"/> (Journey into Nyx).
///
/// B/G "scry land". Oracle text:
///   "This land enters tapped.
///    When this land enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}: Add {B} or {G}."
///
/// Same oracle shape as the Theros scry-land cycle
/// (<see cref="TempleOfTriumphFactory"/>), only the two mana abilities
/// produce {B} and {G} (CR 605.1a) and the ETB keyword action is scry 1
/// (CR 701.20). Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {B} and {G} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that scries 1.
/// - Scry-1 fall-back (no agent) puts the peeked card on the bottom.
/// - Scry with an empty library is a graceful no-op.
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the scry-land cycle.
/// </summary>
public class TempleOfMaladyTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TempleOfMalady_IsLand_WithCorrectName()
    {
        var land = TempleOfMaladyFactory.Create(_alice);

        land.Name.Should().Be("Temple of Malady");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TempleOfMalady()
    {
        var card = NamedCardFactory.Create("Temple of Malady", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Temple of Malady");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void TempleOfMalady_HasManaAbility_ForBlack()
    {
        var land = TempleOfMaladyFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void TempleOfMalady_HasManaAbility_ForGreen()
    {
        var land = TempleOfMaladyFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void TempleOfMalady_EtbTrigger_IsBattlefieldActive()
    {
        var land = TempleOfMaladyFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void TempleOfMalady_EtbEffect_ScriesOne_DefaultsTopCardToBottom()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = TempleOfMaladyFactory.Create(alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // No agent registered → fall-back puts the single peeked card (Top)
        // on the bottom; the previously-second card is now on top.
        alice.Zones.Library.GetCards().Should().Equal(new[] { second, top });
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void TempleOfMalady_EtbEffect_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);

        var land = TempleOfMaladyFactory.Create(alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Library.GetCards().Should().BeEmpty();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
