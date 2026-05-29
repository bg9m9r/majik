using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Legion Loyalist (Gatecrash, {R}). Creature —
/// Goblin Soldier 1/1. Oracle text (verified against Scryfall):
///   "Haste
///    Battalion — Whenever this creature and at least two other creatures
///    attack, creatures you control gain first strike and trample until end
///    of turn and can't be blocked by creature tokens this turn."
///
/// The card's base shape (name, type, Goblin Soldier subtypes, {R}, 1/1)
/// is materialised from the embedded JSON definition
/// (<c>legion-loyalist.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Haste + the Battalion trigger
/// are layered on top here — the JSON <c>AbilityDefinition</c> schema
/// doesn't express keyword markers or attack triggers, so they live in the
/// factory (same posture as <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Haste (CR 702.10)</b> — wired as a <see cref="KeywordAbility"/>
///   marker so the combat / summoning-sickness subsystem surfaces it. Same
///   shape as Goblin Guide / Bloodbraid Elf.
/// - <b>Battalion (CR 508.1f)</b> — wired as a <see cref="TriggeredAbility"/>
///   over <see cref="AttackersDeclaredEvent"/>. The trigger fires once per
///   declare-attackers step when (a) this card's controller is the
///   attacking player, (b) this card is itself among the declared
///   attackers, and (c) at least three creatures are attacking in total
///   (this creature + at least two others). Same
///   <see cref="EventTriggerCondition{TEvent}"/> attack-declared pattern as
///   <see cref="SoaringThoughtThiefFactory"/>.
/// - <b>Resolve body</b> snapshots the controller's battlefield creatures
///   at resolution time (CR 608.2) and on each registers:
///   <list type="bullet">
///     <item><see cref="GrantKeywordUntilEndOfTurnEffect"/>("First strike")
///       — Layer 6 keyword grant (CR 613.1c / 702.7), EOT cleanup
///       (CR 514.2).</item>
///     <item><see cref="GrantKeywordUntilEndOfTurnEffect"/>("Trample") —
///       Layer 6 keyword grant (CR 613.1c / 702.19), EOT cleanup.</item>
///     <item><see cref="CantBeBlockedExceptByEffect"/> with predicate
///       <c>blocker =&gt; blocker is not Permanent { IsToken: true }</c> —
///       "can't be blocked by creature tokens this turn" (CR 509.1b). The
///       combat validator intersects this restriction via the source
///       creature's <see cref="Creature.ActiveEffects"/>.</item>
///   </list>
///   Creatures without a live <see cref="ContinuousEffectsService"/> wired
///   no-op cleanly (same defensive guard as
///   <see cref="ViolentOutburstFactory.ApplyPumpAndHaste"/>).
///
/// ## Notes
/// - Single-arg <see cref="Create(Player)"/> is the
///   <see cref="NamedCardFactory"/> dispatch entry point; the Battalion
///   trigger is attached structurally (no live TriggerManager required —
///   the engine's zone-driven <see cref="TriggerManager.BindCard"/> picks
///   up the card's triggered abilities once it's on the battlefield).
/// - The token-block restriction's <c>ExpiresAtEndOfTurn</c> is true on the
///   grant effects; the <see cref="CantBeBlockedExceptByEffect"/> is a
///   non-expiring continuous effect whose <see cref="ContinuousEffect.IsActive"/>
///   short-circuits when the affected creature leaves the battlefield, so
///   the printed "this turn" wording is honoured for all combat-relevant
///   queries (creatures present during the turn; combat ends at cleanup).
/// </summary>
[CardName("Legion Loyalist")]
public static class LegionLoyalistFactory
{
    public const string CardName = "Legion Loyalist";
    public const string Slug = "legion-loyalist";

    /// <summary>CR 702.10 — Haste keyword marker.</summary>
    public const string HasteKeyword = "Haste";

    /// <summary>CR 702.7 — granted First strike.</summary>
    public const string FirstStrikeKeyword = "First strike";

    /// <summary>CR 702.19 — granted Trample.</summary>
    public const string TrampleKeyword = "Trample";

