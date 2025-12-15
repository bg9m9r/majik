using Majik.Core.Cards.Types;

namespace Majik.Core.Targeting;

/// <summary>
/// Value object specifying what can be targeted by a spell or ability.
/// </summary>
public class TargetSpecification
{
    /// <summary>
    /// Whether players can be targeted.
    /// </summary>
    public bool CanTargetPlayers { get; }

    /// <summary>
    /// Whether cards can be targeted.
    /// </summary>
    public bool CanTargetCards { get; }

    /// <summary>
    /// Whether permanents can be targeted.
    /// </summary>
    public bool CanTargetPermanents { get; }

    /// <summary>
    /// Whether spells on the stack can be targeted.
    /// </summary>
    public bool CanTargetSpells { get; }

    /// <summary>
    /// Whether abilities on the stack can be targeted.
    /// </summary>
    public bool CanTargetAbilities { get; }

    /// <summary>
    /// Required card types for permanent targets (if any).
    /// </summary>
    public IReadOnlyList<CardType>? RequiredCardTypes { get; }

    /// <summary>
    /// Whether the target must be controlled by the caster.
    /// </summary>
    public bool MustBeControlledByCaster { get; }

    /// <summary>
    /// Whether the target must be controlled by an opponent.
    /// </summary>
    public bool MustBeControlledByOpponent { get; }

    /// <summary>
    /// Minimum number of targets required.
    /// </summary>
    public int MinTargets { get; }

    /// <summary>
    /// Maximum number of targets allowed.
    /// </summary>
    public int MaxTargets { get; }

    private TargetSpecification(
        bool canTargetPlayers,
        bool canTargetCards,
        bool canTargetPermanents,
        bool canTargetSpells,
        bool canTargetAbilities,
        IReadOnlyList<CardType>? requiredCardTypes,
        bool mustBeControlledByCaster,
        bool mustBeControlledByOpponent,
        int minTargets,
        int maxTargets)
    {
        CanTargetPlayers = canTargetPlayers;
        CanTargetCards = canTargetCards;
        CanTargetPermanents = canTargetPermanents;
        CanTargetSpells = canTargetSpells;
        CanTargetAbilities = canTargetAbilities;
        RequiredCardTypes = requiredCardTypes;
        MustBeControlledByCaster = mustBeControlledByCaster;
        MustBeControlledByOpponent = mustBeControlledByOpponent;
        MinTargets = minTargets;
        MaxTargets = maxTargets;

        if (minTargets < 0)
        {
            throw new ArgumentException("MinTargets cannot be negative", nameof(minTargets));
        }

        if (maxTargets < minTargets)
        {
            throw new ArgumentException("MaxTargets must be >= MinTargets", nameof(maxTargets));
        }
    }

    /// <summary>
    /// Create a target specification for targeting any permanent.
    /// </summary>
    public static TargetSpecification AnyPermanent(int minTargets = 1, int maxTargets = 1)
    {
        return new TargetSpecification(
            canTargetPlayers: false,
            canTargetCards: false,
            canTargetPermanents: true,
            canTargetSpells: false,
            canTargetAbilities: false,
            requiredCardTypes: null,
            mustBeControlledByCaster: false,
            mustBeControlledByOpponent: false,
            minTargets: minTargets,
            maxTargets: maxTargets);
    }

    /// <summary>
    /// Create a target specification for targeting any player.
    /// </summary>
    public static TargetSpecification AnyPlayer(int minTargets = 1, int maxTargets = 1)
    {
        return new TargetSpecification(
            canTargetPlayers: true,
            canTargetCards: false,
            canTargetPermanents: false,
            canTargetSpells: false,
            canTargetAbilities: false,
            requiredCardTypes: null,
            mustBeControlledByCaster: false,
            mustBeControlledByOpponent: false,
            minTargets: minTargets,
            maxTargets: maxTargets);
    }

    /// <summary>
    /// Create a target specification for targeting a creature.
    /// </summary>
    public static TargetSpecification Creature(int minTargets = 1, int maxTargets = 1, bool mustBeControlledByOpponent = false)
    {
        return new TargetSpecification(
            canTargetPlayers: false,
            canTargetCards: false,
            canTargetPermanents: true,
            canTargetSpells: false,
            canTargetAbilities: false,
            requiredCardTypes: new[] { CardType.Creature },
            mustBeControlledByCaster: false,
            mustBeControlledByOpponent: mustBeControlledByOpponent,
            minTargets: minTargets,
            maxTargets: maxTargets);
    }

    /// <summary>
    /// Create a target specification for targeting a spell.
    /// </summary>
    public static TargetSpecification Spell(int minTargets = 1, int maxTargets = 1)
    {
        return new TargetSpecification(
            canTargetPlayers: false,
            canTargetCards: false,
            canTargetPermanents: false,
            canTargetSpells: true,
            canTargetAbilities: false,
            requiredCardTypes: null,
            mustBeControlledByCaster: false,
            mustBeControlledByOpponent: false,
            minTargets: minTargets,
            maxTargets: maxTargets);
    }

    /// <summary>
    /// Create a target specification with custom parameters.
    /// </summary>
    public static TargetSpecification Create(
        bool canTargetPlayers = false,
        bool canTargetCards = false,
        bool canTargetPermanents = false,
        bool canTargetSpells = false,
        bool canTargetAbilities = false,
        IReadOnlyList<CardType>? requiredCardTypes = null,
        bool mustBeControlledByCaster = false,
        bool mustBeControlledByOpponent = false,
        int minTargets = 1,
        int maxTargets = 1)
    {
        return new TargetSpecification(
            canTargetPlayers,
            canTargetCards,
            canTargetPermanents,
            canTargetSpells,
            canTargetAbilities,
            requiredCardTypes,
            mustBeControlledByCaster,
            mustBeControlledByOpponent,
            minTargets,
            maxTargets);
    }
}
