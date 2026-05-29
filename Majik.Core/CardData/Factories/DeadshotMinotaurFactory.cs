using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Deadshot Minotaur (Khans of Tarkir, {3}{R}{G}).
///
/// Creature — Minotaur 3/4. Oracle text (verified against Scryfall):
///   "When this creature enters, it deals 3 damage to target creature with
///    flying.
///    Cycling {R/G} ({R/G}, Discard this card: Draw a card.)"
///
/// The base shape (name, Creature, Minotaur subtype, {3}{R}{G}, 3/4) is
/// materialised from the embedded JSON definition
/// (<c>deadshot-minotaur.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="StormscaleScionFactory"/>. The two printed behaviours (the ETB
/// damage trigger and Cycling) are layered on top here because the JSON
/// <c>AbilityDefinition</c> schema does not yet express ETB triggers with
/// restricted targets or keyword markers.
///
/// ## Implemented (v1)
/// - <b>ETB triggered ability (CR 603.6a)</b>: fires when this creature
///   enters the battlefield, declaring a single 1..1
///   "target creature with flying" <see cref="TargetRequest"/>. The
///   candidate gatherer narrows to creatures with the Flying keyword
///   (CR 702.9) on any battlefield via
///   <see cref="CombatAbilities.HasFlying"/> — same restricted-target shape
///   as <see cref="PlummetFactory"/>. On resolution the chosen target takes
///   3 damage via <see cref="Fx.DealDamage"/> after a CR 608.2b
///   resolution-time legality re-check (still a creature, still on the
///   battlefield, still has Flying). Same ETB-damage wiring shape as
///   <see cref="MurderousRedcapFactory"/>, but with the flying-only target
///   restriction.
/// - <b>Cycling {R/G} (CR 702.32)</b>: routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{R/G}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a "Cycling" <see cref="KeywordAbility"/>
///   marker, layers <see cref="DiscardSelfCost"/> (CR 702.32a hand-zone
///   gate), and on resolve draws a card then publishes
///   <see cref="CardCycledEvent"/> (CR 702.32d) when an event bus is
///   supplied — identical to <see cref="MonstrousCarabidFactory"/>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. ETB trigger + cycling
///   activated ability attached without an event bus (no
///   <see cref="CardCycledEvent"/> publication). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired cycling
///   (publishes <see cref="CardCycledEvent"/> on resolve).
///
/// CR rule references: 205.3m (Minotaur subtype), 603.6a (ETB trigger),
/// 702.9 (Flying), 702.32 (Cycling), 608.2b (illegal-target re-check).
/// </summary>
[CardName("Deadshot Minotaur")]
public static class DeadshotMinotaurFactory
{
    public const string CardName = "Deadshot Minotaur";
    public const string Slug = "deadshot-minotaur";
    public const string CyclingCost = "{R/G}";
    public const int EtbDamage = 3;

    /// <summary>
    /// Construct Deadshot Minotaur with no event bus. The ETB damage trigger
    /// and the cycling activated ability are attached to the card shape;
    /// cycling resolve draws a card but publishes no
    /// <see cref="CardCycledEvent"/>. Suitable for dispatcher / shape /
    /// cost-stack tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Deadshot Minotaur. When <paramref name="eventBus"/> is
    /// supplied the cycling resolve body publishes
    /// <see cref="CardCycledEvent"/> so CR 702.32d "Whenever a player cycles
    /// a card" triggers fire.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Minotaur subtype, {3}{R}{G}, 3/4). The JSON carries no abilities —
        // the ETB trigger and Cycling are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB damage trigger (CR 603.6a). Declares one "target creature with
        // flying" TargetRequest; the candidate gatherer narrows to creatures
        // with Flying (CR 702.9) on any battlefield (same restricted-target
        // shape as Plummet). Resolution reads ChosenTargets and deals 3
        // damage after the CR 608.2b legality re-check.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: deal {EtbDamage} damage to target creature with flying",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                // CR 608.2b — resolution-time legality re-check: still a
                // creature, still on the battlefield, still has Flying
                // (CR 702.9).
                if (chosen[0][0] is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return;
                if (!CombatAbilities.HasFlying(target)) return;

                Fx.DealDamage(target, EtbDamage);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature with flying",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gather every creature with Flying on any
                    // battlefield (CR 702.9). Removal intent pushes the
                    // opponent's biggest flier up the bot's ranker.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => CombatAbilities.HasFlying(c))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Cycling {R/G} — CR 702.32. Shared CyclingFactory primitive appends
        // the DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }
}
