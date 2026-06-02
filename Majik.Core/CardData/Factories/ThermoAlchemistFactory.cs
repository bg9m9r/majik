using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thermo-Alchemist (Eldritch Moon, {1}{R}).
///
/// Creature — Human Shaman 0/3 (red). Oracle text (verified against Scryfall):
///   "Defender
///    {T}: This creature deals 1 damage to each opponent.
///    Whenever you cast an instant or sorcery spell, untap this creature."
///
/// The base shape (name, Creature, Human/Shaman subtypes, {1}{R}, 0/3) is
/// materialised from the embedded JSON definition (<c>thermo-alchemist.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Defender, the tap-burn activated
/// ability, and the untap-on-instant/sorcery-cast trigger are layered on here —
/// the JSON <c>AbilityDefinition</c> schema doesn't yet express the Defender
/// keyword, activated abilities, or cast-spell triggers, so they live in the
/// factory (same posture as <see cref="NettleDroneFactory"/>, which is the same
/// printed engine but keys its untap trigger on colorless spells / Devoid).
///
/// ## Implemented (v1)
/// - 0/3 Human Shaman, mana cost {1}{R} (red — no Devoid, unlike Nettle Drone).
/// - <b>Defender keyword (CR 702.3)</b> — wired as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/>
///   surfaces it for block legality (the card can't attack). Same marker
///   pattern as <see cref="ElectrostaticFieldFactory"/> / Wall of Fire.
/// - <b>{T}: 1 damage to each opponent</b> — an <see cref="ActivatedAbility"/>
///   whose only cost is <see cref="AdditionalCost.Tap"/> on Thermo-Alchemist
///   itself (CR 602.2 / 602.5 — a tap-symbol cost). On resolution it deals 1
///   damage to each opponent, routed through <see cref="Fx.DealDamageAny"/>
///   against the injected <c>opponentResolver</c> (the Player aggregate exposes
///   no opponents list at v1, so the caller threads "each opponent" through —
///   same resolver-injection pattern as <see cref="NettleDroneFactory"/> /
///   <see cref="VoldarenEpicureFactory"/>). CR 119 — damage to a player is
///   life loss.
/// - <b>Untap-on-instant/sorcery-cast trigger (CR 603.1)</b> — fires on a
///   <see cref="SpellCastEvent"/> whose
///   <see cref="Majik.Core.Spells.ISpell.Controller"/> matches Thermo-Alchemist's
///   controller ("you") AND whose <see cref="Majik.Core.Spells.ISpell.Card"/>
///   is an Instant OR Sorcery (CR 205.3 / 304.1 / 307.1 — same instant/sorcery
///   filter as <see cref="ElectrostaticFieldFactory"/> / <see cref="GuttersnipeFactory"/>).
///   The untap half mirrors <see cref="NettleDroneFactory"/> (CR 701.20 —
///   untapping an already-untapped permanent is a no-op, so the effect guards
///   on <see cref="Permanent.IsTapped"/> before calling
///   <see cref="Permanent.Untap"/>). This is the printed engine: tap for burn,
///   then untap each time you cast an instant or sorcery.
///
/// ## Single-arg dispatcher path
///
/// The <see cref="Create(Player)"/> overload attaches Defender + both abilities
/// structurally (correct card shape for factory-shape / dispatch tests). The
/// trigger is not registered with a <see cref="TriggerManager"/>; the tap-burn
/// half no-ops with no opponent resolver. Production callers use the full
/// overload.
///
/// ## Deferred (v1 gaps)
/// - <b>Live "each opponent" enumeration</b> — no <c>Player.Opponents</c>
///   accessor at v1; resolver-injection shared with
///   <see cref="NettleDroneFactory"/> / <see cref="VoldarenEpicureFactory"/>.
/// </summary>
[CardName("Thermo-Alchemist")]
public static class ThermoAlchemistFactory
{
    public const string CardName = "Thermo-Alchemist";
    public const string Slug = "thermo-alchemist";
    public const int Power = 0;
    public const int Toughness = 3;
    public const int TapBurnAmount = 1;

    /// <summary>
    /// Construct Thermo-Alchemist with no live wiring. Defender + the tap-burn
    /// activated ability + the untap-on-instant/sorcery-cast trigger are
    /// attached structurally; the trigger is NOT registered with a
    /// <see cref="TriggerManager"/> and the burn half no-ops (no opponent
    /// resolver). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, opponentResolver: null);

    /// <summary>
    /// Construct a fully-wired Thermo-Alchemist.
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
        // Human/Shaman subtypes, {1}{R}, 0/3). The JSON carries no abilities —
        // Defender / tap-burn / untap-trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.3 — Defender keyword marker. Wired so
        // CombatAbilities.HasDefender surfaces it for block legality (same
        // marker pattern as Electrostatic Field / Wall of Fire).
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // ----------------------------------------------------------------
        // {T}: This creature deals 1 damage to each opponent. CR 602.2 /
        // 602.5 — a {T} (tap-symbol) cost activated ability. Resolution
        // deals 1 to each opponent via the resolver-injection pattern
        // (Nettle Drone shape). CR 119 — damage to a player is life loss.
        // Without a resolver the burn half no-ops (shape path).
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
        // Whenever you cast an instant or sorcery spell, untap this creature.
        // CR 603.1 — cast trigger. Predicate: spell cast by this card's
        // controller AND the spell's card is an Instant OR Sorcery
        // (CR 205.3 / 304.1 / 307.1 — same filter as Electrostatic Field /
        // Guttersnipe). Effect untaps Thermo-Alchemist itself; CR 701.20
        // makes untapping an already-untapped permanent a no-op, so guard on
        // IsTapped (Nettle Drone / Nettle Sentinel posture).
        // ----------------------------------------------------------------
        var untapEffect = new Effect(
            $"{CardName}: untap self (whenever you cast an instant or sorcery spell)",
            () =>
            {
                if (card.IsTapped) card.Untap();
            });

        var untapTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>((e, _) =>
                ReferenceEquals(e.Spell.Controller, card.Controller ?? owner)
                && (e.Spell.Card.HasType(CardType.Instant)
                    || e.Spell.Card.HasType(CardType.Sorcery))),
            effects: new IEffect[] { untapEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(untapTrigger);
        triggers?.RegisterTriggeredAbility(untapTrigger);

        return card;
    }
}
