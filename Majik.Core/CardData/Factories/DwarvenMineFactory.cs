using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dwarven Mine (Throne of Eldraine) — the red member
/// of the Eldraine "creature land" cycle (Idyllic Grange, Witch's Cottage,
/// Mystic Sanctuary, Dwarven Mine, Gingerbread Cabin). Oracle text (verified
/// against Scryfall 2026-06-14):
///   "({T}: Add {R}.)
///    This land enters tapped unless you control three or more other
///    Mountains.
///    When this land enters untapped, create a 1/1 red Dwarf creature token."
///
/// Scryfall type line: <c>Land — Mountain</c>. Dwarven Mine <b>is</b> a
/// Mountain (it carries the Mountain land subtype), so its {T}: Add {R} is the
/// intrinsic Mountain mana ability (CR 305.6) — printed in reminder text. The
/// card itself is excluded from its own "three or more <i>other</i> Mountains"
/// count via reference equality.
///
/// <para>
/// The Land shell — name, the Mountain subtype, and the {T}: Add {R} mana
/// ability (CR 605.1 — mana abilities don't use the stack) — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/dwarven-mine.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, mirroring the
/// JSON-driven posture of <see cref="CinderGladeFactory"/>. The two printed
/// behaviours that outgrow the JSON schema (the count-conditional enters-tapped
/// replacement and the ETB-untapped token trigger) are layered on top here.
/// </para>
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — nonbasic <see cref="Land"/> with the Mountain
///   subtype, no supertype, no mana cost.
/// - <b>{T}: Add {R}</b> — single <see cref="ManaAbility"/> from the JSON def
///   (CR 605.1 / CR 305.6 intrinsic Mountain mana ability).
/// - <b>Enters tapped unless you control three or more other Mountains
///   (CR 614.1c)</b> — a <see cref="ConditionalEntersTappedReplacement"/> on
///   the supplied <see cref="ReplacementBus"/>. The predicate counts the
///   controller's battlefield permanents carrying the
///   <see cref="CardSubtype.Mountain"/> subtype, excluding this card via
///   reference equality (so dual lands with the Mountain subtype, snow
///   Mountains, etc. all count; the entering Mine never counts itself). Same
///   count-predicate shape as <see cref="CinderGladeFactory"/>, swapping the
///   "two or more basic lands" check for "three or more other Mountains".
/// - <b>When this land enters untapped, create a 1/1 red Dwarf token
///   (CR 603.6a)</b> — a <see cref="TriggeredAbility"/> over
///   <see cref="CardMovedEvent"/> gated to this card entering the battlefield,
///   AND additionally gated on the land having entered <i>untapped</i>. The
///   "untapped" condition is read off <see cref="Permanent.IsTapped"/> at
///   trigger-evaluation time: <see cref="ZoneService.CommitMove"/> applies the
///   enters-tapped replacement (taps the permanent) BEFORE it publishes the
///   <see cref="CardMovedEvent"/>, so <c>!land.IsTapped</c> at condition time
///   faithfully reflects whether the Mine entered untapped. The token is minted
///   via the shared <see cref="TokenFactory"/> primitive (1/1, red, Dwarf),
///   same token-mint shape as <see cref="StormscaleScionFactory"/>.
///
/// ## Notes
/// - The trigger is NOT an intervening-"if": Dwarven Mine's "When this land
///   enters untapped" describes the trigger <i>event</i> (it only triggers at
///   all when the land entered untapped), so checking the tapped state once at
///   condition-evaluation time is the correct model — there is no re-check at
///   resolution.
/// - The token-mint effect lambda reads <see cref="Card.Controller"/> at
///   resolution so a control-changed Mine still hands the token to its current
///   controller (CR 603.6d controller-at-trigger would be more precise, but the
///   Mine never changes control between ETB and the same-event trigger).
/// - Single-arg dispatcher path constructs without a
///   <see cref="ReplacementBus"/> / <see cref="TriggerManager"/> /
///   <see cref="ZoneService"/> — both the ETB-tapped replacement and the token
///   trigger registration are omitted (shape-only posture matching every other
///   ETB-replacement factory's single-arg path); the mana ability is still
///   attached, and the trigger is still built + attached to the card so its
///   shape is inspectable.
/// </summary>
[CardName("Dwarven Mine")]
public static class DwarvenMineFactory
{
    public const string CardName = "Dwarven Mine";
    public const string Slug = "dwarven-mine";

