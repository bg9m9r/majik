namespace Majik.Core.Zones;

/// <summary>
/// Enumeration of zone types in Magic: The Gathering.
/// </summary>
public enum ZoneType
{
    Library,
    Hand,
    Battlefield,
    Graveyard,
    Exile,
    Stack,
    Command,

    /// <summary>
    /// CR 100.4 / CR 702.139 — Sideboard. A "zone outside the game"
    /// (CR 400.11) that holds cards available before the game starts:
    /// the up-to-15-card MTG sideboard plus the single Companion slot
    /// (CR 702.139a). The engine tracks this zone explicitly so
    /// Companion's once-per-game "cast from outside the game" pipeline
    /// has a concrete source to draw from.
    /// </summary>
    Sideboard
}
