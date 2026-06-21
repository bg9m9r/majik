using Majik.Core.Cards;
using Majik.Core.Events;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// CR 509.1h — fires when a creature is declared as a blocker. One event per
/// blocker→attacker pairing so binders for "Whenever ~ blocks a creature, …"
/// triggers (Brimaz, King of Oreskos) can hook a per-blocker condition without
/// walking the whole block plan. The blocking creature and the attacker it was
/// declared to block are both carried (CR 509.1h — "blocks a creature" names
/// the blocked attacker, so the trigger can act on that specific attacker, e.g.
/// "create a token blocking that creature").
/// </summary>
public class CreatureBlocksEvent : GameEvent
{
    /// <summary>The permanent that was declared as a blocker (typed
    /// <see cref="Permanent"/> so an animated manland blocking is carried too;
    /// deferral <c>animated-noncreature-as-combatant</c>, 4B).</summary>
    public Permanent Blocker { get; }

    /// <summary>The attacking permanent that <see cref="Blocker"/> is blocking.</summary>
    public Permanent BlockedAttacker { get; }

    public CreatureBlocksEvent(Permanent blocker, Permanent blockedAttacker)
        : base()
    {
        Blocker = blocker ?? throw new ArgumentNullException(nameof(blocker));
        BlockedAttacker = blockedAttacker ?? throw new ArgumentNullException(nameof(blockedAttacker));
    }
}
