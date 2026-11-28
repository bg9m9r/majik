namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 601.2 / 732.1 — thrown by an agent when its player backs out of a cast
/// at one of the casting prompts (modes, X, targets). The cast flow reverses
/// the cast exactly as it does an illegal one: the card returns to the zone it
/// was cast from, and mana abilities already activated are not undone.
/// An <see cref="InvalidOperationException"/>, so every dispatcher that already
/// unwinds an illegal cast unwinds a cancelled one too.
/// </summary>
public sealed class CastCancelledException : InvalidOperationException
{
    public CastCancelledException()
        : base("The player cancelled the cast (CR 601.2 / 732.1).")
    {
    }
}