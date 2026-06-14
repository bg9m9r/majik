using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Make Disappear (Streets of New Capenna, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Casualty 1 (As you cast this spell, you may sacrifice a creature with
///    power 1 or greater. When you do, copy this spell and you may choose a new
///    target for the copy.)
///    Counter target spell unless its controller pays {2}."
///
/// ## Why a named factory (no template covers it)
/// The base spell is the exact "counter target spell unless its controller
/// pays {2}" shape of <see cref="MiscalculationFactory"/> /
/// <see cref="ManaLeakFactory"/> (Mana Leak charges {3}; Make Disappear charges
/// {2}). The "unless pay" rider is not bound by any single spell template, so
/// it gets a named factory.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}, blue. Card shape comes from the embedded
///   JSON (<c>make-disappear.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Counter target spell unless its controller pays {2}</b> — same
///   "auto-pay-if-able" posture as <see cref="MiscalculationFactory"/> /
///   <see cref="ManaLeakFactory"/> / <see cref="QuenchFactory"/>: at resolution
///   the engine checks whether the target spell's controller has {2} available;
///   if yes, it is spent automatically and the counter no-ops (CR 118.4 —
///   "unless" cost). If no, the spell is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and its card goes to the
///   graveyard (CR 701.5).
///
/// ## Deferred
/// - <b>Casualty 1</b> (CR 702.153) — the engine has no Casualty infrastructure
///   (sacrifice-a-creature-as-an-additional-cost + copy-this-spell rider). The
///   normalizer strips the keyword line, so the card behaves as the base soft
///   counter. Tracked as a v1 deferral; revisit when Casualty / spell-copy
///   infra lands.
/// - Real "do you want to pay {2}?" agent prompt — same queue as Censor / Daze
///   / Mana Leak / Mystical Dispute. v1 is deterministic: "pay if able."
/// </summary>
[CardName("Make Disappear")]
public static class MakeDisappearFactory
{
    public const string CardName = "Make Disappear";
    public const string Slug = "make-disappear";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>Pay-or-counter rider (CR 118.4 — "unless its controller pays {2}").</summary>
    public const int UnlessPayGeneric = 2;

    /// <summary>
    /// Construct Make Disappear. The card shape (Instant {1}{U}, blue) is
    /// materialized from the embedded JSON definition. The Casualty 1 rider is
    /// deferred (no engine infra), so no additional ability is layered on.
    /// </summary>
    public static Cards.Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Cards.Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Make Disappear's
    /// "counter target spell unless its controller pays {2}" effect. Mirrors
    /// <see cref="MiscalculationFactory.BuildSpellDefinition"/> /
    /// <see cref="ManaLeakFactory.BuildDefinition"/>.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    // CR 118.4 — ask the target spell's controller whether to
                    // pay to keep it on the stack; counter on no / can't afford
                    // (CR 701.5). See PayUnlessCounterRider.
                    Majik.Core.Primitives.PayUnlessCounterRider.Build(
                        $"Make Disappear — counter target spell unless its controller pays {{{UnlessPayGeneric}}}",
                        stack,
                        () => targetResolver(raw) as ISpell,
                        unlessPayN: UnlessPayGeneric),
                };
            });
    }
}
