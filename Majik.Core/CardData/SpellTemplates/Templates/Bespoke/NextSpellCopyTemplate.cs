using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Services;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Galvanic Iteration family — "When you next cast an instant or sorcery
/// spell this turn, copy that spell. You may choose new targets for the
/// copy."
///
/// Cards bound: Doublecast, Galvanic Iteration, Howl of the Horde.
///
/// ## Implemented (v1)
/// - Pattern match anchored at the start of the (normalized) oracle text.
/// - On cast, register a one-shot <see cref="DelayedTriggeredAbility"/>
///   that listens for the caster's next <see cref="SpellCastEvent"/>
///   matching an instant or sorcery (excluding the source spell itself —
///   we don't want Doublecast to copy itself).
/// - When the trigger fires it calls
///   <see cref="SpellCopier.PushCopyOfTopSpell"/> with the just-cast
///   spell. v1 is a lossy stub — see <see cref="SpellCopier"/> for the
///   list of gaps (no real stack object, no retargeting, simultaneous
///   resolution).
///
/// ## Deferred (v1 gaps)
/// - <b>"You may choose new targets for the copy"</b>: the v1 copier reuses
///   the original targets verbatim. Tracked in <see cref="SpellCopier"/>.
/// - <b>"this turn" expiry</b>: the trigger is one-shot and self-unregisters
///   on fire, so the practical effect of "fires only on the NEXT spell" is
///   preserved — but if no spell is cast for the rest of the turn the
///   trigger silently lingers. The leaked registration doesn't fire on
///   later turns' spells in practice (the controller's next spell would
///   trigger it, which is wrong cross-turn) — acceptable lossy semantic
///   until a turn-scoped cleanup hook lands.
/// - <b>Trailing rider clauses</b> on Galvanic Iteration (Flashback) and
///   Howl of the Horde (Raid bonus copy) are dropped — those families
///   need separate templates / additional-cost machinery.
/// </summary>
public sealed class NextSpellCopyTemplate : ISpellTemplate
{
    // Anchored at the start — must be the leading clause of the oracle text.
    // The trailing rider clauses (Flashback on Galvanic Iteration; Raid bonus
    // copy on Howl of the Horde) are matched-but-ignored — the regex doesn't
    // consume them and the template's TryExtractParams only checks IsMatch,
    // which is start-of-string-anchored via the ^.
    private static readonly Regex Pattern = new(
        @"^\s*when\s+you\s+next\s+cast\s+an\s+instant\s+or\s+sorcery\s+spell\s+this\s+turn,\s+copy\s+that\s+spell\.",
        RegexOptions.IgnoreCase);

    public int Priority => 75;
    public string Name => "NextSpellCopy";

    // Closest match in the existing BotIntent enum. "Copy a burn spell" =
    // Burn-like; "copy a removal spell" = Removal-like. The bot picks this
    // up as a buff/protection-ish hold-mana signal, which is roughly the
    // right call (you'd hold mana to chain into a real spell).
    public BotIntent Intent => BotIntent.Buff;

    /// <summary>
    /// Requires <see cref="SpellBindContext.Triggers"/> to register the
    /// delayed trigger AND <see cref="SpellBindContext.Stack"/> so the
    /// copier can (eventually) push a real stack object. Both must be wired
    /// for the binding to be safe.
    /// </summary>
    public bool CanBind(SpellBindContext ctx) =>
        ctx.Triggers is not null && ctx.Stack is not null;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var caster = ctx.Caster;
        var triggers = ctx.Triggers!;
        var stack = ctx.Stack!;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[] { new Effect("register next-spell copy trigger", () =>
            {
                // The delayed trigger fires on the controller's next instant
                // or sorcery SpellCastEvent. The triggering spell isn't
                // visible to the pre-built effect list — TriggeredAbility's
                // effects don't see the GameEvent that fired them. Workaround:
                // the trigger condition runs immediately when the event
                // publishes (TriggerManager.EvaluateTriggers), so it can
                // capture the spell into a closure before the queued effect
                // runs at stack resolution. Lossy in one edge case: if two
                // matching spells publish back-to-back before the effect
                // resolves, the second overwrites the first — but
                // DelayedTriggeredAbility self-unregisters on first match
                // (TriggerManager.EvaluateTriggers removes it from _abilities
                // after queueing), so that race is bounded to "the spell that
                // re-fires the trigger before the queue drains", which is not
                // a thing the engine produces in normal flow.
                Majik.Core.Spells.ISpell? capturedSpell = null;

                var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
                {
                    var spell = e.Spell;
                    if (!ReferenceEquals(spell.Controller, caster)) return false;
                    var card = spell.Card;
                    if (card is null) return false;
                    var isInstantOrSorcery =
                        card.HasType(CardType.Instant) || card.HasType(CardType.Sorcery);
                    if (!isInstantOrSorcery) return false;
                    capturedSpell = spell;
                    return true;
                });

                var copyEffect = new Effect("copy next spell", () =>
                {
                    if (capturedSpell is null) return;
                    SpellCopier.PushCopyOfTopSpell(stack, capturedSpell);
                });

                var delayed = new DelayedTriggeredAbility(
                    source: caster,
                    controller: caster,
                    condition: condition,
                    effects: new IEffect[] { copyEffect });
                triggers.RegisterDelayed(delayed);
            }) });
    }
}
