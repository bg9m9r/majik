using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>CR 704.5a/b/c — a player with 0 life, an attempted empty-library
/// draw, or 10+ poison counters loses the game.</summary>
public sealed class PlayerLifeCheck : IStateBasedActionCheck
{
    public string Name => "PlayerLife";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        foreach (var player in ctx.Players)
        {
            if (player.HasLost) continue;

            string? reason = null;
            if (player.LifeTotal <= 0)
                reason = $"{player.Name} lost: 0 or less life (CR 704.5a)";
            else if (player.TriedToDrawFromEmptyLibrary)
                reason = $"{player.Name} lost: tried to draw from empty library (CR 704.5b)";
            else if (player.PoisonCounters >= 10)
                reason = $"{player.Name} lost: 10+ poison counters (CR 704.5c)";

            if (reason != null)
            {
                player.MarkLost();
                ctx.EventBus?.Publish(new PlayerLostEvent(player));
                ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(reason));
                anyExecuted = true;
            }
        }
        return anyExecuted;
    }
}
