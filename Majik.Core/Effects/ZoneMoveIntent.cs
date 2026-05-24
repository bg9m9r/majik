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
///
/// <see cref="WasCast"/> is true when the card arrived via a normal
/// <see cref="Majik.Core.Game.SpellCastFlow"/> cast (CR 114.1a). It is
/// false for reanimation, blinks, Sneak Attack / Through the Breach
/// cheats, Aether Vial puts, and every other "put onto the battlefield"
/// path. Containment Priest and similar effects consult this flag.
/// </summary>
public sealed record ZoneMoveIntent(
    ICard Card,
    ZoneType FromZone,
    ZoneType ToZone,
    Player? Controller = null,
    bool EntersTapped = false,
    int PlusOneCountersOnEnter = 0,
    bool WasCast = false);
