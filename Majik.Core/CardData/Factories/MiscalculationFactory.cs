using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Miscalculation (Urza's Saga, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Counter target spell unless its controller pays {2}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// ## Why a named factory (no template covers it)
/// Miscalculation is the exact "soft counter + cycling" shape of
/// <see cref="CensorFactory"/> — only the costs differ ({1}{U} vs {U},
/// counter-unless-{2} vs -{1}, cycling {2} vs {1}). It pairs the
/// "counter unless pay" rider (<see cref="QuenchFactory"/> /
/// <see cref="ManaLeakFactory"/>) with the shared Cycling primitive; no
/// single spell template binds the two together, so it gets a named factory.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}, blue. Card shape comes from the
///   embedded JSON (<c>miscalculation.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Counter target spell unless its controller pays {2}</b> — same
///   "auto-pay-if-able" posture as <see cref="CensorFactory"/> /
///   <see cref="ManaLeakFactory"/> / <see cref="QuenchFactory"/>: at
///   resolution the engine checks whether the target spell's controller
///   has {2} available; if yes, it is spent automatically and the counter
///   no-ops (CR 118.4 — "unless" cost). If no, the spell is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and its card goes to
///   the graveyard (CR 701.5).
/// - <b>Cycling {2}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{2}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + the Cycling keyword marker, layers
///   the <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.32a) onto the
///   cost stack, and on resolve publishes <see cref="CardCycledEvent"/> for
///   CR 702.32d subscribers.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Cycling activated
///   ability attached without an event bus (no CardCycledEvent
///   publication). Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Cycling
///   resolve publishes <see cref="CardCycledEvent"/> so "Whenever a
///   player cycles" triggers fire.
///
/// ## Deferred
/// - Real "do you want to pay {2}?" agent prompt — same queue as Censor /
///   Daze / Mana Leak / Mystical Dispute. v1 is deterministic: "pay if able."
/// </summary>
[CardName("Miscalculation")]
public static class MiscalculationFactory
{
    public const string CardName = "Miscalculation";
    public const string Slug = "miscalculation";
    public const string PrintedManaCost = "{1}{U}";
    public const string CyclingCost = "{2}";

    /// <summary>Pay-or-counter rider (CR 118.4 — "unless its controller pays {2}").</summary>
    public const int UnlessPayGeneric = 2;

    /// <summary>
    /// Construct Miscalculation with no event bus. The cycling activated
    /// ability is attached to the card shape; activation is gated to the
    /// controller's hand by <see cref="DiscardSelfCost.CanPay"/>. Shape-only
    /// — no <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Cards.Instant Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Miscalculation. The card shape (Instant {1}{U}, blue) is
    /// materialized from the embedded JSON definition; the Cycling {2}
    /// activated ability is then layered on via the shared primitive. When
    /// <paramref name="eventBus"/> is supplied the cycling resolve body
    /// publishes <see cref="CardCycledEvent"/> so CR 702.32d "Whenever a
    /// player cycles a card" triggers fire.
    /// </summary>
    public static Cards.Instant Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Cards.Instant)CardDefinitionFactory.Build(def, owner);

        // ----------------------------------------------------------------
        // Cycling {2} — CR 702.32. Routed through the shared CyclingFactory
        // primitive; the primitive appends the DiscardSelfCost hand-zone
        // gate (CR 702.32a) and the CardCycledEvent publish (CR 702.32d)
        // automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for
    /// Miscalculation's "counter target spell unless its controller pays {2}"
    /// effect. Mirrors <see cref="CensorFactory.BuildSpellDefinition"/> /
    /// <see cref="ManaLeakFactory.BuildDefinition"/>.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var unlessCost = ManaCost.Zero.AddGenericCost(UnlessPayGeneric);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Miscalculation — counter target spell unless its controller pays {2}", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 118.4 — target's controller may pay {2} to prevent
                        // the counter. v1 auto-pays when able (same posture as
                        // Censor / Mana Leak / Quench).
                        if (spell.Controller is not null
                            && spell.Controller.PayMana(unlessCost))
                        {
                            return;
                        }

                        // Controller couldn't pay — counter the spell (CR 701.5).
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
