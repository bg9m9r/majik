using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nettle Drone (Oath of the Gatewatch, {2}{R}).
///
/// Creature — Eldrazi Drone 3/1 (colorless — Devoid). Oracle text (verified
/// against Scryfall):
///   "Devoid (This card has no color.)
///    {T}: This creature deals 1 damage to each opponent.
///    Whenever you cast a colorless spell, untap this creature."
///
/// The card's base shape (name, Creature, Eldrazi + Drone subtypes, {2}{R},
/// 3/1) is materialised from the embedded JSON definition
/// (<c>nettle-drone.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Devoid, the tap-burn activated
/// ability, and the untap-on-colorless-cast trigger are layered on top here —
/// the JSON <c>AbilityDefinition</c> schema doesn't yet express the Devoid
/// keyword, activated abilities, or cast-spell triggers, so they live in the
/// factory (same posture as <see cref="GlaringFleshrakerFactory"/> /
/// <see cref="KozileksReturnFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Devoid (CR 702.114)</b> — stamped via <see cref="Card.SetDevoid"/>
///   so <see cref="CardColors.GetColors"/> returns empty regardless of the
///   {R} pip, plus a <see cref="KeywordAbility"/> marker for ability-scan
///   discoverability. Same shape as <see cref="KozileksReturnFactory"/>.
/// - <b>{T}: 1 damage to each opponent</b> — an <see cref="ActivatedAbility"/>
///   whose only cost is <see cref="AdditionalCost.Tap"/> on Nettle Drone
///   itself (CR 602.2 / 602.5 — a tap symbol cost). On resolution the effect
///   deals 1 damage to each opponent, routed through
///   <see cref="Fx.DealDamageAny"/> against the injected
///   <c>opponentResolver</c> (the Player aggregate exposes no opponents list
///   at v1, so the caller threads "each opponent" through — same
///   resolver-injection pattern as <see cref="GlaringFleshrakerFactory"/> /
///   <see cref="VoldarenEpicureFactory"/>). CR 119 — damage to a player is
///   life loss.
/// - <b>Untap-on-colorless-spell-cast trigger (CR 603.1)</b> — fires on a
///   <see cref="SpellCastEvent"/> whose
///   <see cref="Majik.Core.Spells.ISpell.Controller"/> matches Nettle
///   Drone's controller AND whose
///   <see cref="Majik.Core.Spells.ISpell.Card"/> is colorless
///   (<see cref="CardColors.GetColors"/> returns an empty set — CR 105.2c
///   "a colorless object has no color"). Same predicate shape as
///   <see cref="GlaringFleshrakerFactory"/>'s cast-colorless trigger; the
///   untap half mirrors <see cref="NettleSentinelFactory"/> (CR 701.20 —
///   untapping an already-untapped permanent is a no-op, so the effect
///   guards on <see cref="Permanent.IsTapped"/> before calling
///   <see cref="Permanent.Untap"/>). Note Nettle Drone is itself colorless
///   (Devoid), so casting Nettle Drone — or any other colorless spell —
///   feeds this trigger, the printed engine: tap for burn, then untap when
///   you cast a colorless spell.
///
/// ## Single-arg dispatcher path
///
/// The <see cref="Create(Player)"/> overload attaches Devoid + both abilities
/// structurally (correct card shape for factory-shape / dispatch tests).
/// The trigger is not registered with a <see cref="TriggerManager"/>; the
/// tap-burn half no-ops with no opponent resolver. Production callers use the
/// full overload.
///
/// ## Deferred (v1 gaps)
/// - <b>Live "each opponent" enumeration</b> — no <c>Player.Opponents</c>
///   accessor at v1; resolver-injection shared with
///   <see cref="GlaringFleshrakerFactory"/> / <see cref="VoldarenEpicureFactory"/>.
/// </summary>
[CardName("Nettle Drone")]
public static class NettleDroneFactory
{
    public const string CardName = "Nettle Drone";
    public const string Slug = "nettle-drone";
    public const int Power = 3;
    public const int Toughness = 1;
    public const int TapBurnAmount = 1;

    /// <summary>CR 702.114 — Devoid keyword marker string.</summary>
    public const string DevoidKeyword = "Devoid";

    /// <summary>
    /// Construct Nettle Drone with no live wiring. Devoid + the tap-burn
    /// activated ability + the untap-on-colorless-cast trigger are attached
    /// structurally; the trigger is NOT registered with a
    /// <see cref="TriggerManager"/> and the burn half no-ops (no opponent
    /// resolver). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, opponentResolver: null);

    /// <summary>
    /// Construct a fully-wired Nettle Drone.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager for registration. May be null
    /// — the untap trigger attaches structurally but isn't enrolled.</param>
    /// <param name="opponentResolver">Live enumerator of "each opponent" for
    /// the tap-burn ability. Without a resolver the burn half no-ops.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi + Drone subtypes, {2}{R}, 3/1). The JSON carries no
        // abilities — Devoid / tap-burn / untap-trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.114 — Devoid. Stamp IsDevoid so CardColors.GetColors returns
        // empty regardless of the {R} pip; also attach the KeywordAbility
        // marker for ability-scan discoverability. Same shape as Kozilek's
        // Return.
        card.SetDevoid(true);
        card.AddAbility(new KeywordAbility(DevoidKeyword, card, owner));

        // ----------------------------------------------------------------
        // {T}: This creature deals 1 damage to each opponent. CR 602.2 /
        // 602.5 — a {T} (tap-symbol) cost activated ability. Resolution
        // deals 1 to each opponent via the resolver-injection pattern
        // (Glaring Fleshraker shape). CR 119 — damage to a player is life
        // loss. Without a resolver the burn half no-ops (shape path).
        // ----------------------------------------------------------------
        var burnEffect = new Effect(
            $"{CardName}: deal {TapBurnAmount} damage to each opponent",
            () =>
            {
                var opponents = opponentResolver?.Invoke();
                if (opponents == null) return;

                foreach (var opp in opponents)
                {
                    if (ReferenceEquals(opp, owner)) continue;
                    Fx.DealDamageAny(opp, TapBurnAmount);
                }
            });

        var burnAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { burnEffect });

        card.AddAbility(burnAbility);

        // ----------------------------------------------------------------
        // Whenever you cast a colorless spell, untap this creature.
        // CR 603.1 — cast trigger. Predicate: spell cast by this card's
        // controller AND the spell's card is colorless (CR 105.2c — empty
        // color set). Effect untaps Nettle Drone itself; CR 701.20 makes
        // untapping an already-untapped permanent a no-op, so guard on
        // IsTapped (Nettle Sentinel posture). Nettle Drone is itself
        // colorless (Devoid), so casting it — or any colorless spell —
        // feeds this trigger.
        // ----------------------------------------------------------------
        var untapEffect = new Effect(
            $"{CardName}: untap self (whenever you cast a colorless spell)",
            () =>
            {
                if (card.IsTapped) card.Untap();
            });

        var untapTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>((e, _) =>
                ReferenceEquals(e.Spell.Controller, card.Controller ?? owner)
                && CardColors.GetColors(e.Spell.Card).Count == 0),
            effects: new IEffect[] { untapEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(untapTrigger);
        triggers?.RegisterTriggeredAbility(untapTrigger);

        return card;
    }
}
