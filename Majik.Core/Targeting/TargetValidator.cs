using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;

namespace Majik.Core.Targeting;

/// <summary>
/// Service for validating targets according to Magic: The Gathering rules (Rule 115).
/// </summary>
public class TargetValidator
{
    /// <summary>
    /// Validate that targets meet the specification requirements.
    /// </summary>
    public bool ValidateTargets(TargetSpecification specification, IEnumerable<ITarget> targets, Player caster)
    {
        if (specification == null)
        {
            throw new ArgumentNullException(nameof(specification));
        }

        if (targets == null)
        {
            throw new ArgumentNullException(nameof(targets));
        }

        if (caster == null)
        {
            throw new ArgumentNullException(nameof(caster));
        }

        var targetList = targets.ToList();

        // Check target count
        if (targetList.Count < specification.MinTargets)
        {
            return false;
        }

        if (targetList.Count > specification.MaxTargets)
        {
            return false;
        }

        // Validate each target
        foreach (var target in targetList)
        {
            if (!ValidateTarget(specification, target, caster))
            {
                return false;
            }
        }

        // Check for duplicate targets (Rule 115.3)
        var targetIds = targetList.Select(t => t.Id).ToList();
        if (targetIds.Count != targetIds.Distinct().Count())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validate a single target against the specification.
    /// </summary>
    public bool ValidateTarget(TargetSpecification specification, ITarget target, Player caster)
    {
        if (specification == null)
        {
            throw new ArgumentNullException(nameof(specification));
        }

        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (caster == null)
        {
            throw new ArgumentNullException(nameof(caster));
        }

        // Check target type
        switch (target.TargetType)
        {
            case TargetType.Player:
                if (!specification.CanTargetPlayers)
                {
                    return false;
                }
                break;

            case TargetType.Card:
                if (!specification.CanTargetCards)
                {
                    return false;
                }
                break;

            case TargetType.Permanent:
                if (!specification.CanTargetPermanents)
                {
                    return false;
                }

                // Check card type requirements
                if (target is Target targetImpl)
                {
                    var permanent = targetImpl.GetPermanent();
                    if (permanent != null && specification.RequiredCardTypes != null)
                    {
                        var hasRequiredType = specification.RequiredCardTypes.Any(type => permanent.HasType(type));
                        if (!hasRequiredType)
                        {
                            return false;
                        }
                    }

                    // Check controller requirements
                    if (specification.MustBeControlledByCaster)
                    {
                        if (permanent?.Controller != caster)
                        {
                            return false;
                        }
                    }

                    if (specification.MustBeControlledByOpponent)
                    {
                        if (permanent?.Controller == caster || permanent?.Controller == null)
                        {
                            return false;
                        }
                    }
                }
                break;

            case TargetType.Spell:
                if (!specification.CanTargetSpells)
                {
                    return false;
                }
                break;

            case TargetType.Ability:
                if (!specification.CanTargetAbilities)
                {
                    return false;
                }
                break;

            default:
                return false;
        }

        return true;
    }

    /// <summary>
    /// Get all valid targets for a specification.
    /// </summary>
    public IEnumerable<ITarget> GetValidTargets(TargetSpecification specification, IEnumerable<Player> players, IEnumerable<ICard> cards, Player caster)
    {
        if (specification == null)
        {
            throw new ArgumentNullException(nameof(specification));
        }

        if (caster == null)
        {
            throw new ArgumentNullException(nameof(caster));
        }

        var validTargets = new List<ITarget>();

        // Add valid players
        if (specification.CanTargetPlayers && players != null)
        {
            validTargets.AddRange(players.Select(p => Target.Player(p)));
        }

        // Add valid permanents
        if (specification.CanTargetPermanents && cards != null)
        {
            var permanents = cards.OfType<Cards.Permanent>();
            
            foreach (var permanent in permanents)
            {
                // Check card type requirements
                if (specification.RequiredCardTypes != null)
                {
                    var hasRequiredType = specification.RequiredCardTypes.Any(type => permanent.HasType(type));
                    if (!hasRequiredType)
                    {
                        continue;
                    }
                }

                // Check controller requirements
                if (specification.MustBeControlledByCaster && permanent.Controller != caster)
                {
                    continue;
                }

                if (specification.MustBeControlledByOpponent && (permanent.Controller == caster || permanent.Controller == null))
                {
                    continue;
                }

                validTargets.Add(Target.Permanent(permanent));
            }
        }

        return validTargets;
    }
}
