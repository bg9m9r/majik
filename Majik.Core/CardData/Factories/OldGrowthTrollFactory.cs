using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Old-Growth Troll (Kaldheim, {G}{G}{G}).
///
/// Creature — Troll Warrior 4/4. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Trample"
///   "When Old-Growth Troll dies, if it was a creature, return it to the
///    battlefield. It's an Aura enchantment with enchant Forest you control
///    and 'Enchanted Forest has \"{T}: Add {G}{G}\" and \"{1}, {T},
///    Sacrifice this land: Create a tapped 4/4 green Troll Warrior creature
///    token with trample.\"'"
///
/// ## Implementation — the return-as-Aura-on-dies primitive
///
/// The dies trigger (CR 603.6c / 700.4) routes through the reusable
/// <see cref="ReturnAsAuraOnDeathEffect"/> primitive — the new engine seam
/// this card unblocks. On resolution it:
///   1. picks a Forest the controller controls (CR 303.4 — an Aura needs a
///      legal object to enchant; no Forest → the return does not happen,
///      CR 303.4g);
///   2. returns the dead creature to the battlefield as a fresh
///      <b>Enchantment — Aura</b> object (CR 614.12 — the returning object is
///      a new object) named "Old-Growth Troll", entered through
///      <see cref="ZoneService"/>;
///   3. attaches it to the chosen Forest (CR 303.4f — an Aura enters already
///      attached to its host);
///   4. grants that Forest two abilities via <see cref="GrantAbilityEffect"/>
///      (CR 613.1f), which follow the Aura and drop when it leaves play:
///        * "{T}: Add {G}{G}" — a <see cref="ManaAbility"/> (CR 605.1);
///        * "{1}, {T}, Sacrifice this land: Create a tapped 4/4 green Troll
///          Warrior creature token with trample" — an
///          <see cref="ActivatedAbility"/> (mirrors
///          <see cref="TectonicEdgeFactory"/>'s {1},{T},Sacrifice shape).
///
/// ## Wiring
///
/// - <see cref="Create(Player)"/> builds the card shape + dies trigger only
///   (no continuous-effects / zone service). The trigger's return-as-Aura
///   body is a safe no-op without a service (mirrors the shape-only posture of
///   <see cref="RancorFactory"/>'s single-arg overload). Suitable for shape /
///   dispatch tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, ZoneService?, TriggerManager?)"/>
///   wires the live return-as-Aura primitive + (optionally) registers the dies
///   trigger so a Battlefield → Graveyard move fires it automatically.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Host pick is deterministic</b> (first Forest the controller controls),
///   not agent-driven. "Which Forest" is a target detail, not the
///   return-as-Aura mechanic; an agent-driven pick is a documented residual.
/// - <b>"if it was a creature"</b> — Old-Growth Troll is always a creature on
///   the battlefield in v1 (no effect strips its creature type), so the
///   intervening-if is always satisfied; the clause is not separately gated.
/// - <b>Granted-token's own Trample keyword</b> is a marker
///   (<see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> reads it).
/// </summary>
[CardName("Old-Growth Troll")]
public static class OldGrowthTrollFactory
{
    public const string CardName = "Old-Growth Troll";
    public const string PrintedManaCost = "{G}{G}{G}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public const string OracleText =
        "Trample\n" +
        "When Old-Growth Troll dies, if it was a creature, return it to the " +
        "battlefield. It's an Aura enchantment with enchant Forest you control " +
        "and \"Enchanted Forest has '{T}: Add {G}{G}' and '{1}, {T}, " +
        "Sacrifice this land: Create a tapped 4/4 green Troll Warrior creature " +
        "token with trample.'\"";

    /// <summary>
    /// Build the card shape (4/4 Troll Warrior, Trample) + dies trigger only.
    /// The dies trigger's return-as-Aura body is inert without a
    /// <see cref="ContinuousEffectsService"/> (shape-only path). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, continuousEffects: null, zones: null, triggers: null);

