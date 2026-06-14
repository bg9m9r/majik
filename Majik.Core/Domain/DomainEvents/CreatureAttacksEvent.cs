using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// CR 508.1f — fires when a creature is declared as an attacker. One event
/// per attacking creature so binders for "Whenever ~ attacks, …" triggers
/// can hook a per-attacker condition without needing to walk the whole
/// CombatPlan.
/// </summary>
public class CreatureAttacksEvent : GameEvent
{
    /// <summary>
    /// The attacking permanent. Typed <see cref="Permanent"/> (not
    /// <see cref="Creature"/>) so an animated NON-creature combatant — a manland
    /// (CR 613.1c) — names ITSELF here when it attacks, letting Restless-land
    /// "whenever ~ attacks" triggers finally observe their own land (deferral
    /// <c>animated-noncreature-as-combatant</c>, 4B). A real <see cref="Creature"/>
    /// is a <see cref="Permanent"/>, so existing trigger binders that read
    /// <c>.Controller</c> / <c>ReferenceEquals</c> are unaffected.
    /// </summary>
    public Permanent Attacker { get; }
    public object DefendingPlayerOrPlaneswalker { get; }

    public CreatureAttacksEvent(Permanent attacker, object defendingPlayerOrPlaneswalker)
        : base(EventType.PhaseEnded)
    {
        Attacker = attacker ?? throw new ArgumentNullException(nameof(attacker));
        DefendingPlayerOrPlaneswalker = defendingPlayerOrPlaneswalker
            ?? throw new ArgumentNullException(nameof(defendingPlayerOrPlaneswalker));
    }

    /// <summary>
    /// CR 506.2 / CR 508.4d — resolve the <b>defending player</b> from a
    /// <see cref="DefendingPlayerOrPlaneswalker"/> value. When the attack was
    /// declared against a player, that player IS the defending player; when it
    /// was declared against a planeswalker (real OR an EFFECTIVE planeswalker —
    /// a flipped creature-front DFC carrying a transient loyalty body, CR 711),
    /// the defending player is the <em>controller of that planeswalker</em>.
    ///
    /// <para>This consults <see cref="Permanent.IsEffectivePlaneswalker"/> rather
    /// than the concrete C# instance type so that a non-real planeswalker
    /// defender resolves the same as a real one. Without it, an attack-trigger
    /// effect that reads "defending player" (Restless Fortress's drain) silently
    /// no-ops when the defender is an effective planeswalker — the residual
    /// coupling between the widened Permanent-level combat surface and the
    /// trigger-time defender read.</para>
    /// </summary>
    /// <returns>The defending player, or <c>null</c> for an unexpected value.</returns>
    public static Player? DefendingPlayerOf(object? defendingPlayerOrPlaneswalker) =>
        defendingPlayerOrPlaneswalker switch
        {
            Player p => p,
            Permanent pw when pw.IsEffectivePlaneswalker() => pw.Controller,
            _ => null,
        };

    /// <summary>The defending player for THIS attack (CR 506.2 / 508.4d).</summary>
    public Player? DefendingPlayer => DefendingPlayerOf(DefendingPlayerOrPlaneswalker);
}
