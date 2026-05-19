using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Rules;

/// <summary>
/// Service for checking and executing state-based actions (Rule 704).
/// State-based actions are checked whenever a player would receive priority.
/// </summary>
public class StateBasedActions
{
    private readonly IEventBus? _eventBus;
    private readonly ZoneService? _zoneService;
    private readonly TriggerManager? _triggerManager;

    public StateBasedActions(
        IEventBus? eventBus = null,
        ZoneService? zoneService = null,
        TriggerManager? triggerManager = null)
    {
        _eventBus = eventBus;
        _zoneService = zoneService;
        _triggerManager = triggerManager;
    }

    /// <summary>
    /// Check and execute all state-based actions.
    /// SBAs are checked repeatedly until none execute (Rule 704.4).
    /// </summary>
    public void CheckStateBasedActions(IEnumerable<Player> players, IEnumerable<ICard> allCards)
    {
        if (players == null || allCards == null)
        {
            return;
        }

        var playerList = players.ToList();
        var cardList = allCards.ToList();

        // SBAs are checked repeatedly until none execute (Rule 704.4)
        bool anyExecuted;
        do
        {
            anyExecuted = false;

            // Check each state-based action in order (Rule 704.3)
            // Note: Order matters - check in the order specified by rules
            if (CheckPlayerLife(playerList)) anyExecuted = true;
            if (CheckCounterCancellation(cardList)) anyExecuted = true;
            if (CheckTokensCeaseToExist(cardList)) anyExecuted = true;
            if (CheckAttachmentLegality(cardList)) anyExecuted = true;
            if (CheckBattleDestroyed(cardList)) anyExecuted = true;
            if (CheckSagaSacrificed(cardList)) anyExecuted = true;
            if (CheckSpellWithNoCard()) anyExecuted = true;
            if (CheckCreatureDeath(cardList)) anyExecuted = true;
            if (CheckPlaneswalkerDeath(cardList)) anyExecuted = true;
            if (CheckLegendRule(cardList)) anyExecuted = true;
            if (CheckPlaneswalkerUniqueness(cardList)) anyExecuted = true;

            // Update card list after each check (cards may have moved zones)
            cardList = allCards.ToList();

        } while (anyExecuted);

        // Rule 603.2c: state-change triggers are checked alongside SBAs.
        _triggerManager?.EvaluateStateChangeTriggers();
    }

    /// <summary>
    /// Check if any player has lost (0 or less life) (Rule 704.5a).
    /// Returns true if any SBA was executed.
    /// </summary>
    private bool CheckPlayerLife(IEnumerable<Player> players)
    {
        bool anyExecuted = false;
        foreach (var player in players)
        {
            if (player.HasLost) continue;

            string? reason = null;
            if (player.LifeTotal <= 0)
                reason = $"{player.Name} lost: 0 or less life (CR 704.5a)";
            else if (player.TriedToDrawFromEmptyLibrary)
                reason = $"{player.Name} lost: tried to draw from empty library (CR 704.5b)";
            else if (player.PoisonCounters >= 10)
                reason = $"{player.Name} lost: 10+ poison counters (CR 704.5c)";

            if (reason != null)
            {
                player.HasLost = true;
                _eventBus?.Publish(new PlayerLostEvent(player));
                _eventBus?.Publish(new StateBasedActionExecutedEvent(reason));
                anyExecuted = true;
            }
        }
        return anyExecuted;
    }

    /// <summary>
    /// Check if any creatures have died (damage >= toughness) (Rule 704.5f).
    /// Returns true if any SBA was executed.
    /// </summary>
    private bool CheckCreatureDeath(IEnumerable<ICard> allCards)
    {
        bool anyExecuted = false;
        var creatures = allCards.OfType<Cards.Creature>().ToList();

        foreach (var creature in creatures)
        {
            if (creature.Zone != ZoneType.Battlefield) continue;
            if (Majik.Core.Combat.CombatAbilities.HasIndestructible(creature)) continue;

            var dies = creature.IsDead() || creature.MarkedForDestructionByDeathtouch;
            if (dies)
            {
                if (_zoneService != null)
                {
                    _zoneService.MoveCardTo(creature, ZoneType.Graveyard);
                }
                else
                {
                    creature.Zone = ZoneType.Graveyard;
                }
                _eventBus?.Publish(new StateBasedActionExecutedEvent($"Creature {creature.Name} died"));
                anyExecuted = true;
            }
        }
        return anyExecuted;
    }

    /// <summary>
    /// Check if any planeswalkers have died (0 loyalty) (Rule 704.5j).
    /// Returns true if any SBA was executed.
    /// </summary>
    private bool CheckPlaneswalkerDeath(IEnumerable<ICard> allCards)
    {
        bool anyExecuted = false;
        var planeswalkers = allCards.OfType<Cards.Planeswalker>().ToList();

        foreach (var planeswalker in planeswalkers)
        {
            if (planeswalker.IsDead() && planeswalker.Zone == ZoneType.Battlefield)
            {
                // Use ZoneService to move planeswalker to graveyard
                if (_zoneService != null)
                {
                    _zoneService.MoveCardTo(planeswalker, ZoneType.Graveyard);
                }
                else
                {
                    planeswalker.Zone = ZoneType.Graveyard;
                }
                _eventBus?.Publish(new StateBasedActionExecutedEvent($"Planeswalker {planeswalker.Name} died"));
                anyExecuted = true;
            }
        }
        return anyExecuted;
    }

