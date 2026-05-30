using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Combat;

/// <summary>
/// Service for validating combat actions according to Magic: The Gathering rules (Rule 508, 509).
/// </summary>
public class CombatValidator
{
    private readonly ContinuousEffectsService? _effects;

    public CombatValidator(ContinuousEffectsService? effects = null)
    {
        _effects = effects;
    }

    /// <summary>
    /// Check if a creature can attack.
    /// </summary>
    public bool CanAttack(Creature creature, Player activePlayer)
    {
        if (creature == null || activePlayer == null)
        {
            return false;
        }

        // Creature must be controlled by active player (Rule 508.1a)
        if (creature.Controller != activePlayer)
        {
            return false;
        }

        // Creature must be on the battlefield (Rule 508.1b)
        if (creature.Zone != ZoneType.Battlefield)
        {
            return false;
        }

        // Creature must be untapped (unless has vigilance) (Rule 508.1c)
        if (creature.IsTapped && !CombatAbilities.HasVigilance(creature))
        {
            return false;
        }

        // Creature must not have summoning sickness (unless has haste) (Rule 302.6)
        if (creature.HasSummoningSickness && !CombatAbilities.HasHaste(creature))
        {
            return false;
        }

        // Per-turn CannotAttack restriction (CR 508.1c) — installed by
        // spells like Orim's Chant / "<X> creatures can't attack this turn".
        if (_effects?.HasRestriction(creature, CombatRestriction.CannotAttack) == true)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Check if a creature can block an attacker.
    /// </summary>
    public bool CanBlock(Creature creature, Attacker attacker, Player defendingPlayer)
    {
        if (creature == null || attacker == null || defendingPlayer == null)
        {
            return false;
        }

        // Creature must be controlled by defending player (Rule 509.1a)
        if (creature.Controller != defendingPlayer)
        {
            return false;
        }

        // Creature must be on the battlefield (Rule 509.1b)
        if (creature.Zone != ZoneType.Battlefield)
        {
            return false;
        }

        // Creature must be untapped (Rule 509.1c)
        if (creature.IsTapped)
        {
            return false;
        }

        // Creature must be able to block flying if attacker has flying (Rule 509.1d)
        if (CombatAbilities.HasFlying(attacker.Creature) && !CombatAbilities.CanBlockFlying(creature))
        {
            return false;
        }

        // CR 702.16e — attacker with protection from blocker's colour can't
        // be blocked by that colour. Check both directions: attacker's
        // protection vs blocker, and blocker's protection vs attacker
        // (the latter being relevant for combat damage but not strictly
        // for the block declaration; here we enforce only the attacker
        // side, which is the canonical "can't be blocked" interaction).
        if (AttackerProtectedFromBlocker(attacker.Creature, creature))
        {
            return false;
        }

        // Per-turn CannotBlock restriction (CR 509.1c) — installed by Falter,
        // Magmatic Chasm, "<modifier> creatures can't block this turn", and
        // the target-creature single-creature variants.
        if (_effects?.HasRestriction(creature, CombatRestriction.CannotBlock) == true)
        {
            return false;
        }

        // Per-turn CannotBeBlocked restriction on the attacker (CR 702.x) —
        // installed by Slip Through Space / Trailblazer evasion grants. The
        // attacker is the creature inside the Attacker wrapper.
        if (_effects?.HasRestriction(attacker.Creature, CombatRestriction.CannotBeBlocked) == true)
        {
            return false;
        }

        return true;
    }

    private static bool AttackerProtectedFromBlocker(Creature attacker, Creature blocker)
    {
        // CR 105.3 / 702.16e — use the blocker's EFFECTIVE colour so a
        // Layer-5 colour-changing effect (e.g. "is all colors") is honoured.
        // Falls back to the printed/static colour when no effect is active.
        var blockerColors = blocker.GetEffectiveColors();
        foreach (var c in blockerColors)
        {
            if (Majik.Core.Rules.Protection.HasProtectionFromColor(attacker, c))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if a player can be attacked.
    /// </summary>
    public bool CanAttackPlayer(Player target, Player attacker)
    {
        if (target == null || attacker == null)
        {
            return false;
        }

        // Cannot attack yourself
        if (target == attacker)
        {
            return false;
        }

        // Target must not have lost
        if (target.HasLost)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Check if a planeswalker can be attacked.
    /// </summary>
    public bool CanAttackPlaneswalker(Planeswalker target, Player attacker)
    {
        if (target == null || attacker == null)
        {
            return false;
        }

        // Planeswalker must be on the battlefield
        if (target.Zone != ZoneType.Battlefield)
        {
            return false;
        }

        // Planeswalker must not be controlled by attacker
        if (target.Controller == attacker)
        {
            return false;
        }

        // Planeswalker must not be dead
        if (target.IsDead())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validate a collection of attacker declarations.
    /// </summary>
    public bool IsValidAttackDeclaration(IEnumerable<Creature> attackers, Player activePlayer, Player? targetPlayer, Planeswalker? targetPlaneswalker)
    {
        if (attackers == null || activePlayer == null)
        {
            return false;
        }

        var attackerList = attackers.ToList();

        // Must have valid target
        if (targetPlayer == null && targetPlaneswalker == null)
        {
            return false;
        }

        // Validate each attacker
        foreach (var attacker in attackerList)
        {
            if (!CanAttack(attacker, activePlayer))
            {
                return false;
            }

            // Check target validity
            if (targetPlayer != null && !CanAttackPlayer(targetPlayer, activePlayer))
            {
                return false;
            }

            if (targetPlaneswalker != null && !CanAttackPlaneswalker(targetPlaneswalker, activePlayer))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validate a collection of blocker declarations.
    /// </summary>
    public bool IsValidBlockDeclaration(IEnumerable<(Creature blocker, Attacker attacker)> blocks, Player defendingPlayer)
    {
        if (blocks == null || defendingPlayer == null)
        {
            return false;
        }

        var blockList = blocks.ToList();
        var blockersUsed = new HashSet<Creature>();

        foreach (var (blocker, attacker) in blockList)
        {
            // Each blocker can only block once (Rule 509.1)
            if (blockersUsed.Contains(blocker))
            {
                return false;
            }

            // Validate block
            if (!CanBlock(blocker, attacker, defendingPlayer))
            {
                return false;
            }

            blockersUsed.Add(blocker);
        }

        return true;
    }
}
