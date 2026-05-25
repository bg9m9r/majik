using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pinnacle Emissary (Edge of Eternities, {1}{U}{R}).
///
/// Artifact Creature — Robot 3/3. Oracle text (verified Scryfall
/// 2026-05-24):
///   "Whenever you cast an artifact spell, create a 1/1 colorless Drone
///    artifact creature token with flying and \"This token can block only
///    creatures with flying.\"
///    Warp {U/R} (You may cast this card from your hand for its warp cost.
///    Exile this creature at the beginning of the next end step, then you
///    may cast it from exile on a later turn.)"
///
/// ## Implemented (v1)
/// - 3/3 Robot Artifact Creature shape — concrete <see cref="Creature"/>
///   instance with <c>AddCardType(CardType.Artifact)</c> on top (mirrors
///   Kappa Cannoneer / Esika's Chariot / Scion of Draco — the Artifact
///   Creature pattern).
/// - <b>Artifact-cast token trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/>
///   filtered to (spell.Controller == this card's controller, spell.Card
///   HasType Artifact). Resolution creates a 1/1 colorless Drone token
///   with the Flying keyword marker via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. The token is built
///   as a Creature with the <see cref="CardSubtype.Drone"/> subtype,
///   then promoted to Artifact via the same
///   <c>AddCardType(CardType.Artifact)</c> pattern Pinnacle Emissary
///   itself uses (CR 111.4 — token types/subtypes are declared by the
///   creating effect). Self-cast satisfies the predicate (CR 112.1a —
///   Pinnacle Emissary itself is an artifact spell), so the trigger
///   fires on its own cast.
///
/// ## Deferred (v1 gaps)
/// - <b>Warp alt-cost (CR 702.??? — new Edge of Eternities keyword)</b>:
///   the engine has no Warp primitive yet (no
///   <c>WarpAlternativeCost</c>, no "exile-at-next-end-step + cast-from-
///   exile-later" lifecycle, no delayed Suspend-style triggered castor).
///   Warp would require a new alt-cost (the {U/R} payment to cast from
///   hand), an end-step exile trigger (similar to Plot / Suspend's
///   delayed-trigger family), and a runtime cast-from-exile grant that
///   doesn't auto-expire (Plot's lifecycle, but lasting until the card
///   is cast from exile — not just one turn). Flagged for a follow-up
///   infra PR (parallels Suspend → Plot → Warp evolution). v1 ships
///   Pinnacle Emissary at its printed {1}{U}{R} cast cost and surfaces
///   <see cref="Card.AddAbility"/> with a <see cref="KeywordAbility"/>
///   marker "Warp" so card-text inspection sees the keyword (same
///   posture as Improvise / Convoke markers).
/// - <b>"This token can block only creatures with flying" blocking
///   restriction on the Drone</b>: the engine has no per-token blocking-
///   restriction primitive yet. v1 ships the Drone with Flying as a
///   keyword marker but does NOT enforce the "can only block flyers"
///   restriction at combat block validation. Flagged in
///   <see cref="CreateDroneToken"/>'s xmldoc as deferred — same posture
///   as Inkmoth Nexus' Infect marker (keyword on the card; mechanic
///   pipeline not wired). When a CombatRestrictionEffect for
///   "can only block flying" lands (parallels Kappa's CannotBeBlocked
///   restriction effect), the Drone token picks up the rider for free.
/// - <b>Token colour identity</b>: explicit empty
///   <see cref="TokenFactory.TokenSpec.Colors"/> = <c>[]</c> stamps
///   "colourless" via <see cref="Card.TokenColorsOverride"/> (CR 105 /
///   CR 111.4). Drone is colourless per oracle.
/// </summary>
[CardName("Pinnacle Emissary")]
public static class PinnacleEmissaryFactory
{
    public const string CardName = "Pinnacle Emissary";
    public const string PrintedManaCost = "{1}{U}{R}";
    public const int Power = 3;
    public const int Toughness = 3;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Pinnacle Emissary with no live bus / trigger-manager
    /// wiring. The cast-trigger is attached to the card shape so
    /// dispatcher / structural tests can observe it; live firing
    /// requires the (owner, eventBus, triggers) overload. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Pinnacle Emissary. When <paramref name="triggers"/> is
    /// supplied the cast-trigger is registered so the bus surfaces it
    /// on a matching <see cref="SpellCastEvent"/>; otherwise the trigger
    /// is attached structurally to the card but not registered for
    /// firing. <paramref name="zoneService"/> is forwarded to the
    /// Drone-token creation path so <see cref="CardMovedEvent"/> fires
    /// (downstream ETB triggers like Soul Warden see the Drone).
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Robot });

        // CR 301.1 + CR 302.1 — Artifact Creature: declare Creature via
        // the concrete <see cref="Creature"/> subclass, then add the
        // Artifact card type on top so HasType(Artifact) returns true.
        // Mirrors Kappa Cannoneer / Esika's Chariot / Scion of Draco.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Warp keyword marker (CR 702.??? — Edge of Eternities). The
        // mechanic (alt-cost + exile-at-end-step + cast-from-exile-later)
        // is deferred; the marker surfaces the keyword for card-text
        // inspection, parity with Improvise / Convoke / Delve markers.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Warp", card, owner));

        // ----------------------------------------------------------------
        // CR 603.1 — "Whenever you cast an artifact spell, create a 1/1
        // colorless Drone artifact creature token with flying and \"This
        // token can block only creatures with flying.\""
        //
        // Predicate: spell controller matches AND spell has Artifact
        // card type (CR 300.1). CR 112.1a — Pinnacle Emissary itself is
        // an artifact spell while on the stack, so the cast that puts
        // Pinnacle Emissary onto the battlefield ALSO satisfies the
        // predicate (the trigger fires from the stack object's
        // controller-spell-cast event, then the source resolves and
        // enters before the trigger goes on the stack itself — same
        // posture as Goblin Rabblemaster / Young Pyromancer-style
        // "whenever you cast X" predicates that capture the source's
        // own cast).
        // ----------------------------------------------------------------
        var droneCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && e.Spell.Card.HasType(CardType.Artifact));

        var droneEffect = new Effect(
            $"{CardName}: create 1/1 colourless Drone artifact creature token with flying (whenever you cast an artifact spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateDroneToken(controller, zoneService);
            });

        var droneTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: droneCondition,
            effects: new IEffect[] { droneEffect });

        card.AddAbility(droneTrigger);
        triggers?.RegisterTriggeredAbility(droneTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.4 — create one 1/1 colourless Drone artifact creature
    /// token with the Flying keyword under <paramref name="controller"/>'s
    /// control.
    ///
    /// <para>v1 gaps:</para>
    /// <list type="bullet">
    ///   <item>Printed "This token can block only creatures with flying"
    ///     blocking restriction is NOT enforced — no combat-block
    ///     restriction primitive for "can only block X" yet. Flying is
    ///     stamped as a keyword marker via
    ///     <see cref="TokenFactory.TokenSpec.Keywords"/>; the
    ///     restriction rider is documented but deferred. When the
    ///     restriction primitive lands (parallels Kappa Cannoneer's
    ///     <c>CombatRestrictionEffect</c> family) the Drone picks up
    ///     the rider for free.</item>
    ///   <item>Token colour identity is colourless (explicit empty
    ///     <see cref="TokenFactory.TokenSpec.Colors"/> = <c>[]</c>).</item>
    /// </list>
    /// </summary>
    public static Creature CreateDroneToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Drone",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Drone },
            // Flying keyword marker. The "can block only creatures with
            // flying" restriction is documented but unenforced — see
            // class xmldoc.
            Keywords: new[] { "Flying" },
            // CR 105 / CR 111.4 — printed "colourless" token. Explicit
            // empty colour list stamps the colourless override on the
            // resulting Card.TokenColorsOverride.
            Colors: Array.Empty<ManaColor>());

        var drone = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // CR 301.1 + CR 302.1 — Artifact creature token. Promote to
        // Artifact card type so HasType(Artifact) returns true (mirrors
        // the AddCardType pattern used on Pinnacle Emissary itself and
        // by Karn Scion of Urza's Construct tokens).
        drone.AddCardType(CardType.Artifact);

        return drone;
    }
}