    /// <summary>
    /// Check Legend rule (Rule 704.5k).
    /// If a player controls two or more legendary permanents with the same name,
    /// that player chooses one and puts the rest into their owner's graveyard.
    /// Returns true if any SBA was executed.
    /// </summary>
    private bool CheckLegendRule(IEnumerable<ICard> allCards)
    {
        bool anyExecuted = false;
        var permanents = allCards.OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield)
            .Where(p => p.HasSupertype(CardSupertype.Legendary))
            .ToList();

        // Group by name and controller
        var legendaryGroups = permanents
            .GroupBy(p => new { p.Name, p.Controller })
            .Where(g => g.Count() > 1);

        foreach (var group in legendaryGroups)
        {
            // Rule 704.5k: Player chooses one, rest go to graveyard
            // For simplicity, keep the one that entered battlefield first (or most recently)
            // In a full implementation, the player would choose
            var sorted = group.OrderBy(p => p.EnteredBattlefieldTimestamp ?? DateTime.MaxValue).ToList();
            var toKeep = sorted.First();
            var toRemove = sorted.Skip(1).ToList();

            foreach (var permanent in toRemove)
            {
                if (_zoneService != null)
                {
                    _zoneService.MoveCardTo(permanent, ZoneType.Graveyard);
                }
                else
                {
                    permanent.Zone = ZoneType.Graveyard;
                }
                _eventBus?.Publish(new StateBasedActionExecutedEvent(
                    $"Legend rule: {permanent.Name} put into graveyard (controlled by {permanent.Controller?.Name})"));
                anyExecuted = true;
            }
        }

