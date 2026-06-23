using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Witchstalker Frenzy (Innistrad: Midnight Hunt,
/// {3}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "This spell costs {1} less to cast for each creature that attacked this
///    turn.
///    Witchstalker Frenzy deals 5 damage to target creature."
///
/// ## Implemented (v1)
///
/// - <b>Identity</b>: {3}{R} Instant, mana value 4, red; owner / controller
///   wired. Base shape materialised from the embedded JSON definition
///   (<c>witchstalker-frenzy.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>Self cost reduction (CR 117.7)</b>: "This spell costs {1} less to
///   cast for each creature that attacked this turn." Wired as a
///   <see cref="CostReductionAbility"/> in the
///   <see cref="CostReductionAbility.TotalReducer"/> shape. The reducer reads
///   a bus-driven per-card counter of the DISTINCT creatures declared as
///   attackers this turn — every creature, regardless of controller (the
///   printed clause is not caster-relative). The counter flips on each
///   <see cref="CreatureAttacksEvent"/> (CR 508.1f — declare attackers) and
///   resets on any <see cref="TurnStartedEvent"/> (the "this turn" window —
///   CR 500.4 / 514 cleanup). Same boxed-counter-off-the-EventBus pattern as
///   <see cref="WeddingAnnouncementFactory"/>'s distinct-attacker tally and
///   <see cref="StormwingEntityFactory"/>'s "instant/sorcery cast this turn"
///   flag.
///     - no creatures attacked this turn → {3}{R}
///     - 2 creatures attacked this turn → {1}{R} (two generic peeled)
///     - 4+ creatures attacked this turn → still {R} (CR 117.7c — generic
///       floors at the coloured pip; the {R} is never reduced)
///   <b>Generic-only reduction (CR 117.7c)</b>: the engine's cost-reduction
///   pipeline (<see cref="CostReduction.GetEffectiveCost"/>) reduces generic
///   mana and floors at the coloured pips, which exactly matches this card —
///   "{1} less" is plain generic. No colored-pip approximation is needed
///   (unlike Stormwing Entity's "{2}{U} less").
/// - <b>5 damage to target creature (CR 119 / 120.3)</b>: the resolve body
///   lives in <see cref="BuildSpellDefinition"/> (single 1..1 "target
///   creature" request; <see cref="Fx.DealDamageAny(object, int)"/> for 5;
///   non-creature resolved target is a no-op per CR 608.2b). Same payload
///   shape as <see cref="BombardFactory"/> at 5 damage instead of 4.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only (the overload
///   <see cref="NamedCardFactory"/> dispatches to). The cost reducer is
///   attached so structural assertions see it, but with no live
///   <see cref="EventBus"/> its attacker counter stays at 0 → printed cost.
///   Same posture as <see cref="StormwingEntityFactory"/>'s dispatcher path.
/// - <see cref="Create(Player, EventBus?)"/> — bus-wired. The attacker
///   counter is driven off <paramref name="eventBus"/> so the cost reduction
///   reflects the live declare-attackers state.
///
/// In live play the 5-damage body is bound from the card's oracle text by
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Damage.DamageCreatureTemplate"/>
/// (the "deals N damage to target creature" template) at cast time — the same
/// template path every burn-to-creature spell uses;
/// <see cref="BuildSpellDefinition"/> mirrors that body so the unique
/// behaviour is unit-testable in isolation (same posture as
/// <see cref="BombardFactory"/>).
/// </summary>
[CardName("Witchstalker Frenzy")]
public static class WitchstalkerFrenzyFactory
{
    public const string CardName = "Witchstalker Frenzy";
    public const string Slug = "witchstalker-frenzy";

    /// <summary>CR 119 — fixed 5 damage to the target creature.</summary>
    public const int Damage = 5;

    /// <summary>
    /// Construct Witchstalker Frenzy with no live wiring (shape / dispatcher
    /// path). The cost reducer attaches so structural assertions see it, but
    /// with no <see cref="EventBus"/> the attacker counter stays at 0 (printed
    /// cost). This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Instant Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Witchstalker Frenzy with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">EventBus driving the "creatures that attacked
    /// this turn" cost-reduction counter. When null the counter stays at 0
    /// (printed cost) — the correct shape-path default.</param>
    public static Instant Create(Player owner, EventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Instant, {3}{R}).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 117.7 — "This spell costs {1} less to cast for each creature
        // that attacked this turn." A bus-driven counter records the DISTINCT
        // creatures declared as attackers this turn — every controller's
        // creatures count (the clause is not caster-relative). Boxed in a
        // HashSet whose count the reducer closure reads; both the bus
        // subscriptions and the reducer share the one set (same
        // EventBus-driven tally as Wedding Announcement's distinct-attacker
        // count). With no live bus the set stays empty → no discount (printed
        // cost), the correct shape-path default.
        //
        // CR 117.7c — the engine reduces GENERIC mana only and floors at the
        // coloured pips. "{1} less" is plain generic, so the {R} pip is never
        // touched and the reduction maps onto the pipeline exactly (no
        // coloured-pip approximation needed).
        // ----------------------------------------------------------------
        var attackersThisTurn = new HashSet<Permanent>();

        if (eventBus != null)
        {
            // CR 508.1f — one CreatureAttacksEvent per declared attacker.
            eventBus.Subscribe<CreatureAttacksEvent>(e => attackersThisTurn.Add(e.Attacker));

            // "this turn" window resets at the start of any turn (CR 500.4 /
            // 514 cleanup) — a brand-new turn begins a fresh tally.
            eventBus.Subscribe<TurnStartedEvent>(_ => attackersThisTurn.Clear());
        }

        card.AddAbility(new CostReductionAbility(
            totalReducer: _ => attackersThisTurn.Count,
            description:
                "This spell costs {1} less to cast for each creature that " +
                "attacked this turn."));

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Witchstalker Frenzy's
    /// resolve body. Single 1..1 "target creature" request, no X. On
    /// resolution deals <see cref="Damage"/> (5) damage to the chosen target
    /// creature through <see cref="Fx.DealDamageAny"/> (CR 120.3); a
    /// non-creature resolved target is a no-op (CR 608.2b).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Witchstalker Frenzy: 5 damage to target creature", () =>
                    {
                        if (target is not Creature) return;
                        Fx.DealDamageAny(target, Damage);
                    }),
                };
            });
    }
}
