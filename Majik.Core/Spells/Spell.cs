using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Players;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.Spells;

/// <summary>
/// Represents a spell on the stack.
/// </summary>
public class Spell : ISpell
{
    private ResolutionState _resolutionState;
    private readonly List<ITarget> _targets = new();
    private readonly List<ICost> _costs = new();
    private readonly List<IEffect> _effects = new();

    public Guid Id { get; }
    public Player Controller { get; }
    public DateTime Timestamp { get; }
    public ICard Card { get; }
    public IReadOnlyList<ITarget> Targets => _targets.AsReadOnly();
    public IReadOnlyList<ICost> Costs => _costs.AsReadOnly();
    public IReadOnlyList<IEffect> Effects => _effects.AsReadOnly();
    public bool IsResolving => _resolutionState.IsResolving;

    public Spell(ICard card, Player controller, IEnumerable<ITarget>? targets = null, IEnumerable<ICost>? costs = null, IEnumerable<IEffect>? effects = null)
    {
        if (card == null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        if (controller == null)
        {
            throw new ArgumentNullException(nameof(controller));
        }

        Card = card;
        Controller = controller;
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
        _resolutionState = ResolutionState.NotResolving();

        if (targets != null)
        {
            _targets.AddRange(targets);
        }

        if (costs != null)
        {
            _costs.AddRange(costs);
        }

        if (effects != null)
        {
            _effects.AddRange(effects);
        }
    }

    /// <summary>
    /// Check if the spell can be cast.
    /// </summary>
    public bool CanBeCast(bool isMainPhase, bool isStackEmpty)
    {
        // Check card type restrictions
        if (Card.HasType(CardType.Sorcery))
        {
            // Sorceries can only be cast during main phase with empty stack
            return isMainPhase && isStackEmpty;
        }

        // Instants can be cast at instant speed (any time you have priority)
        if (Card.HasType(CardType.Instant))
        {
            return true;
        }

        // Other spell types (future)
        return true;
    }

    public void Resolve()
    {
        if (_resolutionState.IsResolving)
        {
            throw new InvalidOperationException("Spell is already resolving");
        }

        _resolutionState = ResolutionState.Resolving();
        
        // Resolution logic (Rule 608)
        // Execute all effects
        foreach (var effect in _effects)
        {
            effect.Execute();
        }
        
        _resolutionState = ResolutionState.Resolved(DateTime.UtcNow);
    }

    /// <summary>
    /// Get the zone the spell should move to after resolution.
    /// Permanents go to battlefield, instants/sorceries go to graveyard (Rule 608.2).
    /// </summary>
    public ZoneType GetDestinationZone()
    {
        // Permanents go to battlefield
        if (Card.HasType(CardType.Creature) ||
            Card.HasType(CardType.Land) ||
            Card.HasType(CardType.Enchantment) ||
            Card.HasType(CardType.Artifact) ||
            Card.HasType(CardType.Planeswalker))
        {
            return ZoneType.Battlefield;
        }

        // Instants and sorceries go to graveyard
        if (Card.HasType(CardType.Instant) || Card.HasType(CardType.Sorcery))
        {
            return ZoneType.Graveyard;
        }

        // Default to graveyard
        return ZoneType.Graveyard;
    }
}
