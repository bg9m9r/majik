using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scute Swarm (Zendikar Rising, {1}{G}).
///
/// Creature — Insect 1/1. Oracle text (verified against Scryfall):
///   "Landfall — Whenever a land you control enters, create a 1/1 green
///    Insect creature token. If you control six or more lands, create a
///    token that's a copy of this creature instead."
///
/// Same landfall trigger plumbing as <see cref="PlatedGeopedeFactory"/> /
/// <see cref="SteppeLynxFactory"/> (the shared
/// <see cref="Triggers.OnLandEntersUnderControl"/> predicate, CR 603.6a;
/// base shape materialised from the embedded JSON definition
/// <c>scute-swarm.json</c> via <see cref="CardDefinitionLoader.FromEmbeddedResource"/>
/// + <see cref="CardDefinitionFactory.Build"/>), but the resolve body mints
/// a token instead of pumping: a vanilla 1/1 green Insect under the normal
/// case, or — when the controller commands six or more lands — a token
/// that's a copy of Scute Swarm itself (CR 706.2). The self-copy is the
/// engine of the card's exponential snowball: each copy is itself a Scute
/// Swarm carrying the same landfall trigger, so the next land drop doubles
/// the swarm.
///
/// ## Implemented (v1)
/// - 1/1 Creature — Insect, mana cost {1}{G}, owner / controller wired.
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 702.142) —
///   fires on a <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to
///   "a land entering the battlefield under the controller's control" via the
///   shared <see cref="Triggers.OnLandEntersUnderControl"/> predicate. No
///   <see cref="TargetRequest"/>: the token creation names no target
///   (CR 603.6a — the intervening "if" is checked on resolution).
/// - <b>Resolve — mint a token</b> (CR 111 / CR 706.2): if the controller
///   controls fewer than <see cref="LandThreshold"/> lands, create one 1/1
///   green Insect creature token via <see cref="TokenFactory.CreateOnBattlefield"/>.
///   Otherwise (six or more lands) create a token that's a copy of Scute Swarm
///   instead — a fresh Scute Swarm built through this same factory, flagged
///   <see cref="Permanent.IsToken"/> and registered with the live
///   <see cref="TriggerManager"/> so the copy's OWN landfall trigger fires on
///   subsequent land drops (the exponential-doubling behaviour). The land
///   count is read at RESOLUTION (CR 603.4) so a land that entered after the
///   trigger but before resolution is counted.
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the landfall trigger for inspection but does not register
///   it with a bus, and the self-copy branch cannot register the copy's
///   trigger (no live <see cref="TriggerManager"/>). Use the
///   <see cref="Create(Player, ZoneService, TriggerManager)"/> overload for
///   live firing and self-copy registration.
/// - <b>Layer-1 copy fidelity</b>: the self-copy is rebuilt from the printed
///   Scute Swarm definition, not a true CR 706.2 snapshot of the source's
///   current characteristics (counters / external pumps on the original are
///   not carried). Scute Swarm has no such modifiers in normal play, so the
///   rebuild is faithful for this card; aligns with the v1 lossy
///   <see cref="Majik.Core.Effects.CopyEffect"/> posture.
/// </summary>
[CardName("Scute Swarm")]
public static class ScuteSwarmFactory
{
    public const string CardName = "Scute Swarm";
    public const string Slug = "scute-swarm";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>CR 603.4 — "If you control six or more lands" intervening-if
    /// threshold; checked on resolution.</summary>
    public const int LandThreshold = 6;

    /// <summary>Name of the vanilla token minted under the normal case.</summary>
    public const string InsectTokenName = "Insect";

