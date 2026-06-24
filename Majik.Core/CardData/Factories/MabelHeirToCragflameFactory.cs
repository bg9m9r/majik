using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mabel, Heir to Cragflame (Bloomburrow, {1}{R}{W}).
///
/// Legendary Creature — Mouse Soldier 3/3. Oracle text (verified against
/// Scryfall, 2026-06-24):
///   "Other Mice you control get +1/+1.
///    When Mabel enters, create Cragflame, a legendary colorless Equipment
///    artifact token with 'Equipped creature gets +1/+1 and has vigilance,
///    trample, and haste' and equip {2}."
///
/// The base shape (name, Legendary supertype, Creature, Mouse + Soldier
/// subtypes, {1}{R}{W}, 3/3) is materialised from the embedded JSON definition
/// (<c>mabel-heir-to-cragflame.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The lord static and the ETB
/// token-creation trigger are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express tribal lords or ETB
/// token-minting triggers (same posture as
/// <see cref="GoblinTrashmasterFactory"/> / <see cref="EsikasChariotFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>"Other Mice you control get +1/+1" (CR 613.7c — Layer 7c)</b> — a
///   <see cref="LordStaticEffect"/> with <c>matchingSubtype: Mouse</c>,
///   <c>includeSelf: false</c> (the printed "Other" clause), controller-scoped
///   (not <c>opponentsOnly</c>). Verbatim Goblin Trashmaster lord static minus
///   the granted keywords. Registers only when a live
///   <see cref="ContinuousEffectsService"/> is supplied;
///   <see cref="LordStaticEffect.IsActive"/> short-circuits on LTB.
///
/// - <b>"When Mabel enters, create Cragflame …" (CR 603.1 / 603.6a)</b> — a
///   <see cref="TriggeredAbility"/> gated on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution it mints the
///   Cragflame Equipment token (see <see cref="CreateCragflame"/>) onto the
///   controller's battlefield. Same self-ETB-creates-a-token shape as Esika's
///   Chariot.
///
/// - <b>Cragflame token (CR 111.10 / CR 301.5 / CR 702.6)</b> — a legendary
///   colourless Equipment artifact token built as a plain
///   <see cref="Artifact"/> (the same primitive composition the Vulshok
///   Morningstar / Shadowspear Equipment factories use), carrying:
///     - Static "Equipped creature gets +1/+1" via
///       <see cref="AttachedBoostEffect"/> at Layer 7c (CR 613.7c).
///     - Granted "vigilance, trample, and haste" (CR 613.1c — Layer 6 ability
///       addition) via a parallel <see cref="AttachedBoostEffect"/> with
///       <c>grantedKeywords</c> registered at <see cref="Layer.Abilities"/>.
///       Identical paired-effect shape to Shadowspear's
///       "+1/+1 and has trample and lifelink."
///     - Equip {2} via the shared <see cref="EquipActivatedAbility"/> primitive
///       (CR 702.6), with the Puresteel Paladin zero-equip cost-provider hook.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The lord static is NOT
///   registered (no effects service) and the ETB trigger is attached for shape
///   inspection but not registered with a <see cref="TriggerManager"/>; if it
///   ever fired it would mint a Cragflame whose equip/boost are likewise
///   unwired. This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?, ZoneService?)"/>
///   — fully wired. <paramref name="effects"/> registers the lord static and
///   each Cragflame's boost/keyword grant; <paramref name="triggers"/>
///   registers the ETB trigger so <see cref="Majik.Core.Events.CardMovedEvent"/>
///   auto-queues it; <paramref name="zones"/> routes the token onto the
///   battlefield via <see cref="ZoneService"/> so its ETB publishes
///   <see cref="Majik.Core.Events.CardMovedEvent"/> (downstream ETB listeners
///   fire).
/// </summary>
[CardName(CardName)]
public static class MabelHeirToCragflameFactory
{
    public const string CardName = "Mabel, Heir to Cragflame";
    public const string Slug = "mabel-heir-to-cragflame";

    /// <summary>The Equipment token Mabel mints (CR 111.10).</summary>
    public const string TokenName = "Cragflame";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Construct Mabel with no live runtime wiring (the dispatcher / shape
    /// path). The lord static is not registered and the ETB trigger is attached
    /// for shape observability but not registered with a
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, triggers: null, zones: null);

