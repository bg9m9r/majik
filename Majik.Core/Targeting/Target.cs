using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Stack;

namespace Majik.Core.Targeting;

/// <summary>
/// Base implementation of a target.
/// </summary>
public class Target : ITarget
{
    public Guid Id { get; }
    public TargetType TargetType { get; }
    public object? TargetObject { get; }

    private Target(TargetType targetType, object? targetObject)
    {
        Id = Guid.NewGuid();
        TargetType = targetType;
        TargetObject = targetObject;
    }

    /// <summary>
    /// Create a target for a player.
    /// </summary>
    public static Target Player(Player player)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        return new Target(TargetType.Player, player);
    }

    /// <summary>
    /// Create a target for a card.
    /// </summary>
    public static Target Card(ICard card)
    {
        if (card == null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        return new Target(TargetType.Card, card);
    }

    /// <summary>
    /// Create a target for a permanent.
    /// </summary>
    public static Target Permanent(Cards.Permanent permanent)
    {
        if (permanent == null)
        {
            throw new ArgumentNullException(nameof(permanent));
        }

        return new Target(TargetType.Permanent, permanent);
    }

    /// <summary>
    /// Create a target for a spell on the stack.
    /// </summary>
    public static Target Spell(Spells.ISpell spell)
    {
        if (spell == null)
        {
            throw new ArgumentNullException(nameof(spell));
        }

        return new Target(TargetType.Spell, spell);
    }

    /// <summary>
    /// Create a target for an ability on the stack.
    /// </summary>
    public static Target Ability(Abilities.IActivatedAbility ability)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        return new Target(TargetType.Ability, ability);
    }

    /// <summary>
    /// Get the player target, or null if not a player target.
    /// </summary>
    public Player? GetPlayer()
    {
        return TargetType == TargetType.Player ? TargetObject as Player : null;
    }

    /// <summary>
    /// Get the card target, or null if not a card target.
    /// </summary>
    public ICard? GetCard()
    {
        return TargetType == TargetType.Card || TargetType == TargetType.Permanent ? TargetObject as ICard : null;
    }

    /// <summary>
    /// Get the permanent target, or null if not a permanent target.
    /// </summary>
    public Cards.Permanent? GetPermanent()
    {
        return TargetType == TargetType.Permanent ? TargetObject as Cards.Permanent : null;
    }

    /// <summary>
    /// Get the spell target, or null if not a spell target.
    /// </summary>
    public Spells.ISpell? GetSpell()
    {
        return TargetType == TargetType.Spell ? TargetObject as Spells.ISpell : null;
    }

    /// <summary>
    /// Get the ability target, or null if not an ability target.
    /// </summary>
    public Abilities.IActivatedAbility? GetAbility()
    {
        return TargetType == TargetType.Ability ? TargetObject as Abilities.IActivatedAbility : null;
    }
}
