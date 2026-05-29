using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flame Sweep (Magic 2011 / reprints, {2}{R}).
///
/// Instant. Oracle text:
///   "Flame Sweep deals 2 damage to each creature except for creatures
///    you control with flying."
///
/// ## Implementation
///
/// Modelled on <see cref="PyroclasmFactory"/> — a "2 damage to each
/// creature" sweep — with two differences:
///   1. Flame Sweep is an Instant, not a Sorcery.
///   2. The sweep skips creatures the caster controls that have flying
///      (CR 109.5 "except for…" carves an exclusion out of the otherwise
///      unrestricted "each creature" set).
///
/// Card shape only at the dispatcher; the on-resolve effect is built on
/// demand via <see cref="BuildResolveEffect"/>. The effect iterates every
/// creature on every supplied player's battlefield and deals 2 damage
/// through <see cref="Creature.TakeDamage"/>, skipping any creature whose
/// controller is the caster AND which has flying.
///
/// Flying is read via <see cref="CombatAbilities.HasFlying(Creature)"/>,
/// the engine's single source of truth for the Flying keyword marker
/// (CR 702.9). "You control" is evaluated against the caster's
/// <see cref="Card.Controller"/> reference — opponent flyers are NOT
/// exempt, only the caster's.
///
/// ## CR notes
/// - CR 109.5 / CR 700 — "each creature" enumerates every creature on the
///   battlefield regardless of controller; the "except for creatures you
///   control with flying" clause removes a subset from that set.
/// - CR 119.2 — non-combat damage; CR 119.3 — damage dealt is recorded by
///   <see cref="Creature.TakeDamage"/>; SBA (CR 704.5g / lethal-damage
///   check) moves lethal-damaged creatures to graveyards on the next SBA
///   pass.
/// - CR 614 — replacement effects on damage (protection, prevention) are
///   honoured by callers who route damage through the replacement bus;
///   this factory deals damage directly to keep the resolve body minimal,
///   same shape as <see cref="PyroclasmFactory.BuildResolveEffect"/>.
/// </summary>
[CardName("Flame Sweep")]
public static class FlameSweepFactory
{
    public const string CardName = "Flame Sweep";
    public const string PrintedManaCost = "{2}{R}";
    public const int Damage = 2;

    /// <summary>CardDef DSL — card shape only. <see cref="BuildResolveEffect"/>
    /// supplies the resolve-time sweep.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Flame Sweep's resolve effect — 2 damage to every creature on
    /// every supplied player's battlefield, except creatures the caster
    /// (<paramref name="you"/>) controls that have flying.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields the sweep
    /// should reach. Typically every player in the game.</param>
    /// <param name="you">The caster — their flying creatures are exempt
    /// (CR 109.5 "creatures you control with flying").</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers, Player you)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);
        ArgumentNullException.ThrowIfNull(you);

        return new IEffect[]
        {
            new Effect($"Flame Sweep: deal {Damage} damage to each creature except creatures you control with flying.", () =>
            {
                // CR 109.5 / CR 700 — "each creature" reaches every creature
                // on every battlefield; the "except for creatures you control
                // with flying" clause removes the caster's flyers. Snapshot to
                // a list before applying so any same-step zone-move side
                // effects don't disturb the enumeration; SBAs run on the next
                // priority pass and move lethal-damaged creatures to
                // graveyards.
                var seen = new HashSet<Creature>();
                foreach (var pl in allPlayers)
                {
                    foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                    {
                        if (!seen.Add(c)) continue;

                        // CR 109.5 — exempt only creatures the caster controls
                        // that have flying. Opponent flyers, and the caster's
                        // own non-flyers, still take damage.
                        bool youControl = ReferenceEquals(c.Controller, you);
                        if (youControl && CombatAbilities.HasFlying(c)) continue;

                        c.TakeDamage(Damage);
                    }
                }
            }),
        };
    }
}
