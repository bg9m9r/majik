using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cartouche of Solidarity (Amonkhet, {W}).
///
/// Enchantment — Aura Cartouche. Oracle text (verified against Scryfall):
///   "Enchant creature you control
///    When this Aura enters, create a 1/1 white Warrior creature token with
///    vigilance.
///    Enchanted creature gets +1/+1 and has first strike."
///
/// ## Implemented (v1)
/// - Card identity (Enchantment, subtypes Aura + Cartouche, mana cost {W},
///   white) materialised from the embedded JSON definition
///   (<c>cartouche-of-solidarity.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="RenegadeMapFactory"/>. The Cartouche subtype (CR 205.3h)
///   was added to <see cref="CardSubtype"/> alongside this factory, mirroring
///   the Curse subtype added for <see cref="TrespassersCurseFactory"/>.
/// - <b>Static "+1/+1 and has first strike"</b> via a single
///   <see cref="AttachedBoostEffect"/> carrying the Layer 7c P/T bump
///   (+1/+1) and the Layer 6 First Strike keyword grant (CR 613 / 702.7).
///   The effect reads <see cref="Permanent.AttachedTo"/> dynamically and is
///   inert while the Aura is unattached or off the battlefield — same shape
///   as <see cref="DaybreakCoronetFactory"/>.
/// - <b>"When this Aura enters, create a 1/1 white Warrior creature token
///   with vigilance."</b> — an enters-the-battlefield
///   <see cref="TriggeredAbility"/> (CR 603.6a / 603.6d) keyed on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution it mints
///   one 1/1 white Warrior token with Vigilance for the controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111 / 111.4 /
///   702.20). Mirrors <see cref="DoomedTravelerFactory"/>'s token-on-event
///   wiring, but on ETB rather than dies.
/// - <b>"Enchant creature you control"</b> — the cast-time
///   <see cref="SpellDefinition"/> (<see cref="BuildSpellDefinition"/>)
///   filters the battlefield to creatures controlled by the caster
///   (CR 702.5b — "Enchant X" defines the legal target set; the
///   "you control" qualifier narrows it per CR 700.6) via
///   <see cref="AuraSpellDefinitionBuilder"/>.
///
/// ## Wiring
/// - <see cref="Create(Player)"/> attaches the boost + ETB trigger to the
///   card shape without registering a continuous-effects service or trigger
///   manager. Suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?, ZoneService?)"/>
///   registers the +1/+1 + first-strike boost against the supplied
///   <see cref="ContinuousEffectsService"/> and the ETB token trigger against
///   the supplied <see cref="TriggerManager"/>, threading the optional
///   <see cref="ZoneService"/> into token ETB so the token's
///   <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/> fires.
///
/// ## Rules reference
/// - CR 303.4f — on resolve, the Aura enters the battlefield attached to its
///   chosen target.
/// - CR 603.6a / 603.6d — "When this Aura enters" is an ETB triggered ability.
/// - CR 111 / 111.4 — tokens enter the battlefield under the controller's
///   control with the stated characteristics.
/// - CR 613 / 702.7 — Layer 7c P/T modification + Layer 6 First Strike grant.
/// - CR 702.20 — Vigilance keyword on the minted token.
/// </summary>
[CardName("Cartouche of Solidarity")]
public static class CartoucheOfSolidarityFactory
{
    public const string CardName = "Cartouche of Solidarity";
    public const string Slug = "cartouche-of-solidarity";
    public const string PrintedManaCost = "{W}";

    public const int PowerBoost = 1;
    public const int ToughnessBoost = 1;

    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>Keyword granted to the enchanted creature: First Strike
    /// (CR 702.7).</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { "First Strike" };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Cartouche of Solidarity with the static boost + ETB token
    /// trigger attached to the card shape but NOT registered with a
    /// continuous-effects service or trigger manager. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Cartouche of Solidarity. When
    /// <paramref name="continuousEffects"/> is supplied, the +1/+1 +
    /// first-strike boost is registered (gated on the Aura being attached and
    /// on the battlefield). When <paramref name="triggers"/> is supplied, the
    /// "create a 1/1 white Warrior token with vigilance" ETB trigger is
    /// registered so an entering <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
    /// places it on the stack automatically. <paramref name="zoneService"/>
    /// threads into token ETB so the token's CardMovedEvent fires.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers = null,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Enchantment, Aura + Cartouche subtypes, {W}, white)
        // from the embedded JSON definition.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Static: "Enchanted creature gets +1/+1 and has first strike."
        // CR 613 — single AttachedBoostEffect carries the Layer 7c P/T bump
        // and the Layer 6 First Strike grant. Inert while unattached.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerBoost,
                toughness: ToughnessBoost,
                grantedKeywords: GrantedKeywords));
        }

        // ----------------------------------------------------------------
        // ETB trigger: "When this Aura enters, create a 1/1 white Warrior
        // creature token with vigilance." CR 603.6a / 603.6d / 111 / 111.4.
        // ----------------------------------------------------------------
        var tokenEffect = new Effect(
            $"{CardName}: create a 1/1 white Warrior creature token with vigilance",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 111.4 / 702.20 — 1/1 white Warrior token with Vigilance.
                var spec = new TokenFactory.TokenSpec(
                    Name: "Warrior",
                    Power: TokenPower,
                    Toughness: TokenToughness,
                    Subtypes: new[] { CardSubtype.Warrior },
                    Keywords: new[] { "Vigilance" },
                    Colors: new[] { ManaColor.White });
                TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Cartouche of
    /// Solidarity. The printed "Enchant creature you control" requires the
    /// target to be a creature controlled by the caster (CR 702.5b + 700.6).
    /// Filters <paramref name="battlefield"/> accordingly.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        Player caster,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target creature you control",
            battlefield: battlefield,
            predicate: candidate => IsCreatureYouControl(candidate, caster));
    }

    /// <summary>
    /// Target-legality predicate for "creature you control": the candidate is
    /// a creature whose controller is <paramref name="caster"/> (CR 702.5b /
    /// 700.6).
    /// </summary>
    public static bool IsCreatureYouControl(Permanent candidate, Player caster)
    {
        if (candidate == null || caster == null) return false;
        if (!candidate.HasType(CardType.Creature)) return false;
        return ReferenceEquals(candidate.Controller, caster);
    }
}
