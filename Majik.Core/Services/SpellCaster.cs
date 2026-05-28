using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.Services;

/// <summary>
/// Service for casting spells.
/// Handles spell casting logic and adds spells to the stack.
/// Implements Magic: The Gathering casting rules (Rule 601).
/// </summary>
public class SpellCaster
{
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly IEventBus? _eventBus;
    private readonly CostPayment _costPayment;
    private readonly TargetValidator _targetValidator;

    public SpellCaster(Majik.Core.Stack.Stack stack, IEventBus? eventBus = null)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _eventBus = eventBus;
        _costPayment = new CostPayment();
        _targetValidator = new TargetValidator();
    }

    /// <summary>
    /// Check if a card can be cast as a spell.
    /// </summary>
    public bool CanCast(ICard card, Player player, bool isMainPhase, bool isStackEmpty)
    {
        if (card == null || player == null)
        {
            return false;
        }

        // Check if card is in hand (Rule 601.2a)
        if (card.Zone != ZoneType.Hand)
        {
            return false;
        }

        // Check card type restrictions
        if (card.HasType(CardType.Sorcery))
        {
            // Sorceries can only be cast during main phase with empty stack (Rule 307.1)
            if (!isMainPhase || !isStackEmpty)
            {
                return false;
            }
        }

        // Instants can be cast at instant speed (any time you have priority)
        // Other validations (targets, costs) will be checked in CastSpell

        return true;
    }

    /// <summary>
    /// Cast a spell with targets and costs (full casting process per Rule 601.2a-h).
    /// </summary>
    /// <remarks>
    /// <paramref name="isMainPhase"/> and <paramref name="isStackEmpty"/>
    /// default to <c>true</c> — i.e. "no timing restriction" — so a bare
    /// <c>CastSpell(card, player)</c> call casts at instant speed
    /// regardless of card type. Sorcery-speed callers that want to
    /// exercise the timing gate (CR 307.1) must pass them explicitly.
    /// </remarks>
    public void CastSpell(ICard card, Player player, IEnumerable<ITarget>? targets = null, IEnumerable<ICost>? costs = null, bool isMainPhase = true, bool isStackEmpty = true)
    {
        if (card == null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        // Step 601.2a: Announce spell and move to stack
        if (!CanCast(card, player, isMainPhase, isStackEmpty))
        {
            throw new InvalidPlayerActionException($"Cannot cast {card.Name}");
        }

        // Step 601.2b: Choose mode (if modal) - TODO: Future
        // Step 601.2c: Choose targets - TODO: Validate targets against specification
        var targetList = targets?.ToList() ?? new List<ITarget>();

        // Step 601.2d: Determine total cost
        var costList = costs?.ToList() ?? new List<ICost>();
        
        // Add mana cost from card
        if (!string.IsNullOrWhiteSpace(card.ManaCost))
        {
            costList.Insert(0, new ManaCostCost(card.ManaCost));
        }

        // Step 601.2e: Activate mana abilities - TODO: Future
        // Step 601.2f: Pay costs
        _costPayment.PayCosts(player, costList);

        // Step 601.2g: Spell becomes cast
        var spell = new Spells.Spell(card, player, targetList, costList);

        // Move card to stack zone (Rule 601.2a)
        // TODO: Use ZoneService to move card to stack zone
        card.SetZone(ZoneType.Stack);

        // Add spell to stack
        _stack.Push(spell);

        // Fire events
        _eventBus?.Publish(new SpellCastEvent(spell));
        if (targetList.Any())
        {
            _eventBus?.Publish(new TargetsChosenEvent(spell, targetList));
        }
        _eventBus?.Publish(new CostsPaidEvent(spell, costList));
    }
}
