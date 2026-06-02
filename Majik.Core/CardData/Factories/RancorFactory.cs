using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rancor (Urza's Legacy, {G}).
///
/// Enchantment — Aura. Oracle text (verified against Scryfall 2026-06-01):
///   "Enchant creature
///    Enchanted creature gets +2/+0 and has trample.
///    When this Aura is put into a graveyard from the battlefield, return it
///    to its owner's hand."
///
/// A relentless green-aggro staple: a one-mana aura that pumps +2/+0 and
/// grants trample, and crucially recurs itself — destroy the enchanted
/// creature (or the aura) and Rancor bounces back to its owner's hand to be
/// re-cast next turn. Combines the JSON-driven aura identity + static
/// <see cref="AttachedBoostEffect"/> boost/keyword-grant posture of
/// <see cref="EtherealArmorFactory"/> / <see cref="DaybreakCoronetFactory"/>
/// with the "put into a graveyard from the battlefield → return to owner's
/// hand" dies-trigger of <see cref="MosswoodDreadknightFactory"/>.
///
/// ## Implementation
///
/// - <b>Card identity</b> (Enchantment — Aura, {G}, green color indicator) is
///   materialised from the embedded JSON definition (<c>rancor.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>, matching the JSON-driven aura
///   posture of <see cref="EtherealArmorFactory"/>.
/// - <b>Static "+2/+0 and has trample"</b> — a single
///   <see cref="AttachedBoostEffect"/> carrying both the Layer 7c +2/+0 pump
///   and the Layer 6 Trample grant (CR 613). The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically so re-attaching transfers
///   the boost without re-registration, and gates on the Aura being on the
///   battlefield AND attached (its <c>IsActive</c> check). The granted
///   "Trample" keyword is the marker consumed by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>.
/// - <b>Dies trigger — CR 603.6c / CR 700.4</b>: "When this Aura is put into
///   a graveyard from the battlefield, return it to its owner's hand." Fires
///   on a Battlefield → Graveyard <see cref="CardMovedEvent"/> matching this
///   specific card via <see cref="Triggers.OnDies"/>. <c>activeZones</c>
///   includes both Battlefield and Graveyard so the trigger still matches
///   after <see cref="ZoneService"/> stamps the card's Zone = Graveyard before
///   publishing the event (Mosswood Dreadknight / Wurmcoil posture). On
///   resolution the Aura is moved from its owner's graveyard to its owner's
///   hand. CR 400.7 — "owner": the return target is <see cref="ICard.Owner"/>,
///   not the controller's hand, so a control-changed Rancor still returns to
///   its true owner.
/// - <b>Enchant creature</b> — the standard bare card-type clause. The
///   cast-time predicate is the generic "creature" filter (CR 702.5b /
///   303.4c), built through the shared <see cref="AuraSpellDefinitionBuilder"/>.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits service wiring
/// and produces the correct card shape only (the dies trigger is attached for
/// observability but uses raw zone manipulation, and the boost is omitted) —
/// suitable for factory-shape / dispatch tests. The three-arg overload
/// registers the static boost and wires the dies trigger to a
/// <see cref="TriggerManager"/> / <see cref="ZoneService"/>.
/// </summary>
[CardName("Rancor")]
public static class RancorFactory
{
    public const string CardName = "Rancor";
    public const string Slug = "rancor";
    public const string Cost = "{G}";
    public const int PowerBoost = 2;
    public const int ToughnessBoost = 0;

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public const string OracleText =
        "Enchant creature\n" +
        "Enchanted creature gets +2/+0 and has trample.\n" +
        "When this Aura is put into a graveyard from the battlefield, return " +
        "it to its owner's hand.";

    /// <summary>Granted keyword on the enchanted creature: Trample
    /// (CR 702.19).</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { "Trample" };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Constructs a Rancor with card identity + dies trigger shape only (no
    /// live continuous effect, no TriggerManager/ZoneService wiring).
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null, zones: null, triggers: null);

    /// <summary>
    /// Constructs a Rancor. When <paramref name="continuousEffects"/> is
    /// supplied, the +2/+0 boost plus the Trample grant is registered against
    /// the service; gated on the aura being on the battlefield AND attached
    /// (effect's <c>IsActive</c> check). When <paramref name="zones"/> /
    /// <paramref name="triggers"/> are supplied, the "put into a graveyard
    /// from the battlefield → return to owner's hand" dies trigger is wired
    /// to fire automatically (CR 603.2).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);

        if (continuousEffects != null)
        {
            // CR 613 — single AttachedBoostEffect carries both the Layer 7c
            // +2/+0 pump and the Layer 6 Trample grant.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerBoost,
                toughness: ToughnessBoost,
                grantedKeywords: GrantedKeywords));
        }

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c / CR 700.4.
        //   "When this Aura is put into a graveyard from the battlefield,
        //    return it to its owner's hand."
        //
        // ActiveZones = {Battlefield, Graveyard} — the trigger's zone-guard
        // must still match after ZoneService has stamped the card's
        // Zone = Graveyard before publishing the CardMovedEvent
        // (Mosswood Dreadknight / Wurmcoil posture).
        //
        // CR 400.7 — "owner". The return target is card.Owner's hand, not the
        // controller's hand, so a control-changed Rancor still returns to its
        // true owner.
        // ----------------------------------------------------------------
        var capturedZones = zones;
        var diesEffect = new Effect(
            $"{CardName}: return to its owner's hand",
            () =>
            {
                // Trigger fires on B → G move; the card now lives in the
                // owner's graveyard. Move it from graveyard → hand.
                var dest = card.Owner ?? owner;

                if (capturedZones != null)
                {
                    capturedZones.MoveCard(
                        card,
                        ZoneType.Graveyard,
                        ZoneType.Hand,
                        controller: null);
                }
                else
                {
                    // Raw zone manipulation — shape-only path.
                    dest.Zones.Graveyard.RemoveCard(card);
                    dest.Zones.Hand.AddCard(card);
                    card.SetZone(ZoneType.Hand);
                }
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Rancor —
    /// "Enchant creature" → a single creature target (CR 702.5b / 303.4c). On
    /// resolution the Rancor enters the battlefield already attached to the
    /// chosen creature (CR 303.4f).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target creature",
            battlefield: battlefield,
            predicate: p => p.HasType(CardType.Creature));
    }
}