    /// <summary>
    /// Build a fully-wired Old-Growth Troll. When
    /// <paramref name="continuousEffects"/> and <paramref name="zones"/> are
    /// supplied, the dies trigger returns the creature as an Enchantment — Aura
    /// attached to a controlled Forest and grants the Forest its two abilities
    /// (CR 614.12 / 303.4f / 613.1f). When <paramref name="triggers"/> is
    /// supplied, the dies trigger is registered so it fires automatically on a
    /// Battlefield → Graveyard move (CR 603.2).
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Troll, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // Dies trigger (CR 603.6c / 700.4) — "When Old-Growth Troll dies …
        // return it to the battlefield as an Aura enchantment with enchant
        // Forest you control."
        //
        // ActiveZones = {Battlefield, Graveyard} — ZoneService stamps
        // card.Zone = Graveyard BEFORE publishing the CardMovedEvent, so the
        // trigger must stay observable in both zones (Rancor / Doomed
        // Traveler / Wurmcoil posture).
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName}: return to the battlefield as an Aura enchanting a Forest you control",
            () =>
            {
                ReturnAsAuraOnDeathEffect.Apply(
                    deadPermanent: card,
                    auraName: CardName,
                    auraManaCost: PrintedManaCost,
                    // "enchant Forest you control" (CR 702.5b / 303.4).
                    hostPredicate: static p =>
                        p.HasType(CardType.Land) && p.HasSubtype(CardSubtype.Forest),
                    grantedAbilityFactories: new Func<Permanent, IAbility>[]
                    {
                        BuildGrantedManaAbility,
                        host => BuildGrantedTokenAbility(host, zones),
                    },
                    continuousEffects: continuousEffects,
                    zones: zones);
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
    /// "{T}: Add {G}{G}" granted to the enchanted Forest (CR 605.1 — a mana
    /// ability; doesn't use the stack).
    /// </summary>
    private static IAbility BuildGrantedManaAbility(Permanent host) =>
        new ManaAbility(host, host.Controller ?? host.Owner!, ManaCost.Parse("{G}{G}"));

    /// <summary>
    /// "{1}, {T}, Sacrifice this land: Create a tapped 4/4 green Troll Warrior
    /// creature token with trample." Granted to the enchanted Forest. Mirrors
    /// <see cref="TectonicEdgeFactory"/>'s {1},{T},Sacrifice shape: the
    /// self-sacrifice is inlined into the resolution body (the cost was
    /// declared at activation via <see cref="AdditionalCost.Sacrifice"/>; the
    /// visible zone-move catches up here).
    /// </summary>
    private static IAbility BuildGrantedTokenAbility(Permanent host, ZoneService? zones)
    {
        var controller = host.Controller ?? host.Owner!;

        var tokenEffect = new Effect(
            $"{CardName} Aura: sacrifice the enchanted Forest, create a tapped 4/4 green Troll Warrior with trample",
            () =>
            {
                // Self-sacrifice the land (CR 701.16) — Tectonic Edge posture.
                SacrificeHost(host);

                // CR 111 / 111.4 — create one tapped 4/4 green Troll Warrior
                // creature token with trample for the controller.
                var token = TokenFactory.CreateOnBattlefield(
                    new TokenFactory.TokenSpec(
                        Name: "Troll Warrior",
                        Power: 4,
                        Toughness: 4,
                        Subtypes: new[] { CardSubtype.Troll, CardSubtype.Warrior },
                        Keywords: new[] { "Trample" },
                        Colors: new[] { ManaColor.Green }),
                    controller,
                    zones);

                // CR 110.5 — the token enters tapped.
                token.Tap();
            });

        return new ActivatedAbility(
            source: host,
            controller: controller,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(host),
                new SacrificeSelfCost(host),
            },
            effects: new IEffect[] { tokenEffect });
    }

    /// <summary>Move the enchanted Forest to its owner's graveyard
    /// (CR 701.16 — Sacrifice). The Aura attached to it leaves play with it
    /// (CR 704.5n / 303.4); the grant lifecycle drops as the Aura's
    /// <see cref="Permanent.AttachedTo"/> goes null / off-battlefield.</summary>
    private static void SacrificeHost(Permanent host)
    {
        var ownerOf = host.Owner ?? host.Controller;
        if (ownerOf == null) return;
        if (host.Zone != ZoneType.Battlefield) return;

        ownerOf.Zones.Battlefield.RemoveCard(host);
        ownerOf.Zones.Graveyard.AddCard(host);
        host.SetZone(ZoneType.Graveyard);
    }
}
