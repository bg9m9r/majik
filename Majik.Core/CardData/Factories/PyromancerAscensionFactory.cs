using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pyromancer Ascension (Zendikar, {U}{R}).
///
/// Enchantment. Oracle text:
///   "Whenever you cast an instant or sorcery spell that has the same name
///    as a card in your graveyard, you may put a quest counter on Pyromancer
///    Ascension.
///    As long as Pyromancer Ascension has two or more quest counters on it,
///    if you would cast an instant or sorcery spell, instead you cast that
///    spell and a copy of it."
///
/// ## Implemented (v1)
/// - <b>Enchantment {U}{R}</b> — card shape, owner / controller wired.
/// - <b>Quest-counter trigger (CR 603.1)</b>: an <see cref="EventTriggerCondition{T}"/>
///   over <see cref="SpellCastEvent"/> fires whenever the controller casts
///   an instant/sorcery whose name matches a card in their graveyard. On
///   resolve we place one <see cref="CounterType.Quest"/> counter on the
///   source via <see cref="Majik.Core.Primitives.Fx.PlaceCounter"/>. The
///   printed "you may" is taken automatically (no opt-out prompt) — same
///   posture as other "you may" tutor / counter-accumulation v1
///   implementations (see <see cref="GoblinEngineerFactory"/>'s ETB tutor).
/// - <b>Spell-copy trigger (CR 614 / approximation)</b>: the printed
///   wording is a static replacement effect ("if you would cast … instead
///   you cast that spell and a copy of it"). The engine does not yet have
///   a "replace a cast" replacement primitive; v1 approximates by
///   attaching a SECOND triggered ability that fires on the same
///   <see cref="SpellCastEvent"/>, gated to: (a) the spell's controller is
///   the Ascension's controller, (b) the spell is an instant or sorcery,
///   and (c) the Ascension has ≥2 quest counters on it. On resolve it
///   pushes one copy via <see cref="SpellCopier.PushCopyOfTopSpell"/>.
///   <b>Observable contract</b>: every instant/sorcery cast by the
///   controller while Ascension has ≥2 quest counters yields one extra
///   resolution of that spell's effects — matching the printed "and a
///   copy of it" semantics. The mechanical difference from the printed
///   replacement effect is timing on the stack (the copy is a triggered
///   ability rather than a stack-mate of the original), which is the
///   same shape <see cref="Majik.Core.Keywords.StormHelper"/> uses for
///   Storm copies.
/// - <b>Threshold gate</b>: the copy trigger's condition reads the live
///   quest-counter count on the source at trigger-evaluation time so a
///   single cast that simultaneously crosses the threshold (via the
///   quest-counter trigger queued ahead of it) does not retroactively
///   copy itself — the threshold check happens before the queued quest
///   trigger has resolved.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Both triggers are
///   attached for shape inspection but not registered with a
///   <see cref="TriggerManager"/>; spell-cast events do not reach them.
///   Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager?, Majik.Core.Stack.Stack?)"/>
///   — fully wired. Triggers register and the copy trigger pushes copies
///   onto <paramref name="stack"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" opt-out on the quest-counter trigger</b>: the counter
///   is placed automatically. A full implementation would prompt the
///   controller's agent (yes/no) at resolution.
/// - <b>Static replacement primitive</b>: the printed wording is a
///   continuous static replacement ("if you would cast … instead you
///   cast that spell and a copy of it"). The engine has no "cast" event
///   that can be replaced; the copy-trigger approximation makes the
///   observable outcome (one extra resolution per cast at ≥2 counters)
///   correct but stacks the copy as a triggered ability rather than a
///   sibling stack object. Same shape gap as the
///   <see cref="Majik.Core.Keywords.StormHelper"/> copy semantics.
/// - <b>Re-targeting copies</b>: inherited from
///   <see cref="SpellCopier"/> — copies reuse the original spell's
///   targets verbatim.
/// </summary>
[CardName("Pyromancer Ascension")]
public static class PyromancerAscensionFactory
{
    public const string CardName = "Pyromancer Ascension";
    public const string PrintedManaCost = "{U}{R}";
    public const int CopyThreshold = 2;

