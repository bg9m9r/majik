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
/// Named-card factory for Glimmerlight (Foundations Jumpstart, {2}).
///
/// Artifact — Equipment. Oracle text (verified against Scryfall / the embedded
/// seed 2026-06-24):
///   "When this Equipment enters, create a 1/1 white Glimmer enchantment
///    creature token.
///    Equipped creature gets +1/+1.
///    Equip {1} ({1}: Attach to target creature you control. Equip only as a
///    sorcery.)"
///
/// The base shape (name, Artifact, Equipment subtype, {2}) is materialised from
/// the embedded JSON definition (<c>glimmerlight.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three text clauses are
/// layered on here — the JSON <c>AbilityDefinition</c> schema expresses neither
/// an ETB token-mint trigger, a parameterised attached-boost static, nor the
/// equip activated ability.
///
/// ## Implemented (v1)
///
/// - <b>ETB token mint (CR 603.1 / CR 111)</b> — "When this Equipment enters,
///   create a 1/1 white Glimmer enchantment creature token." A
///   <see cref="TriggeredAbility"/> on <see cref="Triggers.OnEnterBattlefieldSelf"/>
///   whose effect mints a 1/1 white token via
///   <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, ZoneService?)"/>
///   with the <see cref="CardSubtype.Glimmer"/> subtype and white colour
///   (CR 111.4), then additively flags <see cref="CardType.Enchantment"/> on the
///   minted token (CR 301.1 / 302.1 — the token is an enchantment creature; the
///   <see cref="TokenFactory"/> creature path stamps only Creature). Routes
///   through the supplied <see cref="ZoneService"/> so the token's
///   <see cref="Majik.Core.Events.CardMovedEvent"/> fires for downstream
///   battlefield-entry listeners (Soul Warden etc.). Same ETB-mint shape as
///   <see cref="EsikasChariotFactory"/> /
///   <see cref="ArchitectOfRestorationFactory"/>.
///
/// - <b>"Equipped creature gets +1/+1." (CR 613 Layer 7c)</b> — registered via
///   <see cref="AttachedBoostEffect"/>, the same dynamic-<see cref="Permanent.AttachedTo"/>
///   shape Bone Saw / Skullclamp / Colossus Hammer use. Gated on Glimmerlight
///   being on the battlefield AND attached, so an unequipped (or off-battlefield)
///   Glimmerlight contributes nothing and re-equipping transfers the boost
///   without re-registration.
///
/// - <b>Equip {1} (CR 702.6)</b> — activated ability via the
///   <see cref="EquipActivatedAbility"/> primitive: sorcery-speed gate,
///   "creature you control" target gathering, attach resolution, and the
///   Puresteel Paladin zero-equip cost-provider hook are all encapsulated. Same
///   shape as Bone Saw / Cranial Plating.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger and equip
///   ability are attached for inspection; the boost is NOT registered against a
///   live <see cref="ContinuousEffectsService"/> and the ETB trigger is NOT
///   enrolled with a <see cref="TriggerManager"/>. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?, ZoneService?)"/>
///   — fully wired.
/// </summary>
[CardName("Glimmerlight")]
public static class GlimmerlightFactory
{
    public const string CardName = "Glimmerlight";
    public const string Slug = "glimmerlight";
    public const string EquipCost = "{1}";

    /// <summary>
    /// Construct Glimmerlight with no live wiring. The ETB token-mint trigger
    /// and the Equip {1} ability attach structurally; the +1/+1 boost is NOT
    /// registered against any <see cref="ContinuousEffectsService"/> and the ETB
    /// trigger is NOT enrolled with a <see cref="TriggerManager"/>. Suitable for
    /// dispatcher / shape tests.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct a fully-wired Glimmerlight. When
    /// <paramref name="continuousEffects"/> is supplied the +1/+1 boost (Layer
    /// 7c) is registered against it; when <paramref name="triggers"/> is supplied
    /// the ETB token-mint trigger is enrolled so a battlefield-entry
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> routes it; the minted token
    /// flows through <paramref name="zoneService"/> when supplied so its
    /// battlefield-entry event fires.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact,
        // Equipment subtype, {2}). No abilities in the JSON — the three text
        // clauses are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.1 / CR 111.
        //   "When this Equipment enters, create a 1/1 white Glimmer enchantment
        //    creature token."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create a 1/1 white Glimmer enchantment creature token",
            () => CreateGlimmerToken(card.Controller ?? owner, zoneService));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +1/+1."
        // CR 613 Layer 7c. Gates on the source being on the battlefield AND
        // attached (AttachedBoostEffect.IsActive).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 1, toughness: 1));
        }

        // ----------------------------------------------------------------
        // Equip {1} — activated ability (CR 702.6) via the
        // EquipActivatedAbility primitive. Sorcery-speed gate, "creature you
        // control" target gathering, attach resolution, and the Puresteel
        // zero-equip cost-provider hook are all encapsulated.
        // ----------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — mint a 1/1 white Glimmer enchantment creature token
    /// onto <paramref name="controller"/>'s battlefield. The
    /// <see cref="TokenFactory"/> creature path stamps only
    /// <see cref="CardType.Creature"/>, so additively flag
    /// <see cref="CardType.Enchantment"/> on the minted token (CR 301.1 /
    /// 302.1 — the token is an enchantment creature). Routes through
    /// <paramref name="zoneService"/> when supplied so the token's
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> fires. Public so tests /
    /// bots can drive the mint directly.
    /// </summary>
    public static Creature CreateGlimmerToken(Player controller, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var token = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec(
                Name: "Glimmer",
                Power: 1,
                Toughness: 1,
                Subtypes: new[] { CardSubtype.Glimmer },
                Keywords: null,
                Colors: new[] { ManaColor.White }),
            controller,
            zoneService);

        // CR 301.1 / 302.1 — the token is an enchantment creature; flag the
        // Enchantment type additively (the TokenFactory creature path stamps
        // only Creature).
        token.AddCardType(CardType.Enchantment);

        return token;
    }
}
