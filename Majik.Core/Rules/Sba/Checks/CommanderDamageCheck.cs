using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>
/// CR 704.5j — in the Commander variant, a player who has been dealt 21 or more
/// combat damage by the same commander over the course of the game loses the
/// game. The per-source accumulation is tracked on each player's
/// <see cref="Majik.Core.Formats.Commander.CommanderState"/> as combat damage is
/// dealt (<c>CommandFlow.TakeCommanderDamage</c>); this state-based action
/// converts that accumulated total into the actual loss, as a DEFERRED SBA
/// sweep — mirroring how <see cref="PlayerLifeCheck"/> defers the CR 704.5a
/// life-loss check rather than flipping <c>HasLost</c> eagerly at the damage
/// site. (Before this check existed, <c>CombatFlow</c> set <c>HasLost</c>
/// eagerly the moment the 21st point landed, inconsistent with the deferred
/// life-loss model.)
/// </summary>
public sealed class CommanderDamageCheck : IStateBasedActionCheck
{
    public string Name => "CommanderDamage";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        foreach (var player in ctx.Players)
        {
            if (player.HasLost) continue;
            if (player.Commander?.HasLostToCommanderDamage() != true) continue;

            player.MarkLost();
            ctx.EventBus?.Publish(new PlayerLostEvent(player));
            ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(
                $"{player.Name} lost: 21+ commander damage from one source (CR 704.5j)"));
            anyExecuted = true;
        }
        return anyExecuted;
    }
}
