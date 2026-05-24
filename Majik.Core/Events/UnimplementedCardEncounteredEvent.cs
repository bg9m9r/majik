using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// Diagnostic event — the bot (or any agent) attempted to make a decision
/// involving an <see cref="ICard.IsVanillaShell"/> card whose printed rules
/// text the engine doesn't enforce. Fires at most once per (game, card-name)
/// from <see cref="Majik.Core.Diagnostics.VanillaShellTracker"/>; downstream
/// subscribers (server logs, portal toast banners) can surface a "coverage
/// gap" notice to the operator without spamming the channel.
/// <para>
/// Not part of game state — purely meta. The game continues normally; the
/// bot has already deprioritised the unimplemented card in its EV scoring.
/// </para>
/// </summary>
public sealed class UnimplementedCardEncounteredEvent : GameEvent
{
    /// <summary>The card whose printed rules text is not enforced.</summary>
    public ICard Card { get; }

    /// <summary>The card's name — repeated here so subscribers can log
    /// without dereferencing <see cref="Card"/> (which may be reused across
    /// game instances by long-lived loggers).</summary>
    public string CardName { get; }

    /// <summary>The player who owns / would have used the card. Null when
    /// the encounter happened during a context that isn't player-specific
    /// (e.g. a deck-load scan).</summary>
    public Player? Player { get; }

    /// <summary>Human-readable context — "bot castable enumeration",
    /// "target candidate", "activated-ability source", etc. Useful for
    /// distinguishing the kind of decision the bot was making when it
    /// noticed the gap.</summary>
    public string Context { get; }

    public UnimplementedCardEncounteredEvent(ICard card, Player? player, string context)
        : base(EventType.UnimplementedCardEncountered)
    {
        Card = card ?? throw new ArgumentNullException(nameof(card));
        CardName = card.Name;
        Player = player;
        Context = context ?? string.Empty;
    }
}
