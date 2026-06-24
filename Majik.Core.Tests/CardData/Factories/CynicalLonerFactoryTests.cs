using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CynicalLonerFactory"/> (Aetherdrift, {1}{B}).
/// Creature — Human Survivor 3/1. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "This creature can't be blocked by Glimmers.
///    Survival — At the beginning of your second main phase, if this creature
///    is tapped, you may search your library for a card, put it into your
///    graveyard, then shuffle."
///
/// Covers ONLY the card's unique behaviour:
/// - Identity ({1}{B} Creature — Human Survivor 3/1).
/// - "Can't be blocked by Glimmers" (CR 509.1b) — a Glimmer can't block it,
///   a non-Glimmer can.
/// - Survival intervening-if (CR 603.4): the trigger only goes on the stack
///   when the Loner is tapped (untapped ⇒ no trigger).
/// - Survival resolution: search library → graveyard → shuffle (CR 701.18 /
///   701.20a), gated on the "you may".
/// (Dispatch + well-formedness are covered for every card by
/// CardFactoryContractTests.)
/// </summary>
[Trait("Color", "B")]
public class CynicalLonerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static T OnBattlefield<T>(T permanent, Player owner) where T : Permanent
    {
        permanent.SetOwner(owner);
        permanent.SetController(owner);
        owner.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        return permanent;
    }

    private static Card InLibrary(string name, Player owner)
    {
        var card = new Sorcery(name, "{1}");
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }

    [Fact]
    public void Identity_HumanSurvivor_3_1_AtOneB()
    {
        var loner = CynicalLonerFactory.Create(_alice);

        loner.Name.Should().Be("Cynical Loner");
        loner.ManaCost.Should().Be("{1}{B}");
        loner.Power.Should().Be(3);
        loner.Toughness.Should().Be(1);
        loner.HasSubtype(CardSubtype.Human).Should().BeTrue();
        loner.HasSubtype(CardSubtype.Survivor).Should().BeTrue();
    }

    [Fact]
    public void CantBeBlockedByGlimmers_GlimmerCantBlock_NonGlimmerCan()
    {
        var svc = new ContinuousEffectsService();
        var loner = CynicalLonerFactory.Create(_alice, svc, triggers: null);
        loner.SetZone(ZoneType.Battlefield);

        // A Glimmer can't block it (CR 509.1b); a plain creature can.
        var glimmer = new Creature("Enduring Innocence", "{1}{W}", 2, 1,
            subtypes: new[] { CardSubtype.Glimmer })
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var plain = new Creature("Bear", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };

        BlockLegality.CanBlock(glimmer, loner, out _).Should()
            .BeFalse("Cynical Loner can't be blocked by Glimmers (CR 509.1b)");
        BlockLegality.CanBlock(plain, loner, out _).Should()
            .BeTrue("a non-Glimmer creature can block normally");
    }

    [Fact]
    public void Survival_InterveningIf_OnlyWhenTapped()
    {
        var loner = CynicalLonerFactory.Create(_alice);
        OnBattlefield(loner, _alice);
        var trigger = loner.Abilities.OfType<TriggeredAbility>().Single();

        // Untapped: the intervening-if (CR 603.4) blocks the trigger from the stack.
        loner.IsTapped.Should().BeFalse();
        trigger.CanBePutOnStack().Should().BeFalse("untapped ⇒ Survival doesn't trigger (CR 603.4)");

        // Tapped: the intervening-if is satisfied.
        loner.Tap();
        trigger.CanBePutOnStack().Should().BeTrue("tapped ⇒ Survival triggers");
    }

    [Fact]
    public async Task Survival_WhenTapped_SearchesLibraryToGraveyardThenShuffles()
    {
        var loner = CynicalLonerFactory.Create(_alice);
        OnBattlefield(loner, _alice);
        loner.Tap();

        var top = InLibrary("Swamp", _alice);
        InLibrary("Lightning Bolt", _alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true); // take the optional "you may"

        var game = new GameContext(
            _alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 1, currentPhase: null, stack: new Majik.Core.Stack.Stack());
        var ctx = ResolutionContext.For(_alice, agent, game, chosenTargets: null);

        await CynicalLonerFactory.ResolveSurvivalAsync(loner, _alice, zoneService: null, ctx);

        // ScriptedAgent's default library pick takes the first candidate.
        top.Zone.Should().Be(ZoneType.Graveyard,
            "the searched card is put into the graveyard (CR 701.18)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(top);
        // Library still holds the other card (it wasn't milled).
        _alice.Zones.Library.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public async Task Survival_Decline_NoMill()
    {
        var loner = CynicalLonerFactory.Create(_alice);
        OnBattlefield(loner, _alice);
        loner.Tap();

        var card = InLibrary("Swamp", _alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // decline the optional "you may"

        var game = new GameContext(
            _alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 1, currentPhase: null, stack: new Majik.Core.Stack.Stack());
        var ctx = ResolutionContext.For(_alice, agent, game, chosenTargets: null);

        await CynicalLonerFactory.ResolveSurvivalAsync(loner, _alice, zoneService: null, ctx);

        card.Zone.Should().Be(ZoneType.Library, "declining the 'you may' mills nothing");
    }
}
