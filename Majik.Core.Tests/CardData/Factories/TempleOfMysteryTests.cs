using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TempleOfMysteryFactory"/> (Theros Beyond Death).
///
/// G/U "scry land". Oracle text:
///   "This land enters tapped.
///    When this land enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}: Add {G} or {U}."
///
/// Same oracle shape as the rest of the Temple scry-land cycle
/// (<see cref="TempleOfTriumphFactory"/>), only the produced colours are
/// {G}/{U} (CR 605.1a). The ETB keyword action is scry 1 (CR 701.20). Loaded
/// from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {G} and {U} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that scries 1.
/// - Scry-1 fall-back (no agent) puts the peeked card on the bottom.
/// - Scry with an empty library is a graceful no-op.
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the rest of the cycle.
/// </summary>
[Trait("Color", "C")]
public class TempleOfMysteryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TempleOfMystery_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Temple of Mystery", _alice);

        land.Name.Should().Be("Temple of Mystery");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void TempleOfMystery_HasManaAbility_ForGreen()
    {
        var land = (Land)NamedCardFactory.Create("Temple of Mystery", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void TempleOfMystery_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("Temple of Mystery", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void TempleOfMystery_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Temple of Mystery", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void TempleOfMystery_EtbEffect_ScriesOne_DefaultsTopCardToBottom()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = (Land)NamedCardFactory.Create("Temple of Mystery", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // No agent registered → fall-back puts the single peeked card (Top)
        // on the bottom; the previously-second card is now on top.
        alice.Zones.Library.GetCards().Should().Equal(new[] { second, top });
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void TempleOfMystery_EtbEffect_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);

        var land = (Land)NamedCardFactory.Create("Temple of Mystery", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Library.GetCards().Should().BeEmpty();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public async Task TempleOfMystery_EtbEffect_PromptsCtxAgent_HonoursKeepTop()
    {
        // PLAN 01 (Slice D) — the migrated CardDefinitionFactory scry effect
        // now prompts the agent off the ResolutionContext rather than
        // auto-bottoming. A scripted agent that KEEPS the top card must be
        // honoured: Top stays on top instead of being bottomed.
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var agent = new ScriptedAgent();
        // Keep the peeked top card on top (nothing to bottom).
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: System.Array.Empty<ICard>(),
            TopOrder: new[] { (ICard)top }));

        var land = (Land)NamedCardFactory.Create("Temple of Mystery", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        var rc = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);
        foreach (var effect in etb.Effects)
        {
            await effect.ExecuteAsync(rc);
        }

        // Agent kept Top on top — library order unchanged.
        alice.Zones.Library.GetCards().Should().Equal(new[] { top, second });
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
