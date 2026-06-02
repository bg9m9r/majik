using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodsoaked Champion (Khans of Tarkir, {B}).
///
/// Creature — Human Warrior 2/1. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "This creature can't block.
///    Raid — {1}{B}: Return this card from your graveyard to the
///    battlefield. Activate only if you attacked this turn."
///
/// Bloodsoaked Champion composes two analogue shapes already in the engine:
/// - <b>"This creature can't block." (CR 509.1c)</b> — the same non-expiring
///   <see cref="CombatRestrictionEffect"/> rider as
///   <see cref="GravecrawlerFactory"/> / Bloodghast / Bloodbraid Marauder.
/// - <b>Graveyard-activated self-return (CR 113.6 / 117.1a)</b> — the
///   <c>{cost}: Return this card from your graveyard to the battlefield</c>
///   shape from <see cref="SqueeDubiousMonarchFactory"/> (mana cost as a
///   <see cref="ManaCostCost"/> on an <see cref="ActivatedAbility"/>; the
///   self-return is performed in the resolution effect via
///   <see cref="ZoneService.MoveCard"/> when available so ETB triggers fire,
///   CR 603.6a). Bloodsoaked Champion's return carries NO additional cost
///   (Squee exiles three other graveyard cards) — only the {1}{B} mana cost.
///
/// The <b>Raid</b> ability word (CR 702 reminder — Raid is purely an ability
/// word, no rules meaning) is realised as "Activate only if you attacked
/// this turn" — a per-activation legality gate (CR 605.1b activation-timing
/// style restriction). It is wired through the
/// <see cref="ActivatedAbility"/>'s <c>canActivateCheck</c> hook, which
/// surfaces the predicate to <see cref="Majik.Core.Rules.ActionValidator"/>
/// so the activation is enumerable only while the controller has attacked
/// this turn. The "attacked this turn" fact is tracked the same way
/// <see cref="BerserkFactory"/> tracks its "if it attacked this turn" clause:
/// a live <see cref="CreatureAttacksEvent"/> listener flips an internal flag
/// when a creature controlled by Bloodsoaked Champion's owner is declared as
/// an attacker. The flag is reset at the start of each of the owner's turns
/// (the listener also resets on <see cref="TurnStartedEvent"/> for the owner
/// so a stale attack from a prior turn does not re-enable the return).
///
/// The base card shape (name / Creature type / Human Warrior subtypes / {B}
/// cost / 2/1 body) is materialised from the embedded JSON definition
/// (<c>bloodsoaked-champion.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the can't-block rider and the
/// Raid activated ability are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither yet (same posture as
/// <see cref="SqueeDubiousMonarchFactory"/> / <see cref="BloodbraidMarauderFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Creature shape</b> Human Warrior 2/1 at printed cost {B}.
/// - <b>"This creature can't block." (CR 509.1c)</b> — non-expiring
///   <see cref="CombatRestriction.CannotBlock"/> rider, registered only when
///   a <see cref="ContinuousEffectsService"/> is supplied (the shape-only
///   dispatcher path has no service, mirroring Gravecrawler).
/// - <b>Raid {1}{B} graveyard-return (CR 113.6 / 117.1a)</b>:
///   <see cref="ActivatedAbility"/> carrying a {1}{B}
///   <see cref="ManaCostCost"/>. The <c>canActivateCheck</c> gate enforces
///   "Activate only if you attacked this turn" — true only while the
///   internal attacked-this-turn flag is set. On resolution the Champion is
///   returned from its owner's graveyard to the battlefield (via
///   <see cref="ZoneService.MoveCard"/> when wired so ETB triggers fire,
///   else a raw zone move). The resolution body re-checks that the Champion
///   is in its owner's graveyard, so a spurious activation from another zone
///   is no-op-shaped.
/// - <b>Attacked-this-turn tracking</b>: when an <see cref="IEventBus"/> is
///   supplied, a <see cref="CreatureAttacksEvent"/> listener sets the flag
///   whenever a creature controlled by the owner attacks, and a
///   <see cref="TurnStartedEvent"/> listener clears it at the start of the
///   owner's turn (so the Raid gate is per-turn, CR 500.1 / 514). Without a
///   bus, the optional <c>attackedThisTurn</c> predicate is consulted
///   instead (caller-supplied); when both are absent the gate defaults to
///   closed (no attack recorded → cannot activate).
///
/// ## Deferred (v1 gaps)
/// - <b>Zone-scoped activated abilities</b>: the engine doesn't yet gate
///   activated abilities on source zone, so the Raid ability is enumerable
///   from any zone (same caveat as Squee / Priest of Fell Rites). The
///   resolution body's graveyard re-check keeps off-zone activations
///   no-op-shaped, and the printed "from your graveyard" is honoured at
///   resolution.
/// - <b>"You attacked this turn" without a bus or predicate</b>: the gate
///   defaults closed on the shape-only dispatcher path (no attack source
///   wired). Production wiring threads the live event bus via the full
///   overload.
/// </summary>
[CardName("Bloodsoaked Champion")]
public static class BloodsoakedChampionFactory
{
    public const string CardName = "Bloodsoaked Champion";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "bloodsoaked-champion";