    /// <summary>
    /// Construct Scute Swarm with no live <see cref="ZoneService"/> /
    /// <see cref="TriggerManager"/> wiring. The landfall trigger is attached
    /// for shape inspection but not registered with a bus, and the self-copy
    /// branch falls back to a direct battlefield placement without trigger
    /// registration. Suitable for shape / dispatcher tests. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Scute Swarm. When <paramref name="triggers"/> is supplied the
    /// landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering under
    /// the controller's control automatically queues the ability; the same
    /// manager is used to register the OWN landfall trigger of any self-copy
    /// token minted on the six-or-more-lands branch (CR 706.2) so the swarm
    /// snowballs on later land drops. When <paramref name="zones"/> is supplied
    /// minted tokens route through <see cref="TokenFactory.CreateOnBattlefield"/>
    /// using the service so each token publishes
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> on battlefield entry.
    /// </summary>
    public static Creature Create(Player owner, ZoneService? zones, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Insect subtype, {1}{G}, 1/1). The JSON carries no abilities —
        // the landfall trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / 603.6a / CR 702.142.
        //   "Whenever a land you control enters, create a 1/1 green Insect
        //    creature token. If you control six or more lands, create a
        //    token that's a copy of this creature instead."
        // Predicate shared with Steppe Lynx / Plated Geopede / Lotus Cobra.
        // No target: the effect names no target. The intervening "if" land
        // count is checked at RESOLUTION (CR 603.4), reading the controller's
        // live battlefield.
        // ----------------------------------------------------------------
        var landfallEffect = new Effect(
            $"{CardName}: landfall — create a 1/1 green Insect token (a copy of this creature if you control {LandThreshold}+ lands)",
            () =>
            {
                var controller = card.Controller ?? owner;
                if (CountLands(controller) >= LandThreshold)
                {
                    // CR 706.2 — create a token that's a copy of this creature.
                    CreateScuteSwarmCopyToken(controller, zones, triggers);
                }
                else
                {
                    // CR 111 — create one 1/1 green Insect creature token.
                    TokenFactory.CreateOnBattlefield(VanillaInsectSpec, controller, zones);
                }
            });

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { landfallEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        return card;
    }

    /// <summary>CR 111.4 — the 1/1 green Insect token spec minted on the
    /// normal (fewer-than-six-lands) landfall branch.</summary>
    private static TokenFactory.TokenSpec VanillaInsectSpec => new(
        Name: InsectTokenName,
        Power: 1,
        Toughness: 1,
        Subtypes: new[] { CardSubtype.Insect },
        Colors: new[] { ManaColor.Green });

    /// <summary>
    /// CR 603.4 intervening-if count — the number of lands
    /// <paramref name="controller"/> controls, read at resolution.
    /// </summary>
    private static int CountLands(Player controller) =>
        controller.Zones.Battlefield.GetCards().Count(c => c.HasType(CardType.Land));

    /// <summary>
    /// CR 706.2 — create a token that's a copy of Scute Swarm. Rebuilt through
    /// this same factory so the copy carries Scute Swarm's own landfall trigger
    /// (the source of the exponential snowball), flagged
    /// <see cref="Permanent.IsToken"/> and put onto the battlefield under
    /// <paramref name="controller"/>'s control. When a live
    /// <see cref="TriggerManager"/> is supplied the copy's landfall trigger is
    /// registered + bound so it fires on later land drops; without one the
    /// copy is still minted (shape tests) but its trigger is inert.
    /// </summary>
    private static Creature CreateScuteSwarmCopyToken(
        Player controller, ZoneService? zones, TriggerManager? triggers)
    {
        // Build a fresh Scute Swarm carrying its own landfall trigger, wired to
        // the same TriggerManager so the copy snowballs on subsequent landfall.
        var copy = Create(controller, zones, triggers);
        copy.IsToken = true;            // CR 111.1 — it's a token, not a real card.
        copy.HasSummoningSickness = true; // CR 302.6 — entered this turn.

        // CR 111.4 — green Insect; the printed Scute Swarm shape already gives
        // Creature — Insect, but tokens carry colour explicitly (no mana cost
        // to infer from once on the battlefield as a copy/token surface).
        copy.SetTokenColors(new[] { ManaColor.Green });

        // Tokens enter the battlefield directly (CR 111.6) using the
        // sentinel-library pattern ZoneService.MoveCard validates against, so
        // CardMovedEvent fires for downstream ETB listeners.
        copy.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(copy);
        if (zones != null)
        {
            zones.MoveCardTo(copy, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(copy);
            copy.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(copy);
        }

        // CR 603.6a — bind the copy so its landfall trigger observes future
        // land ETBs (no-op without a live TriggerManager).
        triggers?.BindCard(copy);

        return copy;
    }
}
