using Majik.Bot.Evaluation;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Bot.Heuristic;

/// <summary>
/// Picks a priority action by enumerating legal options and scoring each
/// via BoardEval against a projection of post-action state.
/// </summary>
public class PriorityPolicy
{
    protected readonly ArchetypeWeights _weights;

    public PriorityPolicy(ArchetypeWeights weights) { _weights = weights; }

    public virtual PriorityAction Pick(GameContext ctx, Player self)
    {
        var current = BoardEval.Score(ctx, self, _weights);

        PriorityAction best = PriorityAction.Pass;
        double bestScore = current;

        if (ctx.ActivePlayer == self && ctx.CurrentPhase == PhaseStateType.Main && ctx.Stack.Count == 0)
        {
            var landInHand = self.Zones.Hand.GetCards().OfType<Land>().FirstOrDefault();
            if (landInHand != null)
            {
                var projected = current + _weights.ManaSources * 1;
                if (projected > bestScore)
                {
                    bestScore = projected;
                    best = new PriorityAction.PlayLand(landInHand);
                }
            }
        }

        if (ctx.ActivePlayer == self)
        {
            var manaAvailable = self.Zones.Battlefield.GetCards().OfType<Land>().Count();
            var castable = self.Zones.Hand.GetCards()
                .Where(c => c is not Land)
                .Where(c => ApproxCmc(c) <= manaAvailable)
                .OrderByDescending(ApproxCmc)
                .FirstOrDefault();
            if (castable != null)
            {
                var projected = current + _weights.BoardPower * (castable is Creature crt ? crt.Power : 1);
                if (projected > bestScore)
                {
                    bestScore = projected;
                    best = new PriorityAction.CastSpell(castable, Array.Empty<object>());
                }
            }
        }

        return best;
    }

    private static int ApproxCmc(ICard card) => 0; // v1 stub
}
