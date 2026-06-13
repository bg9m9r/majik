using FluentAssertions;
using Majik.Bot.Strategies;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests.Strategies;

public sealed class DeckStrategyHelpersTests
{
    // ── Zone query helpers ─────────────────────────────────────────────────────

    [Fact]
    public void HasInHand_ReturnsTrue_WhenCardPresent()
    {
        var s = new BotTestScenario();
        s.AddCardToHand(s.Self, new Creature("Goblin Guide", manaCost: "{R}", power: 2, toughness: 2));
        DeckStrategyHelpers.HasInHand(s.Self, "Goblin Guide").Should().BeTrue();
    }

    [Fact]
    public void HasInHand_ReturnsFalse_WhenCardAbsent()
    {
        var s = new BotTestScenario();
        DeckStrategyHelpers.HasInHand(s.Self, "Goblin Guide").Should().BeFalse();
    }

    [Fact]
    public void HasOnBoard_ReturnsTrue_WhenCardOnBattlefield()
    {
        var s = new BotTestScenario();
        s.AddCreatureToBattlefield(s.Self, "Dark Confidant", 2, 1);
        DeckStrategyHelpers.HasOnBoard(s.Self, "Dark Confidant").Should().BeTrue();
    }

    [Fact]
    public void HasInGraveyard_ReturnsTrue_WhenCardPresent()
    {
        var s = new BotTestScenario();
        var crt = new Creature("Tarmogoyf", manaCost: "{1}{G}", power: 0, toughness: 1);
        crt.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(crt);
        DeckStrategyHelpers.HasInGraveyard(s.Self, "Tarmogoyf").Should().BeTrue();
    }

    [Fact]
    public void FindInHand_ReturnsCard_WhenPresent()
    {
        var s = new BotTestScenario();
        var crt = new Creature("Goblin Guide", manaCost: "{R}", power: 2, toughness: 2);
        s.AddCardToHand(s.Self, crt);
        DeckStrategyHelpers.FindInHand(s.Self, "Goblin Guide").Should().BeSameAs(crt);
    }

    [Fact]
    public void FindInHand_ReturnsNull_WhenAbsent()
    {
        var s = new BotTestScenario();
        DeckStrategyHelpers.FindInHand(s.Self, "Goblin Guide").Should().BeNull();
    }

    // ── BuildCast ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildCast_ReturnsNull_WhenCardNotInHand()
    {
        var s = new BotTestScenario();
        DeckStrategyHelpers.BuildCast(s.Context, s.Self, "Lightning Bolt").Should().BeNull();
    }

    [Fact]
    public void BuildCast_ReturnsNull_WhenInsufficientMana()
    {
        var s = new BotTestScenario();
        // Card in hand but no mana available.
        s.AddCardToHand(s.Self, new Instant("Lightning Bolt", manaCost: "{R}"));
        DeckStrategyHelpers.BuildCast(s.Context, s.Self, "Lightning Bolt").Should().BeNull();
    }

    [Fact]
    public void BuildCast_ReturnsCastSpell_ForNamedCardInHand_Untargeted()
    {
        var s = new BotTestScenario();
        // One Mountain → 1 mana available. {R} CMC=1.
        s.AddLandToBattlefield(s.Self, "Mountain");
        var bolt = new Instant("Lightning Bolt", manaCost: "{R}");
        s.AddCardToHand(s.Self, bolt);

        var action = DeckStrategyHelpers.BuildCast(s.Context, s.Self, "Lightning Bolt");

        action.Should().BeOfType<PriorityAction.CastSpell>();
        ((PriorityAction.CastSpell)action!).Card.Should().BeSameAs(bolt);
    }

    [Fact]
    public void BuildCast_ReturnsCastSpell_WithExplicitTarget_FoldedIntoTargetsList()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Mountain");
        var bolt = new Instant("Lightning Bolt", manaCost: "{R}");
        s.AddCardToHand(s.Self, bolt);

        var action = DeckStrategyHelpers.BuildCast(s.Context, s.Self, "Lightning Bolt", target: s.Opponent);

        action.Should().BeOfType<PriorityAction.CastSpell>();
        var cs = (PriorityAction.CastSpell)action!;
        cs.Card.Should().BeSameAs(bolt);
        cs.Targets.Should().ContainSingle().Which.Should().BeSameAs(s.Opponent);
    }

    [Fact]
    public void BuildCast_ReturnsCastSpell_ForCreature_InSorceryWindow()
    {
        var s = new BotTestScenario();
        // Two lands for {1}{R} CMC=2.
        s.AddLandToBattlefield(s.Self, "Mountain1");
        s.AddLandToBattlefield(s.Self, "Mountain2");
        var guide = new Creature("Goblin Guide", manaCost: "{R}", power: 2, toughness: 2);
        s.AddCardToHand(s.Self, guide);

        var action = DeckStrategyHelpers.BuildCast(s.Context, s.Self, "Goblin Guide");

        action.Should().BeOfType<PriorityAction.CastSpell>();
        ((PriorityAction.CastSpell)action!).Card.Should().BeSameAs(guide);
    }

    [Fact]
    public void BuildCast_ReturnsNull_ForSorcery_WhenNotSorceryWindow()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Mountain");
        s.AddLandToBattlefield(s.Self, "Mountain2");
        var sorcery = new Sorcery("Thoughtseize", manaCost: "{B}");
        s.AddCardToHand(s.Self, sorcery);

        // Opponent's turn → no sorcery window; BuildCast should return null.
        var oppCtx = new Majik.Core.Game.GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Opponent,
            turnNumber: 1, currentPhase: Majik.Core.StateMachine.StepStateType.PreCombatMain,
            stack: s.Stack, landPlayAvailable: false);

        DeckStrategyHelpers.BuildCast(oppCtx, s.Self, "Thoughtseize").Should().BeNull();
    }

    // ── BuildActivate ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildActivate_ReturnsNull_WhenPermanentNotOnBoard()
    {
        var s = new BotTestScenario();
        DeckStrategyHelpers.BuildActivate(s.Context, s.Self, "Vault Skirge").Should().BeNull();
    }
}
