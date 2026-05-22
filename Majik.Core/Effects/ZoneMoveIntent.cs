using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// "Would move zones" intent passed through <see cref="ReplacementBus"/>
/// before the move commits. ETB replacements ("enters tapped",
/// "enters with N counters"), "exile instead of graveyard" replacements,
/// and "if you would draw, instead..." all inspect this.
///
/// <see cref="EntersTapped"/> is a side-channel set by ETB replacements
/// that mutate the card's IsTapped after it lands.
/// </summary>
public sealed record ZoneMoveIntent(
    ICard Card,
    ZoneType FromZone,
    ZoneType ToZone,
    Player? Controller = null,
    bool EntersTapped = false,
    int PlusOneCountersOnEnter = 0);
