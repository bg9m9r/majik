using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Zones;

/// <summary>
/// Manages all zones for a player.
/// </summary>
public class ZoneManager
{
    private readonly Dictionary<ZoneType, IZone> _zones = new();
    private readonly Player _player;
    private readonly IEventBus? _eventBus;

    public ZoneManager(Player player, IEventBus? eventBus = null)
    {
        _player = player;
        _eventBus = eventBus;
        
        // Initialize all zones
        InitializeZones();
    }

    private void InitializeZones()
    {
        _zones[ZoneType.Library] = new Zone(ZoneType.Library, $"{_player.Name}'s Library");
        _zones[ZoneType.Hand] = new Zone(ZoneType.Hand, $"{_player.Name}'s Hand");
        _zones[ZoneType.Battlefield] = new Zone(ZoneType.Battlefield, $"{_player.Name}'s Battlefield");
        _zones[ZoneType.Graveyard] = new Zone(ZoneType.Graveyard, $"{_player.Name}'s Graveyard");
        _zones[ZoneType.Exile] = new Zone(ZoneType.Exile, $"{_player.Name}'s Exile");
        _zones[ZoneType.Stack] = new Zone(ZoneType.Stack, $"{_player.Name}'s Stack");
        _zones[ZoneType.Command] = new Zone(ZoneType.Command, $"{_player.Name}'s Command");
    }

    /// <summary>
    /// Get a zone by type.
    /// </summary>
    public IZone GetZone(ZoneType zoneType)
    {
        return _zones[zoneType];
    }

    /// <summary>
    /// Move a card from one zone to another.
    /// </summary>
    public void MoveCard(ICard card, ZoneType fromZone, ZoneType toZone)
    {
        var sourceZone = _zones[fromZone];
        var targetZone = _zones[toZone];

        if (sourceZone.RemoveCard(card))
        {
            targetZone.AddCard(card);
            _eventBus?.Publish(new CardMovedEvent(card, fromZone, toZone));
        }
    }

    /// <summary>
    /// Get the library zone.
    /// </summary>
    public IZone Library => _zones[ZoneType.Library];

    /// <summary>
    /// Get the hand zone.
    /// </summary>
    public IZone Hand => _zones[ZoneType.Hand];

    /// <summary>
    /// Get the battlefield zone.
    /// </summary>
    public IZone Battlefield => _zones[ZoneType.Battlefield];

    /// <summary>
    /// Get the graveyard zone.
    /// </summary>
    public IZone Graveyard => _zones[ZoneType.Graveyard];

    /// <summary>
    /// Get the exile zone.
    /// </summary>
    public IZone Exile => _zones[ZoneType.Exile];

    /// <summary>
    /// Get the stack zone.
    /// </summary>
    public IZone Stack => _zones[ZoneType.Stack];

    /// <summary>
    /// Get the command zone.
    /// </summary>
    public IZone Command => _zones[ZoneType.Command];
}
