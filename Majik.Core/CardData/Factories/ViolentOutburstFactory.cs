using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Violent Outburst (Alara Reborn, {1}{R}{G}).
///
/// Instant. Oracle text:
///   "Creatures you control get +1/+0 and gain haste until end of turn.
///    Cascade (When you cast this spell, exile cards from the top of your
///    library until you exile a nonland card that costs less. You may cast
///    it without paying its mana cost. Put the exiled cards on the bottom
///    in a random order.)"
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {1}{R}{G} (mana value 3 — the
///   tightest cascade source for a sub-3 hit; cascade exiles until a
///   nonland card with mana value &lt; 3, so MV-2-and-below is the eligible
///   pool — paired with Crashing Footfalls and Living End in cascade
///   shells that aim to hit the dedicated payoffs at MV 3).
/// - <b>Cascade triggered ability (CR 702.85)</b>: <see cref="TriggeredAbility"/>
///   over <see cref="SpellCastEvent"/> for this card, mirroring
///   <see cref="CrashingFootfallsFactory"/> / <see cref="ShardlessAgentFactory"/>
///   / <see cref="BloodbraidElfFactory"/>'s shape. <b>Cascade is type-agnostic</b>
///   — the trigger condition is `ReferenceEquals(e.Spell.Card, card)` and
///   <see cref="CascadeAction.Cascade"/> takes only `(Player, int sourceMV)`,
///   so an instant-speed cascade source works the same as Crashing Footfalls'
///   sorcery cast. <see cref="TriggeredAbility.ActiveZones"/> = `{ Stack }`
///   so the trigger fires while Violent Outburst is on the stack as a spell.
///   On resolution invokes <see cref="CascadeAction.Cascade"/> with
///   <c>sourceManaValue: 3</c>; the optional <c>willCast</c> predicate
///   forwards the controller's "you may" decision. Free-cast of the
///   eligible card is caller-driven via <see cref="Costs.CastFromExileAlternativeCost"/>
///   on the <see cref="CascadeAction.CascadeResult.Eligible"/>.
/// - <b>Resolve effect</b>: <see cref="BuildSpellDefinition"/> snapshots the
///   caster's battlefield creatures at resolution time (CR 608.2 — effects
///   resolve against current game state) and for each one registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1, 0) (Layer 7c per CR 613.1c)
///   + a <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Haste") (Layer 6
///   keyword grant — CR 702.10) against the creature's
///   <see cref="Creature.ActiveEffects"/>; both expire on cleanup (CR
///   514.2). Creatures without an <see cref="ContinuousEffectsService"/>
///   wired no-op cleanly (same defensive guard as Violent Urge).
/// - <b>Cascade discovery</b>: registered in
///   <see cref="Players.Agents.CascadeAltCostProbe.DefaultIsCascadeCard"/>
///   so the bot's bidding heuristic / value layer sees Violent Outburst
///   as a cascade card without extra wiring (joins Crashing Footfalls,
///   Living End, Shardless Agent, Bloodbraid Elf in the ship list).
///
/// ## Deferred (v1 gaps)
/// - <b>Free-cast wiring</b>: caller-driven via the
///   <c>onCascadeResolved</c> hook (same shape as Crashing Footfalls /
///   Shardless Agent / Bloodbraid Elf). Single-arg dispatcher path attaches
///   the trigger structurally with no SpellCastFlow wired — suitable for
///   shape / dispatcher tests.
/// - <b>Pump/haste applies to creatures entering AFTER resolution</b>:
///   Violent Outburst's pump is a one-shot resolution-time snapshot per
///   CR 608.2 — creatures that enter the battlefield AFTER the spell
///   resolves do NOT get +1/+0 or haste (the printed text grants it to
///   "creatures you control", not "creatures you control until end of
///   turn" as a continuous static — that distinction matters for tokens
///   created later in the same turn).
/// </summary>
[CardName("Violent Outburst")]
public static class ViolentOutburstFactory
{
    public const string CardName = "Violent Outburst";
    public const string PrintedManaCost = "{1}{R}{G}";
    public const int CascadeSourceManaValue = 3;

    /// <summary>+P pump magnitude. Violent Outburst prints +1/+0.</summary>
    public const int PumpPower = 1;

    /// <summary>+T pump magnitude. Violent Outburst prints +1/+0.</summary>
    public const int PumpToughness = 0;

    /// <summary>Granted keyword — CR 702.10 Haste.</summary>
    public const string GrantedKeyword = "Haste";

    /// <summary>
    /// Single-arg dispatcher path. Attaches the cascade trigger
    /// structurally so card shape is correct; no TriggerManager wiring.
    /// The resolve-time pump/haste effect is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner) =>
        Create(owner, triggers: null, willCast: null, onCascadeResolved: null);

    /// <summary>
    /// Fully-wired construction. <paramref name="triggers"/> registers the
    /// cascade trigger so a <see cref="SpellCastEvent"/> for this card
    /// lands on the stack automatically. <paramref name="willCast"/> is
    /// the controller's "you may" decision on the cascaded card (default
    /// = always cast). <paramref name="onCascadeResolved"/> receives the
    /// <see cref="CascadeAction.CascadeResult"/> for caller-driven
    /// free-cast through <see cref="Costs.CastFromExileAlternativeCost"/>.
    /// </summary>
    public static Instant Create(
        Player owner,
        TriggerManager? triggers,
        Func<ICard, bool>? willCast = null,
        Action<CascadeAction.CascadeResult>? onCascadeResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.85 — Cascade. Type-agnostic: the trigger condition keys on
        // ReferenceEquals(e.Spell.Card, card), independent of instant /
        // sorcery / creature / planeswalker / enchantment card type. The
        // SpellCastEvent fires when the spell is announced on the stack
        // (CR 601.2) regardless of speed, so instant-speed cascade fires
        // at cast just like Crashing Footfalls' sorcery cast.
        // ----------------------------------------------------------------
        var cascadeCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        var cascadeEffect = new Effect(
            $"{CardName} — Cascade (CR 702.85)",
            () =>
            {
                var result = CascadeAction.Cascade(
                    controller: owner,
                    sourceManaValue: CascadeSourceManaValue,
                    willCast: willCast);
                onCascadeResolved?.Invoke(result);
            });

        var cascadeTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: cascadeCondition,
            effects: new IEffect[] { cascadeEffect },
            // Cascade trigger fires while the cascading spell is on the
            // stack — same active-zone choice as Crashing Footfalls /
            // Living End / Shardless Agent / Bloodbraid Elf.
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(cascadeTrigger);
        triggers?.RegisterTriggeredAbility(cascadeTrigger);

        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> Violent Outburst
    /// uses when cast — no targets, no modes; on resolution, enumerate the
    /// caster's battlefield creatures and register +1/+0 pump + Haste grant
    /// until end of turn on each (CR 613.1c Layers 6/7c, CR 514.2 EOT
    /// cleanup).
    /// </summary>
    /// <param name="caster">Spell controller — the battlefield whose
    /// creatures get the pump/haste rider.</param>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return SpellDefinition.Vanilla(_ => new IEffect[]
        {
            new Effect(
                $"Violent Outburst: creatures you control get +{PumpPower}/+{PumpToughness} and gain {GrantedKeyword} until end of turn",
                () => ApplyPumpAndHaste(caster)),
        });
    }

    /// <summary>
    /// Apply Violent Outburst's pump+haste rider to every creature
    /// <paramref name="controller"/> controls at the moment this effect
    /// runs. CR 608.2 — effects resolve against current game state, so the
    /// snapshot is taken at resolution time (not at cast announce).
    /// Creatures without a wired <see cref="ContinuousEffectsService"/>
    /// silently no-op (shape-only tests where ActiveEffects is null).
    /// </summary>
    public static void ApplyPumpAndHaste(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // Snapshot to a list before applying so any same-step zone-move
        // side effects (Glorious End / Lobotomy / cleanup) don't disturb
        // the enumeration. Pyroclasm uses the same `.ToList()` posture.
        var creatures = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();

        foreach (var creature in creatures)
        {
            // Shape-only safety — without a live ContinuousEffectsService
            // wired onto the creature, the pump/haste body silently
            // no-ops rather than NRE'ing. Mirrors Violent Urge's
            // defensive guard.
            if (creature.ActiveEffects == null) continue;

            // CR 613.1c Layer 7c — +1/+0 pump.
            creature.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));

            // CR 613.1c Layer 6 — keyword grant: Haste (CR 702.10).
            creature.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(creature, GrantedKeyword));
        }
    }
}