    /// <summary>Number of OTHER Mountains the controller must control for the
    /// Mine to enter untapped (CR 614.1c).</summary>
    public const int RequiredOtherMountains = 3;

    /// <summary>Power of the minted Dwarf token.</summary>
    public const int TokenPower = 1;
    /// <summary>Toughness of the minted Dwarf token.</summary>
    public const int TokenToughness = 1;
    /// <summary>Name of the minted Dwarf token.</summary>
    public const string TokenName = "Dwarf";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Dwarven Mine owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no ETB-tapped replacement
    /// and no token-trigger registration wired). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, triggers: null, zones: null);

    /// <summary>Construct Dwarven Mine with an optional
    /// <see cref="ReplacementBus"/> for the count-conditional enters-tapped
    /// replacement (CR 614.1c). The token trigger is built + attached but not
    /// registered (no <see cref="TriggerManager"/>).</summary>
    public static Land Create(Player owner, ReplacementBus? replacements) =>
        Create(owner, replacements, triggers: null, zones: null);

    /// <summary>Construct a fully-wired Dwarven Mine.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "enters tapped unless you
    /// control three or more other Mountains" replacement is registered
    /// (CR 614.1c). May be null.</param>
    /// <param name="triggers">When supplied, the ETB-untapped token trigger is
    /// registered so it fires on a qualifying <see cref="CardMovedEvent"/>. May
    /// be null (the trigger is still attached to the card).</param>
    /// <param name="zones">Zone service the minted Dwarf token enters through so
    /// <see cref="CardMovedEvent"/> fires (Soul Warden etc.). May be null —
    /// <see cref="TokenFactory"/> then falls back to direct battlefield
    /// placement (CR 111.6).</param>
    public static Land Create(
        Player owner,
        ReplacementBus? replacements,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land, Mountain
        // subtype, {T}: Add {R}). The conditional enters-tapped replacement and
        // the ETB-untapped token trigger are layered on below.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Enters tapped unless you control three or more OTHER Mountains
        // (CR 614.1c). Predicate returns true => untapped, false => tapped.
        // "Mountain" matched by the Mountain land subtype (CR 205.3i), self
        // excluded by reference equality. Same count-predicate shape as
        // CinderGladeFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    CountControllerOtherMountains(controller, self) >= RequiredOtherMountains));
        }

        // ----------------------------------------------------------------
        // When this land enters untapped, create a 1/1 red Dwarf token
        // (CR 603.6a). The trigger fires only when the Mine entered
        // untapped: ZoneService taps the permanent (from the enters-tapped
        // replacement) BEFORE publishing CardMovedEvent, so reading
        // !land.IsTapped at condition-evaluation time is faithful.
        // ----------------------------------------------------------------
        var tokenTrigger = BuildEntersUntappedTokenTrigger(land, owner, zones);
        land.AddAbility(tokenTrigger);
        triggers?.RegisterTriggeredAbility(tokenTrigger);

        return land;
    }

    /// <summary>
    /// Build the "When this land enters untapped, create a 1/1 red Dwarf token"
    /// triggered ability (CR 603.6a). The trigger condition matches this card
    /// entering the battlefield AND the land being untapped at that moment.
    /// </summary>
    private static TriggeredAbility BuildEntersUntappedTokenTrigger(
        Land land,
        Player controller,
        ZoneService? zones)
    {
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            ReferenceEquals(e.Card, land)
            && e.ToZone == ZoneType.Battlefield
            // "enters UNTAPPED" — ZoneService has already applied the
            // enters-tapped replacement by the time CardMovedEvent fires.
            && !land.IsTapped);

        var mintEffect = new Effect(
            $"{CardName}: create a {TokenPower}/{TokenToughness} red Dwarf creature token",
            () =>
            {
                var bfController = land.Controller ?? controller;

                // CR 111 — 1/1 red Dwarf creature token (no keywords).
                var spec = new TokenFactory.TokenSpec(
                    Name: TokenName,
                    Power: TokenPower,
                    Toughness: TokenToughness,
                    Subtypes: new[] { CardSubtype.Dwarf },
                    Keywords: null,
                    Colors: new[] { ManaColor.Red });

                TokenFactory.CreateOnBattlefield(spec, bfController, zones);
            });

        return new TriggeredAbility(
            source: land,
            controller: controller,
            condition: condition,
            effects: new IEffect[] { mintEffect },
            activeZones: new[] { ZoneType.Battlefield });
    }

    private static int CountControllerOtherMountains(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Mountain));
}
