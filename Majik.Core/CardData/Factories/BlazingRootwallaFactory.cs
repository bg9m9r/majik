using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blazing Rootwalla (Torment and many reprints, {R}).
///
/// Creature — Lizard 1/1. Oracle text (verified against Scryfall 2026-06-10):
///   "{R}: This creature gets +2/+0 until end of turn. Activate only once
///    each turn.
///    Madness {0}"
///
/// ## Madness is intrinsic — NOT wired here (CR 702.35)
/// Madness {0} rides the card while it is in hand, so it cannot hang off the
/// per-permanent ability wiring. It is handled centrally by
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> (name → cost, Blazing
/// Rootwalla = {0}) consulted by the discard funnel
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> — a discarded Blazing
/// Rootwalla is routed to exile and offered for its {0} madness cost
/// automatically. This factory therefore implements ONLY the card's
/// non-madness body.
///
/// The base shape (name, Creature, Lizard subtype, {R}, 1/1) is materialised
/// from the embedded JSON definition (<c>blazing-rootwalla.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The activated ability is layered
/// on here — the JSON <c>AbilityDefinition</c> schema expresses neither the
/// firebreathing pump nor the once-per-turn lock (same posture as
/// <see cref="HiredClawFactory"/>).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Lizard at printed cost {R}, owner/controller wired.
/// - <b>{R}: This creature gets +2/+0 until end of turn (CR 602 / CR 613.1f
///   Layer 7c)</b> — a self-pump <see cref="ActivatedAbility"/> with a single
///   <see cref="ManaCostCost"/> of {R} and no target declarations. On
///   resolution it registers a <see cref="PumpUntilEndOfTurnEffect"/>(+2, 0)
///   on the card's <see cref="Creature.ActiveEffects"/>. When
///   <c>ActiveEffects</c> is null (shape-only path) the pump silently no-ops —
///   same posture as <see cref="WallOfFireFactory"/>'s {R}: +1/+0. Instant
///   speed (CR 602.5a default — no sorcery rider printed). The effect expires
///   at end of turn via <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>
///   (CR 514.2).
/// - <b>"Activate only once each turn" (CR 602.5e)</b> — an <c>int[1]{0}</c>
///   per-turn lock folded into the ability's <c>canActivateCheck</c> gate,
///   flipped to 1 by the resolve body and reset to 0 by a
///   <see cref="TurnStartedEvent"/> handler (CR 500.1) when an event bus is
///   supplied. Same lock shape as <see cref="HiredClawFactory"/>; folded into
///   the activation gate rather than a cost because the cost here is plain
///   mana.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — the <see cref="NamedCardFactory"/> dispatch
///   target. The pump ability is mounted with the once-per-turn lock active.
///   Without an event bus the lock is never reset (acceptable for shape /
///   single-turn scenarios — same posture as <see cref="HiredClawFactory"/> /
///   <see cref="QuirionRangerFactory"/>).
/// - <see cref="Create(Player, IEventBus?)"/> — wires the
///   <see cref="TurnStartedEvent"/> reset so the once-per-turn lock reopens
///   each turn in a real match.
/// </summary>
[CardName("Blazing Rootwalla")]
public static class BlazingRootwallaFactory
{
    public const string CardName = "Blazing Rootwalla";
    public const string Slug = "blazing-rootwalla";
    public const string PumpCost = "{R}";
    public const int PowerBoost = 2;
    public const int ToughnessBoost = 0;

    /// <summary>
    /// Construct Blazing Rootwalla with no event-bus wiring — the
    /// <see cref="NamedCardFactory"/> dispatch target. The {R}: +2/+0 pump is
    /// attached with the once-per-turn lock active; the lock is never reset
    /// without an event bus (suitable for shape / single-turn scenarios).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Blazing Rootwalla with optional <see cref="TurnStartedEvent"/>
    /// reset wiring. When <paramref name="eventBus"/> is supplied, the
    /// once-per-turn activation lock is reset at the start of every turn
    /// (CR 500.1) — mirrors <see cref="HiredClawFactory"/>.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Lizard
        // subtype, {R}, 1/1). The JSON carries no abilities — the pump is
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 602.5e — "Activate only once each turn." Closure shared between
        // the activation gate and the TurnStartedEvent reset handler.
        var usedThisTurn = new int[] { 0 };

        // CR 602 / CR 613.1f Layer 7c — "{R}: This creature gets +2/+0 until
        // end of turn." Plain self-pump activated ability (not a mana ability —
        // produces no mana; uses the stack). On resolution a
        // PumpUntilEndOfTurnEffect(+2, 0) is registered against
        // card.ActiveEffects, and the once-per-turn lock is flipped. null
        // ActiveEffects (shape-only path) = silent no-op (same posture as
        // WallOfFireFactory).
        var pumpEffect = new Effect(
            $"{CardName}: +{PowerBoost}/+{ToughnessBoost} until end of turn ({{R}})",
            () =>
            {
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, PowerBoost, ToughnessBoost));

                // CR 602.5e — record this turn's single permitted activation.
                usedThisTurn[0] = 1;
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(PumpCost) },
            effects: new IEffect[] { pumpEffect },
            // CR 602.5e — gate closed once already activated this turn.
            canActivateCheck: () => usedThisTurn[0] == 0));

        // CR 500.1 — reset the per-turn activation lock at the start of each
        // turn. Without an event bus the lock stays set after the first
        // activation (acceptable for shape / single-turn tests).
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => usedThisTurn[0] = 0);
        }

        return card;
    }
}
