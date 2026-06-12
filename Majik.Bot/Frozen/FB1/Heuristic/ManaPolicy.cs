using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Bot.Frozen.FB1;

/// <summary>
/// Picks which mana sources to tap. v1: returns <see cref="ManaPayment.Empty"/>
/// so the engine's ManaPaymentResolver auto-taps basics on the bot's behalf.
/// </summary>
internal static class ManaPolicy
{
    public static ManaPayment Pick(
        GameContext ctx, Player self, int costGenericAmount, int coloredRequired)
        => ManaPayment.Empty;
}