    /// <summary>Raid return cost — CR 117.3.</summary>
    public const string RaidReturnCost = "{1}{B}";

    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Bloodsoaked Champion with no live runtime wiring. The
    /// can't-block rider is NOT registered (no effects service), the Raid
    /// activated ability is attached for shape inspection with its gate
    /// defaulting to closed (no attack source wired), and the self-return
    /// uses a raw zone move. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, zoneService: null, eventBus: null, attackedThisTurn: null);

    /// <summary>
    /// Construct Bloodsoaked Champion with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for the permanent
    /// can't-block restriction (CR 509.1c). May be null — restriction not
    /// enforced without it (shape-only).</param>
    /// <param name="zoneService">Zone service used by the Raid return to move
    /// the Champion from graveyard to battlefield so ETB triggers fire
    /// (CR 603.6a). May be null — a raw zone move is performed instead.</param>
    /// <param name="eventBus">Event bus consulted to track whether the owner
    /// attacked this turn (<see cref="CreatureAttacksEvent"/> sets the gate;
    /// <see cref="TurnStartedEvent"/> for the owner clears it). May be null —
    /// the <paramref name="attackedThisTurn"/> predicate is used instead.</param>
    /// <param name="attackedThisTurn">Fallback predicate returning whether the
    /// owner attacked this turn, consulted when <paramref name="eventBus"/>
    /// is null. May be null — the Raid gate then defaults closed.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        ZoneService? zoneService,
        IEventBus? eventBus,
        Func<bool>? attackedThisTurn)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human
        // Warrior subtypes, {B}, 2/1). The JSON carries no abilities — the
        // can't-block rider + Raid ability are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "This creature can't block." — CR 509.1c.
        // Permanent restriction (expiresAtEndOfTurn = false) registered on
        // the ContinuousEffectsService so CombatValidator.CanBlock returns
        // false for this creature. Mirrors Gravecrawler / Bloodghast.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            effects.Register(new CombatRestrictionEffect(
                CombatRestriction.CannotBlock,
                target: card,
                expiresAtEndOfTurn: false));
        }

        // ----------------------------------------------------------------
        // Attacked-this-turn tracking (the "Raid — … Activate only if you
        // attacked this turn" gate). When an event bus is supplied we track
        // the fact live: a CreatureAttacksEvent for a creature controlled by
        // the owner sets the flag (CR 508.1f), and a TurnStartedEvent for the
        // owner clears it (per-turn reset, CR 500.1). Without a bus, the
        // caller-supplied predicate is consulted; absent both, the gate is
        // closed (no attack recorded).
        // ----------------------------------------------------------------
        var attackedFlag = new[] { false };

        if (eventBus != null)
        {
            eventBus.Subscribe<CreatureAttacksEvent>(e =>
            {
                // "you attacked" — the owner of Bloodsoaked Champion must be
                // the player who controls the declared attacker.
                if (ReferenceEquals(e.Attacker.Controller, owner))
                {
                    attackedFlag[0] = true;
                }
            });

            eventBus.Subscribe<TurnStartedEvent>(e =>
            {
                // Per-turn reset on the owner's turn. "this turn" resets each
                // turn (CR 514 cleanup / new turn) — a prior turn's attack
                // does not keep the Raid gate open.
                if (ReferenceEquals(e.Player, owner))
                {
                    attackedFlag[0] = false;
                }
            });
        }

        Func<bool> gate = eventBus != null
            ? () => attackedFlag[0]
            : (attackedThisTurn ?? (() => false));

        // ----------------------------------------------------------------
        // Raid activated ability — {1}{B}: Return this card from your
        // graveyard to the battlefield. Activate only if you attacked this
        // turn. (CR 113.6 / 117.1a; gate via canActivateCheck — CR 605.1b
        // style activation legality.)
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return self from graveyard to battlefield (Raid)",
            () =>
            {
                // Re-check zone at resolution — only returns from the owner's
                // graveyard (printed "from your graveyard"). Off-zone
                // activations are no-op-shaped while engine zone-scoping is
                // deferred.
                if (card.Zone != ZoneType.Graveyard) return;
                if (!ReferenceEquals(card.Owner, owner)) return;
                if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

                if (zoneService != null)
                {
                    // ZoneService.MoveCard fires ETB triggers + replacements
                    // (CR 603.6a).
                    zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Battlefield, owner);
                }
                else
                {
                    owner.Zones.Graveyard.RemoveCard(card);
                    owner.Zones.Battlefield.AddCard(card);
                    card.SetZone(ZoneType.Battlefield);
                    card.SetController(owner);
                }
            });

        var raidAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(RaidReturnCost) },
            effects: new IEffect[] { returnEffect },
            // "Activate only if you attacked this turn."
            canActivateCheck: gate);

        card.AddAbility(raidAbility);

        return card;
    }
}
