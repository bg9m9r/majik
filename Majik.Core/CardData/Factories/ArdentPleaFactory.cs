using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ardent Plea (Alara Reborn, {1}{W}{U}).
///
/// Enchantment. Oracle text (verified against Scryfall 2026-05-29):
///   "Exalted (Whenever a creature you control attacks alone, that creature
///    gets +1/+1 until end of turn.)
///    Cascade (When you cast this spell, exile cards from the top of your
///    library until you exile a nonland card that costs less. You may cast
///    it without paying its mana cost. Put the exiled cards on the bottom
///    in a random order.)"
///
/// Ardent Plea pairs the two analogue shapes already in the engine:
/// - <b>Exalted (CR 702.90)</b> — same source-closure trigger pattern as
///   <see cref="IgnobleHierarchFactory"/> / <see cref="NobleHierarchFactory"/>.
/// - <b>Cascade (CR 702.85)</b> — same <see cref="SpellCastEvent"/> trigger +
///   <see cref="CascadeAction.Cascade"/> routing as
///   <see cref="ViolentOutburstFactory"/> / <see cref="BloodbraidElfFactory"/>.
///
/// The base card shape (name / Enchantment type / {1}{W}{U} cost) is
/// materialised from the embedded JSON definition (<c>ardent-plea.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the two triggered abilities and
/// the Exalted keyword marker are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither Cascade nor Exalted yet
/// (same posture as <see cref="RestlessSpireFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Enchantment shape</b> at printed cost {1}{W}{U} (mana value 3 — a
///   cascade source whose hit pool is nonland MV ≤ 2).
/// - <b>Exalted keyword marker</b> (CR 702.90) as a <see cref="KeywordAbility"/>.
/// - <b>Exalted trigger (CR 702.90b)</b>: fires on every
///   <see cref="CreatureAttacksEvent"/> for a creature this card's controller
///   controls, while Ardent Plea is on the battlefield. The "attacks alone"
///   check + +1/+1 EOT pump read from the injected
///   <c>attackingCreaturesSource</c> closure (identical to Ignoble Hierarch).
/// - <b>Cascade trigger (CR 702.85)</b>: <see cref="TriggeredAbility"/> over
///   <see cref="SpellCastEvent"/> for this card,
///   <see cref="TriggeredAbility.ActiveZones"/> = { Stack } so it fires while
///   Ardent Plea is on the stack as a spell. Cascade is type-agnostic — the
///   trigger keys on <c>ReferenceEquals(e.Spell.Card, card)</c>, so an
///   enchantment cascade source behaves exactly like the sorcery / instant /
///   creature analogues. On resolution invokes
///   <see cref="CascadeAction.Cascade"/> with <c>sourceManaValue: 3</c>; the
///   optional <c>willCast</c> predicate forwards the controller's "you may"
///   decision, and <c>onCascadeResolved</c> hands the
///   <see cref="CascadeAction.CascadeResult"/> back for caller-driven free-cast
///   through <see cref="Costs.CastFromExileAlternativeCost"/>.
/// - <b>Cascade discovery</b>: registered in
///   <see cref="Players.Agents.CascadeAltCostProbe.DefaultIsCascadeCard"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Free-cast wiring</b>: caller-driven via the <c>onCascadeResolved</c>
///   hook (same shape as Violent Outburst / Bloodbraid Elf). The single-arg
///   dispatcher path attaches the trigger structurally with no SpellCastFlow
///   wired — suitable for shape / dispatcher tests.
/// </summary>
[CardName("Ardent Plea")]
public static class ArdentPleaFactory
{
    public const string CardName = "Ardent Plea";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "ardent-plea";

    /// <summary>Cascade source mana value — Ardent Plea is MV 3 ({1}{W}{U}).</summary>
    public const int CascadeSourceManaValue = 3;

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches both triggers structurally so the card shape is correct; the
    /// Exalted pump body is a no-op (no attackers source) and the Cascade
    /// effect has no result callback. No TriggerManager wiring.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, attackingCreaturesSource: null,
            willCast: null, onCascadeResolved: null);

    /// <summary>
    /// Fully-wired construction.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register both triggers against.
    /// May be null — the triggers are still attached to the card shape.</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker list, read at Exalted-trigger resolution. May be null — pump
    /// body is a no-op.</param>
    /// <param name="willCast">Controller's "you may" decision on the cascaded
    /// card (default = always cast).</param>
    /// <param name="onCascadeResolved">Receives the
    /// <see cref="CascadeAction.CascadeResult"/> for caller-driven free-cast.</param>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource,
        Func<ICard, bool>? willCast = null,
        Action<CascadeAction.CascadeResult>? onCascadeResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Enchantment / {1}{W}{U}) from the embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Enchantment card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Enchantment but got "
                + $"'{built.GetType().Name}'.");
        }

        // CR 702.90 — Exalted keyword marker so data-side tools see it.
        card.AddAbility(new KeywordAbility("Exalted", card, owner));

        // ----------------------------------------------------------------
        // CR 702.90b — Exalted. "Whenever a creature you control attacks
        // alone, that creature gets +1/+1 until end of turn." Same
        // source-closure shape as Ignoble Hierarch / Noble Hierarch.
        // ----------------------------------------------------------------
        var exaltedEffect = new Effect(
            $"{CardName} Exalted: +1/+1 EOT when a creature attacks alone",
            () =>
            {
                if (attackingCreaturesSource == null) return;

                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();

                // Only creatures controlled by Ardent Plea's current
                // controller count (CR 702.90b — "a creature you control
                // attacks alone").
                var controlledAttackers = new List<Creature>();
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    if (!ReferenceEquals(atk.Controller, card.Controller)) continue;
                    controlledAttackers.Add(atk);
                }

                // "attacks alone" — exactly 1 controlled attacker.
                if (controlledAttackers.Count != 1) return;

                var soloAttacker = controlledAttackers[0];
                if (soloAttacker.ActiveEffects == null) return;

                soloAttacker.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(soloAttacker, 1, 1));
            });

        var exaltedTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) => ReferenceEquals(e.Attacker.Controller, card.Controller)),
            effects: new IEffect[] { exaltedEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(exaltedTrigger);
        triggers?.RegisterTriggeredAbility(exaltedTrigger);

        // ----------------------------------------------------------------
        // CR 702.85 — Cascade. Type-agnostic: keyed on
        // ReferenceEquals(e.Spell.Card, card). The SpellCastEvent fires when
        // Ardent Plea is announced on the stack (CR 601.2); ActiveZones =
        // { Stack } so the trigger is live while it is on the stack as a
        // spell. Same shape as Violent Outburst / Bloodbraid Elf.
        // ----------------------------------------------------------------
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
            condition: new EventTriggerCondition<SpellCastEvent>(
                (e, _) => ReferenceEquals(e.Spell.Card, card)),
            effects: new IEffect[] { cascadeEffect },
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(cascadeTrigger);
        triggers?.RegisterTriggeredAbility(cascadeTrigger);

        return card;
    }
}
