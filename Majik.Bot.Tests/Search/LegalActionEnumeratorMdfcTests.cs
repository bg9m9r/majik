using FluentAssertions;
using Majik.Bot.Search;
using Majik.Bot.Tests.Helpers;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// CR 305 / 712.3 — the enumerator must surface an MDFC back-face LAND play
/// (a land play, not a spell) whenever the land drop is available, regardless
/// of front-face affordability. Without this arm a 0-land MDFC hand is
/// permanently mana-locked (Belcher trace, 2026-06-12): the front-face cast arm
/// gates on affordability, so at 0 mana the engine's face-choice point is never
/// reached.
///
/// <para>Sink into Stupor // Soporific Springs is the canonical spell-front +
/// land-back MDFC (its <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> reports
/// <c>CanCastEitherFace</c> + a <c>CastableBackFace</c> that <c>IsLand</c>).</para>
/// </summary>
public class LegalActionEnumeratorMdfcTests
{
    [Fact]
    public void ForPriority_SurfacesMdfcBackLandCast_AtZeroMana()
    {
        var s = new BotTestScenario();
        // Self is active player, PreCombatMain, empty stack, land drop available.
        // ZERO mana: no lands in play.

        // Hand: only the MDFC (front face is the {1}{U}{U} bounce instant, which
        // is unaffordable at 0 mana — so the normal cast arm cannot surface it).
        var sink = SinkIntoStuporFactory.Create(s.Self);
        s.AddCardToHand(s.Self, sink);

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        actions.Should().Contain(
            a => (a as PriorityAction.CastSpell) != null
              && ReferenceEquals(((PriorityAction.CastSpell)a).Card, sink),
            because: "the MDFC back face is a land — its play must be surfaced even at 0 mana");
        actions.Should().NotContain(a => a is PriorityAction.PlayLand,
            because: "the MDFC is not a plain Land object, so no PlayLand action exists for it");
    }

    [Fact]
    public void ForPriority_NoMdfcBackLandCast_WhenLandDropUsed()
    {
        var s = new BotTestScenario();
        // Same board, but the land drop is NOT available (already used / not our
        // window). The front face is still unaffordable at 0 mana → the MDFC must
        // be absent from the action set entirely.
        var noDropCtx = new Majik.Core.Game.GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Self,
            turnNumber: 1, currentPhase: Majik.Core.StateMachine.StepStateType.PreCombatMain,
            stack: s.Stack, landPlayAvailable: false);

        var sink = SinkIntoStuporFactory.Create(s.Self);
        s.AddCardToHand(s.Self, sink);

        var actions = LegalActionEnumerator.ForPriority(noDropCtx, s.Self);

        actions.Should().NotContain(
            a => (a as PriorityAction.CastSpell) != null
              && ReferenceEquals(((PriorityAction.CastSpell)a).Card, sink),
            because: "the land drop is unavailable and the front face is unaffordable at 0 mana");
    }

    [Fact]
    public void ForPriority_NonMdfcHand_Unchanged()
    {
        // A plain Land + a vanilla affordable spell hand must enumerate identically
        // before and after the MDFC arm (pin count + kinds).
        var s = new BotTestScenario();
        s.AddCardToHand(s.Self, new Land("Forest"));
        s.AddLandToBattlefield(s.Self, "ForestBF");
        s.AddCardToHand(s.Self,
            new Creature("Llanowar Elves", manaCost: "{G}", power: 1, toughness: 1));

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        // Pass + PlayLand + CastSpell(Llanowar Elves) — exactly three, no MDFC arm.
        actions.Should().HaveCount(3);
        actions.Should().ContainSingle(a => a is PriorityAction.PassAction);
        actions.Should().ContainSingle(a => a is PriorityAction.PlayLand);
        actions.Should().ContainSingle(a => a is PriorityAction.CastSpell);
    }
}
