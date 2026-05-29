using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flusterstorm (Commander 2011 / various reprints, {U}).
///
/// Instant. Oracle text (verified via Scryfall 2026-05):
///   "Counter target instant or sorcery spell unless its controller pays {1}.
///    Storm (When you cast this spell, copy it for each spell cast before it
///    this turn. You may choose new targets for the copies.)"
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - <b>Counter target instant or sorcery spell unless its controller pays
///   {1}</b> — mirrors the "unless pay" pattern from
///   <see cref="SpellPierceFactory"/> (N=2, noncreature) / <see cref="ManaLeakFactory"/>
///   (N=3, any spell), specialized here to N=1 with an instant-OR-sorcery
///   type filter (the <see cref="NegateFactory"/> defensive-resolve-time-filter
///   posture). At resolution (<see cref="BuildDefinition"/>):
///   1. CR 608.2b — if the target is not an instant or sorcery spell at
///      resolution time, the effect does nothing for it.
///   2. CR 118.4 — if the target's controller has {1} available, the engine
///      auto-pays (v1 auto-pay posture — same queue as Spell Pierce / Daze /
///      Mana Leak) and the counter no-ops.
///   3. Otherwise the spell is countered via
///      <see cref="OracleSpellBinder.RemoveFromStack"/> and its card moves to
///      the graveyard (CR 701.5). Uncounterable spells survive (CR 701.5b).
/// - <b>Storm trigger (CR 702.40)</b> — built via <see cref="StormHelper.Build"/>,
///   identical wiring to <see cref="BrainFreezeFactory"/>. Fires on this
///   spell's <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/> with
///   <c>activeZones = Stack</c> and copies it once for each OTHER spell the
///   controller cast before it this turn (count read from
///   <see cref="TurnState.SpellsCastByPlayer"/> at trigger-evaluation time).
///
/// ## Deferred (v1 gaps)
/// - Real "do you want to pay {1}?" agent prompt — same queue as Spell Pierce
///   / Daze / Mana Leak. v1 is deterministic: "pay if able."
/// - Storm copy semantics inherited from <see cref="Majik.Core.Services.SpellCopier"/>:
///   copies re-execute the original effect list in place rather than pushing
///   distinct <see cref="ISpell"/> stack objects, and the
///   "choose new targets for the copies" rider (CR 702.40a) is dropped — same
///   gap as <see cref="BrainFreezeFactory"/>.
/// </summary>
[CardName("Flusterstorm")]
public static class FlusterstormFactory
{
    public const string CardName = "Flusterstorm";
    public const string PrintedManaCost = "{U}";

    /// <summary>The {N} the target's controller must pay to avoid the counter.</summary>
    public const int UnlessPayN = 1;

    /// <summary>CardDef DSL — card shape only (no abilities). The storm
    /// trigger is attached by <see cref="Create"/>; resolve behaviour
    /// (counter unless pay {1}) is built via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    /// <summary>
    /// Construct Flusterstorm as an Instant card with the storm trigger
    /// attached structurally (no stack / turn-state wired — shape-only). The
    /// trigger is inspectable via <c>card.Abilities</c> for shape tests;
    /// bus-driven storm firing requires a stack + turn-state, supplied via
    /// <see cref="StormHelper.Build"/> at the cast site (same posture as
    /// <see cref="BrainFreezeFactory.Create(Player)"/>).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Instant)CardDefRuntime.Build(Define(), owner);

        var storm = StormHelper.Build(card, owner, stack: null, turnState: null);
        card.AddAbility(storm);

        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Single 1..1
    /// "target instant or sorcery spell" request; on resolution checks the
    /// instant-or-sorcery filter (CR 608.2b) and the unless-pay rider,
    /// countering only when the target's controller cannot / does not pay {1}.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live
    /// engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered
    /// spell. Null in pure-shape tests — the effect becomes a no-op.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var unlessCost = ManaCost.Zero.AddGenericCost(UnlessPayN);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target instant or sorcery spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — counter target instant or sorcery spell unless its controller pays {{{UnlessPayN}}}",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;

                            // CR 608.2b — only instant/sorcery spells are legal
                            // targets; if the target is neither at resolution
                            // time the effect does nothing for it (mirrors the
                            // defensive type filter in NegateFactory).
                            if (!spell.Card.HasType(CardType.Instant)
                                && !spell.Card.HasType(CardType.Sorcery))
                            {
                                return;
                            }

                            // CR 118.4 — if the target's controller has {1}
                            // in their mana pool, they auto-pay (v1 auto-pay
                            // posture). Flusterstorm no-ops.
                            if (spell.Controller is not null
                                && spell.Controller.PayMana(unlessCost))
                            {
                                return;
                            }

                            // Otherwise: counter. CR 701.5 / CR 701.5b —
                            // uncounterable spells survive the attempt.
                            if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
                            spell.Card.SetZone(ZoneType.Graveyard);
                        }),
                };
            });
    }
}