    /// <summary>
    /// CR 508.1f — Battalion needs at least three attackers in total
    /// (this creature plus at least two other creatures).
    /// </summary>
    public const int BattalionMinAttackers = 3;

    /// <summary>
    /// Construct Legion Loyalist. The Battalion triggered ability is
    /// attached structurally; the engine's zone-driven registration
    /// (<see cref="TriggerManager.BindCard"/>) wires it to events once the
    /// card is on the battlefield. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Goblin Soldier, {R}, 1/1). The JSON carries no abilities — Haste
        // and Battalion are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.10 — Haste keyword marker.
        card.AddAbility(new KeywordAbility(HasteKeyword, card, owner));

        // CR 508.1f — Battalion attack-declared trigger.
        card.AddAbility(BuildBattalion(card, owner));

        return card;
    }

    /// <summary>
    /// Build the Battalion triggered ability. Fires on
    /// <see cref="AttackersDeclaredEvent"/> when the controller attacks with
    /// this creature and at least two others (≥3 attackers total). On
    /// resolution it grants first strike + trample and the token-block
    /// restriction to every creature the controller controls (CR 608.2
    /// snapshot at resolution time).
    /// </summary>
    private static TriggeredAbility BuildBattalion(Creature card, Player owner)
    {
        var condition = new EventTriggerCondition<AttackersDeclaredEvent>(
            (e, _) => IsBattalionMatch(e, card, owner));

        var effect = new Effect(
            $"{CardName}: creatures you control gain first strike and trample and can't be blocked by creature tokens until end of turn (CR 508.1f)",
            () => ApplyBattalion(card, owner));

        return new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });
    }

    // --- Trigger condition (CR 508.1f) -----------------------------------

    private static bool IsBattalionMatch(AttackersDeclaredEvent e, Creature card, Player owner)
    {
        var controller = card.Controller ?? owner;

        // CR 109.5 — "this creature ... attack" keys on the controller being
        // the attacking player.
        if (!ReferenceEquals(e.Combat.AttackingPlayer, controller)) return false;

        // "Whenever THIS creature ... attack" — the Loyalist itself must be
        // among the declared attackers.
        var selfAttacking = false;
        var total = 0;
        foreach (var atk in e.Combat.Attackers)
        {
            if (atk?.Creature == null) continue;
            total++;
            if (ReferenceEquals(atk.Creature, card)) selfAttacking = true;
        }

        // CR 508.1f — this creature + at least two others ⇒ ≥3 attackers.
        return selfAttacking && total >= BattalionMinAttackers;
    }

    // --- Resolution body (CR 608.2) --------------------------------------

    /// <summary>
    /// Apply the Battalion rider to every creature
    /// <paramref name="owner"/> (the controller) controls at the moment the
    /// trigger resolves. Each gets first strike + trample until end of turn
    /// (Layer 6 grants, CR 514.2 cleanup) and a "can't be blocked by
    /// creature tokens" restriction (CR 509.1b). Creatures without a live
    /// <see cref="ContinuousEffectsService"/> no-op cleanly.
    /// </summary>
    private static void ApplyBattalion(Creature card, Player owner)
    {
        var controller = card.Controller ?? owner;

        // Snapshot to a list before applying (CR 608.2 — current game state)
        // so any same-step side effects don't disturb the enumeration.
        var creatures = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();

        foreach (var creature in creatures)
        {
            // Shape-only safety — without a live ContinuousEffectsService
            // the grants silently no-op rather than NRE'ing.
            if (creature.ActiveEffects == null) continue;

            // CR 613.1c Layer 6 — grant First strike (CR 702.7) + Trample
            // (CR 702.19) until end of turn (CR 514.2 cleanup).
            creature.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(creature, FirstStrikeKeyword));
            creature.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(creature, TrampleKeyword));

            // CR 509.1b — "can't be blocked by creature tokens this turn."
            // Predicate allows only non-token would-be blockers. The combat
            // validator intersects this against the attacker's ActiveEffects.
            creature.ActiveEffects.Register(
                new CantBeBlockedExceptByEffect(
                    creature,
                    blocker => blocker is not Permanent { IsToken: true },
                    expiresAtEndOfTurn: true));
        }
    }
}