    /// <summary>
    /// Construct Pyromancer Ascension with no live trigger wiring. Both
    /// triggered abilities are attached to the card for shape inspection
    /// but not registered with a <see cref="TriggerManager"/>; spell-cast
    /// events do not reach them.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, stack: null);

    /// <summary>
    /// Construct Pyromancer Ascension. When <paramref name="triggers"/>
    /// is supplied both triggered abilities are registered so
    /// <see cref="SpellCastEvent"/> publications by the controller drive
    /// them. The copy trigger pushes copies onto <paramref name="stack"/>
    /// via <see cref="SpellCopier.PushCopyOfTopSpell"/>; absent a stack
    /// the copy effect no-ops (shape path).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Trigger 1 — Quest counter accumulation (CR 603.1).
        //   "Whenever you cast an instant or sorcery spell that has the
        //    same name as a card in your graveyard, you may put a quest
        //    counter on Pyromancer Ascension."
        //
        // The "you may" is auto-taken (v1). Name match is case-sensitive
        // verbatim against the controller's graveyard.
        // ----------------------------------------------------------------
        var questCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (e.Spell is not Majik.Core.Spells.Spell spell) return false;
            // Only the Ascension's controller's casts qualify.
            if (!ReferenceEquals(spell.Controller, card.Controller ?? owner)) return false;
            var spellCard = spell.Card;
            if (spellCard is null) return false;
            if (!spellCard.HasType(CardType.Instant) && !spellCard.HasType(CardType.Sorcery))
            {
                return false;
            }
            // Same-name match against the controller's graveyard.
            var controller = card.Controller ?? owner;
            var name = spellCard.Name;
            foreach (var raw in controller.Zones.Graveyard.GetCards())
            {
                if (raw is Card g && g.Name == name) return true;
            }
            return false;
        });

        var questEffect = new Effect(
            $"{CardName}: put a quest counter on this enchantment",
            () =>
            {
                // CR 603.6c — leaves-the-battlefield safety: only place
                // the counter while still on the battlefield.
                if (card.Zone != ZoneType.Battlefield) return;
                Majik.Core.Primitives.Fx.PlaceCounter(card, CounterType.Quest, 1);
            });

        var questTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: questCondition,
            effects: new IEffect[] { questEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(questTrigger);
        triggers?.RegisterTriggeredAbility(questTrigger);

        // ----------------------------------------------------------------
        // Trigger 2 — Copy approximation for the printed static
        // replacement effect.
        //   "As long as Pyromancer Ascension has two or more quest
        //    counters on it, if you would cast an instant or sorcery
        //    spell, instead you cast that spell and a copy of it."
        //
        // v1 approximation: a triggered ability on SpellCastEvent. Gates
        // identical to questCondition's controller + instant/sorcery
        // check; the same-name graveyard check is replaced by a live
        // ≥2-quest-counter threshold read on the source.
        // ----------------------------------------------------------------
        Majik.Core.Spells.Spell? capturedSpell = null;

        var copyCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (card.Zone != ZoneType.Battlefield) return false;
            if (e.Spell is not Majik.Core.Spells.Spell spell) return false;
            if (!ReferenceEquals(spell.Controller, card.Controller ?? owner)) return false;
            var spellCard = spell.Card;
            if (spellCard is null) return false;
            if (!spellCard.HasType(CardType.Instant) && !spellCard.HasType(CardType.Sorcery))
            {
                return false;
            }
            // CR 122 — live threshold read on the source. Quest counters
            // accumulated earlier this turn (via the quest trigger) count
            // immediately; the queued quest trigger for THIS cast has not
            // yet resolved (triggers go on the stack), so the threshold
            // does NOT include a not-yet-placed counter for the current
            // spell.
            if (card.Counters.Count(CounterType.Quest) < CopyThreshold) return false;
            capturedSpell = spell;
            return true;
        });

        var copyEffect = new Effect(
            $"{CardName}: copy the just-cast instant or sorcery (CR 707.10)",
            () =>
            {
                if (capturedSpell is null) return;
                if (stack is null) return;
                SpellCopier.PushCopyOfTopSpell(stack, capturedSpell);
            });

        var copyTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: copyCondition,
            effects: new IEffect[] { copyEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(copyTrigger);
        triggers?.RegisterTriggeredAbility(copyTrigger);

        return card;
    }
}