    /// <summary>
    /// Construct Mabel, Heir to Cragflame. When <paramref name="effects"/> is
    /// supplied the "Other Mice you control get +1/+1" lord static is
    /// registered, plus each minted Cragflame's +1/+1 boost and keyword grant.
    /// When <paramref name="triggers"/> is supplied the ETB trigger is
    /// registered. When <paramref name="zones"/> is supplied the Cragflame token
    /// is moved onto the battlefield through <see cref="ZoneService"/> so its
    /// ETB fires (CR 603.6a).
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Mouse + Soldier, {1}{R}{W}, 3/3). No abilities in the JSON —
        // the lord static + ETB trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "Other Mice you control get +1/+1." CR 613.7c (Layer 7c). Verbatim
        // Goblin Trashmaster lord static minus the granted keywords.
        // includeSelf is false (the "Other" clause); controller-scoped (not
        // opponentsOnly). Multiple copies would stack (legend rule keeps it
        // to one in practice).
        // ----------------------------------------------------------------
        if (effects != null)
        {
            effects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Mouse,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false));
        }

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.1.
        //   "When Mabel enters, create Cragflame, a legendary colorless
        //    Equipment artifact token …"
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create the Cragflame Equipment token",
            () => CreateCragflame(card.Controller ?? owner, effects, zones));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Mint the Cragflame Equipment token (CR 111.10 — a legendary colourless
    /// Equipment artifact token) onto <paramref name="controller"/>'s
    /// battlefield. The token carries:
    ///   - "Equipped creature gets +1/+1" (Layer 7c — CR 613.7c),
    ///   - granted "vigilance, trample, and haste" (Layer 6 — CR 613.1c),
    ///   - equip {2} (CR 702.6) via the shared
    ///     <see cref="EquipActivatedAbility"/> primitive.
    /// When <paramref name="effects"/> is supplied the boost / keyword grant are
    /// registered (they gate on the token being on the battlefield AND attached,
    /// per <see cref="AttachedBoostEffect.IsActive"/>). When
    /// <paramref name="zones"/> is supplied the token enters via
    /// <see cref="ZoneService"/> so its ETB publishes
    /// <see cref="Majik.Core.Events.CardMovedEvent"/>.
    /// Exposed for tests; mirrors the closure baked into the ETB trigger.
    /// </summary>
    public static Artifact CreateCragflame(
        Player controller,
        ContinuousEffectsService? effects,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // CR 111.10 — a legendary colourless Equipment artifact token.
        var token = new Artifact(
            name: TokenName,
            manaCost: "",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Equipment })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        token.SetTokenColors(Array.Empty<ManaColor>()); // colourless (CR 105.2c)

        // Static — "Equipped creature gets +1/+1 and has vigilance, trample,
        // and haste." Two AttachedBoostEffects: Layer 7c for the +1/+1, Layer 6
        // for the granted keywords (CR 613.7c + CR 613.1c). Identical
        // paired-effect shape to Shadowspear. Both gate on Cragflame being on
        // the battlefield AND attached (AttachedBoostEffect.IsActive).
        if (effects != null)
        {
            effects.Register(new AttachedBoostEffect(token, power: 1, toughness: 1));
            effects.Register(new AttachedBoostEffect(
                source: token,
                power: 0,
                toughness: 0,
                grantedKeywords: new[] { "Vigilance", "Trample", "Haste" },
                layer: Layer.Abilities));
        }

        // Equip {2} — activated ability (CR 702.6) via the shared
        // EquipActivatedAbility primitive (sorcery-speed gate, "creature you
        // control" target gathering, attach resolution, and the Puresteel
        // Paladin zero-equip cost-provider hook are encapsulated).
        token.AddAbility(new EquipActivatedAbility(
            source: token,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider));

        // Put the token onto the battlefield. Mirror the TokenFactory artifact
        // helpers: enter via a Library sentinel so ZoneService validates the
        // from-zone; CardMovedEvent fires for downstream ETB listeners.
        token.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(token);
        if (zones != null)
        {
            zones.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.MoveCard(token, ZoneType.Library, ZoneType.Battlefield);
            token.SetZone(ZoneType.Battlefield);
        }

        return token;
    }
}
