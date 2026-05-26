using Majik.Core.Abilities;
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
/// Named-card factory for Censor (Amonkhet, {U}).
///
/// Instant. Oracle text:
///   "Counter target spell unless its controller pays {1}.
///    Cycling {1} ({1}, Discard this card: Draw a card.)"
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - <b>Counter target spell unless its controller pays {1}</b> — same
///   "auto-pay-if-able" posture as <see cref="ManaLeakFactory"/> /
///   <see cref="QuenchFactory"/> / <see cref="DazeFactory"/>: at
///   resolution the engine checks whether the target spell's controller
///   has {1} available; if yes, it is spent automatically and the counter
///   no-ops (CR 118.4). If no, the spell is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and its card goes to
///   the graveyard (CR 701.5).
/// - <b>Cycling {1}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{1}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker, layers the <see cref="DiscardSelfCost"/> hand-zone
///   gate (CR 702.32a) onto the cost stack, and on resolve publishes
///   <see cref="CardCycledEvent"/> for CR 702.32d subscribers (Lightning
///   Rift, Curator of Mysteries, Living End, etc.).
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
/// - Real "do you want to pay {1}?" agent prompt — same queue as Daze /
///   Mana Leak / Mystical Dispute. v1 is deterministic: "pay if able."
/// </summary>
[CardName("Censor")]
public static class CensorFactory
{
    public const string CardName = "Censor";
    public const string PrintedManaCost = "{U}";
    public const string CyclingCost = "{1}";

    /// <summary>Pay-or-counter rider (CR 118.4 — "unless its controller pays {1}").</summary>
    public const int UnlessPayGeneric = 1;

    /// <summary>
    /// Construct Censor with no event bus. The cycling activated ability
    /// is attached to the card shape; activation is gated to the
    /// controller's hand by <see cref="DiscardSelfCost.CanPay"/>. Shape-
    /// only — no <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Instant Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Censor. When <paramref name="eventBus"/> is supplied the
    /// cycling resolve body publishes <see cref="CardCycledEvent"/> so
    /// CR 702.32d "Whenever a player cycles a card" triggers fire.
    /// </summary>
    public static Instant Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cycling {1} — CR 702.32. Routed through the shared
        // CyclingFactory primitive; the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Censor's
    /// "counter target spell unless its controller pays {1}" effect.
    /// Mirrors <see cref="QuenchFactory.BuildSpellDefinition"/> /
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
                    new Effect("Censor — counter target spell unless its controller pays {1}", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 118.4 — target's controller may pay {1} to prevent
                        // the counter. v1 auto-pays when able (same posture as
                        // Mana Leak / Quench / Daze).
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
