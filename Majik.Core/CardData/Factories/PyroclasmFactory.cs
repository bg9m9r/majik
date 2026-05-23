using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pyroclasm (Portal Second Age / Modern reprints,
/// {1}{R}).
///
/// Sorcery. Oracle text:
///   "Pyroclasm deals 2 damage to each creature."
///
/// ## Implementation
///
/// Card shape only at the dispatcher; the on-resolve effect is built on
/// demand via <see cref="BuildResolveEffect"/>. The effect iterates every
/// creature on every supplied player's battlefield and deals 2 damage
/// through <see cref="Creature.TakeDamage"/> — the same path
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Damage.DamageSpellFactory.DealsDamageEachCreatureSpell"/>
/// uses, only widened past the caster-only scan so opponents' creatures
/// also take damage (CR 109.5 — "each" without a controller restriction
/// reaches every creature on the battlefield).
///
/// ## Why a named factory (over the existing template)
/// The shared <c>DealsDamageEachCreatureTemplate</c> already binds
/// Pyroclasm's oracle text by shape, but its v1 stub scans only
/// <c>caster.Zones.Battlefield</c> — opponent creatures stay alive.
/// Production cast paths through <c>SpellCastFlow</c> can plumb
/// <see cref="Majik.Core.Game.ChosenSpellParams.AllPlayers"/> through the
/// effect factory, but several tests and bot probes construct the spell
/// directly without that plumbing. The named factory exposes a single
/// resolve effect that takes <c>allPlayers</c> as a positional argument,
/// matching <see cref="WheelOfFortuneFactory.BuildResolveEffect"/>'s
/// shape, so callers can fire the wrath at every battlefield in one
/// call.
///
/// ## CR notes
/// - CR 109.5 / CR 700 — "each creature" enumerates every creature on the
///   battlefield regardless of controller.
/// - CR 119.2 — non-combat damage; CR 119.3 — damage dealt is recorded by
///   <see cref="Creature.TakeDamage"/>; SBA (CR 704.5f / CreatureDeathCheck)
///   moves lethal-damaged creatures to graveyards on the next SBA pass.
/// - CR 614 — replacement effects on damage (protection, prevention) are
///   honoured by callers who route their damage through
///   <see cref="Majik.Core.Effects.ReplacementBus"/>; this v1 factory deals
///   damage directly to keep the resolve body minimal, same shape as
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Damage.DamageSpellFactory.DealsDamageEachCreatureSpell"/>.
/// </summary>
public static class PyroclasmFactory
{
    public const string CardName = "Pyroclasm";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 2;

    /// <summary>
    /// Build a Pyroclasm sorcery owned by <paramref name="owner"/>. Card
    /// shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Pyroclasm's resolve effect — 2 damage to every creature on
    /// every supplied player's battlefield. Single <see cref="IEffect"/>
    /// entry so callers can splice it into a
    /// <c>SpellDefinition.EffectFactory</c> result or a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields the sweep
    /// should reach. Typically every player in the game.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"Pyroclasm: deal {Damage} damage to each creature.", () =>
            {
                // CR 109.5 / CR 700 — "each creature" reaches every creature
                // on every battlefield. Snapshot to a list before applying so
                // any same-step zone-move side effects don't disturb the
                // enumeration; SBAs run on the next priority pass and move
                // lethal-damaged creatures to graveyards.
                var seen = new HashSet<Creature>();
                foreach (var pl in allPlayers)
                {
                    foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                    {
                        if (seen.Add(c)) c.TakeDamage(Damage);
                    }
                }
            }),
        };
    }
}
