using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Drakuseth, Maw of Flames (Core Set 2020,
/// {4}{R}{R}{R}). Legendary Creature — Dragon 7/7. Oracle text (verified
/// against Scryfall):
///   "Flying
///    Whenever Drakuseth attacks, it deals 4 damage to any target and 3
///    damage to each of up to two other targets."
///
/// ## Shape source
/// Card identity (name, {4}{R}{R}{R}, 7/7, Legendary Creature — Dragon) is
/// loaded from <c>Majik.Core/CardData/Cards/drakuseth-maw-of-flames.json</c>
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
/// through <see cref="CardDefinitionFactory"/>. The Flying keyword marker and
/// the attack burn trigger are attached in code below — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express attack triggers or
/// multi-"any target" damage (same posture as
/// <see cref="BloodhallPriestFactory"/> / <see cref="GlorybringerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>7/7 Legendary Creature — Dragon at {4}{R}{R}{R}</b>, owner/controller
///   wired.
/// - <b>Flying (CR 702.9)</b> — keyword marker via <see cref="KeywordAbility"/>,
///   read by the combat/block subsystem the same way every printed Flyer is.
/// - <b>Attack burn trigger (CR 508.1f / 603.1)</b>: a single
///   <see cref="TriggeredAbility"/> on <see cref="Triggers.OnAttackSelf"/>
///   carrying THREE <see cref="TargetRequest"/>s —
///   <list type="number">
///     <item><description>a 1..1 "any target" that takes
///       <see cref="MajorDamage"/> (4); and</description></item>
///     <item><description>two 0..1 "any other target" requests, each taking
///       <see cref="MinorDamage"/> (3) — "3 damage to each of up to two other
///       targets" (CR 115.1b — "up to" makes each of the two extra targets
///       optional).</description></item>
///   </list>
///   On resolution each chosen target is routed through
///   <see cref="Fx.DealDamageAny"/> so all legal target classes resolve
///   correctly: Player → life loss (CR 120.3), Creature → marked damage
///   (CR 119.3), Planeswalker → loyalty removal (CR 306.7). Targets not
///   chosen (the "up to" slack) and targets illegal on resolution fail
///   silently (CR 608.2b).
///
/// ## "each of up to two OTHER targets" (CR 601.2c)
/// The two minor clauses' targets must each be different from the major
/// target (and from each other). The engine's <see cref="TargetRequest"/>
/// declaration cannot yet express a "different-from-target[0]" constraint, so
/// — same V1 posture as <see cref="ArcTrailFactory"/>'s "any other target" —
/// the distinctness constraint is enforced at the agent/caller level, not in
/// the factory. The resolve body honours whatever targets were chosen and
/// deals the per-clause damage to each.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + trigger attached for observability;
///   nothing registered on a trigger manager. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — trigger registered so the
///   bus drives it automatically on <c>CreatureAttacksEvent</c>.
///
/// ## Deferred (v1 gaps)
/// - <b>Distinct-target enforcement at declaration</b>: "other targets" is
///   caller-enforced rather than expressed on the minor
///   <see cref="TargetRequest"/>s — matches Arc Trail's relaxed posture.
/// - <b>Trigger-on-stack timing</b>: same "trigger resolves now" V1 collapse
///   as every other attack-trigger factory in this repo.
/// - <b>Damage prevention / replacement (CR 615)</b>: damage flows straight
///   through <see cref="Fx.DealDamageAny"/>, same as the burn factories.
/// </summary>
[CardName("Drakuseth, Maw of Flames")]
public static class DrakusethMawOfFlamesFactory
{
    public const string CardName = "Drakuseth, Maw of Flames";
    public const string Slug = "drakuseth-maw-of-flames";

    /// <summary>CR 119.3 — damage dealt to the first ("any target") clause.</summary>
    public const int MajorDamage = 4;

    /// <summary>CR 119.3 — damage dealt to each of the two "other target" clauses.</summary>
    public const int MinorDamage = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Drakuseth with no trigger-manager wiring. The attack burn
    /// trigger is attached for shape observability. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Drakuseth with optional <see cref="TriggerManager"/> wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the attack trigger registers so a
    /// <c>CreatureAttacksEvent</c> for Drakuseth lands its ability on the stack
    /// automatically.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Creature — Dragon, {4}{R}{R}{R}, 7/7). No abilities in JSON — the
        // Flying marker + attack burn trigger are layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        var attackTrigger = BuildAttackTrigger(card, owner);
        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Build the attack burn triggered ability (CR 508.1f / 603.1): three
    /// <see cref="TargetRequest"/>s — one 1..1 "any target" (4 damage) and two
    /// 0..1 "any other target" (3 damage each). On resolution each chosen
    /// target is routed through <see cref="Fx.DealDamageAny"/> (CR 608.2b —
    /// gated per target shape).
    /// </summary>
    private static TriggeredAbility BuildAttackTrigger(Creature card, Player owner)
    {
        TriggeredAbility? ability = null;

        var effect = new Effect(
            $"{CardName}: attacks — {MajorDamage} damage to any target and "
                + $"{MinorDamage} to each of up to two other targets",
            () =>
            {
                if (ability == null) return;

                // CR 601.2c — first clause: 4 damage to the "any target".
                DealClause(ability, requestIndex: 0, amount: MajorDamage, card);

                // CR 115.1b — "up to two other targets": each of the two minor
                // clauses is optional, so a 0-target pick is a clean no-op.
                DealClause(ability, requestIndex: 1, amount: MinorDamage, card);
                DealClause(ability, requestIndex: 2, amount: MinorDamage, card);
            });

        ability = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                // "it deals 4 damage to any target"
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
                // "and 3 damage to each of up to two other targets" — two
                // independent 0..1 requests. Distinctness from the major target
                // (and each other) is caller-enforced (V1), see class remarks.
                new TargetRequest(
                    Description: "any other target",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
                new TargetRequest(
                    Description: "any other target",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        return ability;
    }

    /// <summary>
    /// Deal <paramref name="amount"/> damage to the chosen target of the
    /// <paramref name="requestIndex"/>-th <see cref="TargetRequest"/>, if one
    /// was chosen. A missing / empty clause (the "up to" slack) is a clean
    /// no-op (CR 608.2b).
    /// </summary>
    private static void DealClause(TriggeredAbility ability, int requestIndex, int amount, Creature source)
    {
        if (ability.ChosenTargets.Count <= requestIndex) return;
        var picks = ability.ChosenTargets[requestIndex];
        if (picks.Count == 0) return;

        var target = picks[0];
        if (target == null) return;
        Fx.DealDamageAny(target, amount, source); // CR 608.2b — gated per shape
    }
}