        return anyExecuted;
    }

    /// <summary>
    /// Check Planeswalker uniqueness rule (Rule 704.5m).
    /// If a player controls two or more planeswalkers with the same planeswalker subtype,
    /// that player chooses one and puts the rest into their owner's graveyard.
    /// Returns true if any SBA was executed.
    /// </summary>
    private bool CheckPlaneswalkerUniqueness(IEnumerable<ICard> allCards)
    {
        bool anyExecuted = false;
        var planeswalkers = allCards.OfType<Planeswalker>()
            .Where(p => p.Zone == ZoneType.Battlefield)
            .ToList();

        // Group by planeswalker subtype and controller
        // Planeswalker subtypes are things like "Jace", "Liliana", etc.
        var planeswalkerGroups = planeswalkers
            .Select(p => new
            {
                Planeswalker = p,
                Subtype = p.Subtypes.FirstOrDefault(s => IsPlaneswalkerSubtype(s)),
                p.Controller
            })
            .Where(x => x.Subtype != default(CardSubtype)) // Check if subtype was found
            .GroupBy(x => new { x.Subtype, x.Controller })
            .Where(g => g.Count() > 1);

        foreach (var group in planeswalkerGroups)
        {
            // Rule 704.5m: Player chooses one, rest go to graveyard
            // For simplicity, keep the one that entered battlefield first (or most recently)
            var sorted = group.OrderBy(x => x.Planeswalker.EnteredBattlefieldTimestamp ?? DateTime.MaxValue).ToList();
            var toKeep = sorted.First().Planeswalker;
            var toRemove = sorted.Skip(1).Select(x => x.Planeswalker).ToList();

            foreach (var planeswalker in toRemove)
            {
                if (_zoneService != null)
                {
                    _zoneService.MoveCardTo(planeswalker, ZoneType.Graveyard);
                }
                else
                {
                    planeswalker.Zone = ZoneType.Graveyard;
                }
                _eventBus?.Publish(new StateBasedActionExecutedEvent(
                    $"Planeswalker uniqueness rule: {planeswalker.Name} put into graveyard (controlled by {planeswalker.Controller?.Name})"));
                anyExecuted = true;
            }
        }

        return anyExecuted;
    }

    /// <summary>
    /// CR 704.5q — if a permanent has both +1/+1 and -1/-1 counters,
    /// N pairs are removed (where N = min of the two counts).
    /// </summary>
    private bool CheckCounterCancellation(IEnumerable<ICard> allCards)
    {
        bool anyExecuted = false;
        foreach (var perm in allCards.OfType<Permanent>())
        {
            if (perm.Zone != ZoneType.Battlefield) continue;
            var plus = perm.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne);
            var minus = perm.Counters.Count(Majik.Core.Counters.CounterType.MinusOneMinusOne);
            var n = Math.Min(plus, minus);
            if (n > 0)
            {
                perm.Counters.Remove(Majik.Core.Counters.CounterType.PlusOnePlusOne, n);
                perm.Counters.Remove(Majik.Core.Counters.CounterType.MinusOneMinusOne, n);
                _eventBus?.Publish(new StateBasedActionExecutedEvent(
                    $"{perm.Name}: {n} +1/+1 cancelled with {n} -1/-1"));
                anyExecuted = true;
            }
        }
        return anyExecuted;
    }

    /// <summary>
    /// CR 704.5h/n — Auras illegally attached (no target, target left, wrong
    /// type) go to graveyard. Equipment / Fortifications attached to an
    /// illegal permanent become unattached (stay on the battlefield).
    /// </summary>
    private bool CheckAttachmentLegality(IEnumerable<ICard> allCards)
    {
        bool anyExecuted = false;
        foreach (var perm in allCards.OfType<Permanent>().ToList())
        {
            if (perm.Zone != ZoneType.Battlefield) continue;
            if (perm.AttachedTo == null) continue;
            if (perm.AttachedTo.Zone == ZoneType.Battlefield) continue;

            if (perm.HasType(CardType.Enchantment) && perm.HasSubtype(CardSubtype.Aura))
            {
                perm.Unattach();
                if (_zoneService != null) _zoneService.MoveCardTo(perm, ZoneType.Graveyard);
                else perm.Zone = ZoneType.Graveyard;
                _eventBus?.Publish(new StateBasedActionExecutedEvent(
                    $"Aura {perm.Name} put into graveyard — no legal attachment"));
            }
            else
            {
                perm.Unattach();
                _eventBus?.Publish(new StateBasedActionExecutedEvent(
                    $"{perm.Name} unattached — bearer gone"));
            }
            anyExecuted = true;
        }
        return anyExecuted;
    }

    /// <summary>CR 704.5n — Battle with 0 defense counters → graveyard.</summary>
    private bool CheckBattleDestroyed(IEnumerable<ICard> allCards)
    {
        bool anyExecuted = false;
        foreach (var perm in allCards.OfType<Permanent>().ToList())
        {
            if (perm.Zone != ZoneType.Battlefield) continue;
            if (perm.BattleState == null) continue;
            if (!perm.BattleState.ShouldBeSacrificed()) continue;

            if (_zoneService != null) _zoneService.MoveCardTo(perm, ZoneType.Graveyard);
            else perm.Zone = ZoneType.Graveyard;
            _eventBus?.Publish(new StateBasedActionExecutedEvent(
                $"Battle {perm.Name} destroyed — 0 defense"));
            anyExecuted = true;
        }
        return anyExecuted;
    }

    /// <summary>CR 704.5r — Saga with all chapters complete → sacrifice.</summary>
    private bool CheckSagaSacrificed(IEnumerable<ICard> allCards)
    {
        bool anyExecuted = false;
        foreach (var perm in allCards.OfType<Permanent>().ToList())
        {
            if (perm.Zone != ZoneType.Battlefield) continue;
            if (perm.SagaState == null) continue;
            if (!perm.SagaState.ShouldBeSacrificed()) continue;

            if (_zoneService != null) _zoneService.MoveCardTo(perm, ZoneType.Graveyard);
            else perm.Zone = ZoneType.Graveyard;
            _eventBus?.Publish(new StateBasedActionExecutedEvent(
                $"Saga {perm.Name} sacrificed — final chapter complete"));
            anyExecuted = true;
        }
        return anyExecuted;
    }

    /// <summary>CR 704.5e — spell on stack with no card ceases to exist.
    /// Engine-built spells always carry a card; no-op for now.</summary>
    private bool CheckSpellWithNoCard() => false;

    /// <summary>
    /// CR 704.5d — a token in a zone other than the battlefield ceases
    /// to exist (removed from its current zone, not moved anywhere).
    /// </summary>
    private bool CheckTokensCeaseToExist(IEnumerable<ICard> allCards)
    {
        bool anyExecuted = false;
        foreach (var perm in allCards.OfType<Permanent>().ToList())
        {
            if (!perm.IsToken || perm.Zone == ZoneType.Battlefield) continue;
            var zone = perm.Owner?.Zones.GetZone(perm.Zone);
            if (zone == null || !zone.ContainsCard(perm)) continue;

            zone.RemoveCard(perm);
            _eventBus?.Publish(new StateBasedActionExecutedEvent(
                $"Token {perm.Name} ceases to exist"));
            anyExecuted = true;
        }
        return anyExecuted;
    }

    /// <summary>
    /// Check if a subtype is a planeswalker subtype.
    /// </summary>
    private static bool IsPlaneswalkerSubtype(CardSubtype subtype)
    {
        // Planeswalker subtypes are specific named subtypes like Jace, Liliana, etc.
        return subtype == CardSubtype.Ajani ||
               subtype == CardSubtype.Chandra ||
               subtype == CardSubtype.Jace ||
               subtype == CardSubtype.Liliana ||
               subtype == CardSubtype.Garruk ||
               subtype == CardSubtype.Nissa ||
               subtype == CardSubtype.Teferi ||
               subtype == CardSubtype.Karn ||
               subtype == CardSubtype.Ugin ||
               subtype == CardSubtype.Bolas;
    }
}
