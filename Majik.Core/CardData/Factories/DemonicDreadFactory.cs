using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Demonic Dread (Alara Reborn, {1}{B}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-05-29):
///   "Cascade (When you cast this spell, exile cards from the top of your
///    library until you exile a nonland card that costs less. You may cast
///    it without paying its mana cost. Put the exiled cards on the bottom
///    in a random order.)
///    Target creature can't block this turn."
///
/// Demonic Dread pairs two analogue shapes already in the engine:
/// - <b>Cascade (CR 702.85)</b> — same <see cref="SpellCastEvent"/> trigger +
///   <see cref="CascadeAction.Cascade"/> routing as
///   <see cref="ViolentOutburstFactory"/> / <see cref="ArdentPleaFactory"/>
///   (cascade is type-agnostic — a sorcery cascade source behaves like the
///   instant / enchantment analogues).
/// - <b>"Target creature can't block this turn" (CR 509.1c)</b> — single
///   1..1 <see cref="TargetRequest"/> resolving into a single-target
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBlock"/>, EOT-scoped (CR 514.2).
///   Same restriction shape as <see cref="EarthshakerKhenraFactory"/>'s ETB
///   and <see cref="SunderingEruptionFactory"/>'s spell resolution (but with
///   no power threshold — any creature is a legal target).
///
/// The base card shape (name / Sorcery type / {1}{B}{R} cost) is materialised
/// from the embedded JSON definition (<c>demonic-dread.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the cascade triggered ability
/// and the targeted can't-block resolve effect are layered on here because the
/// JSON <c>AbilityDefinition</c> schema expresses neither Cascade nor a
/// targeted combat restriction yet (same posture as <see cref="ArdentPleaFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Sorcery shape</b> at printed cost {1}{B}{R} (mana value 3 — a cascade
///   source whose hit pool is nonland MV ≤ 2).
/// - <b>Cascade trigger (CR 702.85)</b>: <see cref="TriggeredAbility"/> over
///   <see cref="SpellCastEvent"/> for this card,
///   <see cref="TriggeredAbility.ActiveZones"/> = { Stack } so it fires while
///   Demonic Dread is on the stack as a spell. On resolution invokes
///   <see cref="CascadeAction.Cascade"/> with <c>sourceManaValue: 3</c>; the
///   optional <c>willCast</c> predicate forwards the controller's "you may"
///   decision, and <c>onCascadeResolved</c> hands the
///   <see cref="CascadeAction.CascadeResult"/> back for caller-driven
///   free-cast through <see cref="Costs.CastFromExileAlternativeCost"/>.
/// - <b>Spell resolution</b>: <see cref="BuildDefinition"/> builds the
///   resolve-time <see cref="SpellDefinition"/> with one 1..1 "target
///   creature" <see cref="TargetRequest"/>. On resolution the chosen creature
///   is validated as still on the battlefield (CR 608.2b illegal-target
///   check), then a single-target <see cref="CombatRestrictionEffect"/>
///   (<see cref="CombatRestriction.CannotBlock"/>, <c>expiresAtEndOfTurn:
///   true</c>) is registered on the target creature's
///   <see cref="Creature.ActiveEffects"/> — the combat validator queries
///   there. When the target has no live <see cref="ContinuousEffectsService"/>
///   wired (shape-only tests) the registration is a no-op.
/// - <b>Cascade discovery</b>: registered in
///   <see cref="Players.Agents.CascadeAltCostProbe.DefaultIsCascadeCard"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Free-cast wiring</b>: caller-driven via the <c>onCascadeResolved</c>
///   hook (same shape as Violent Outburst / Ardent Plea). The single-arg
///   dispatcher path attaches the trigger structurally with no SpellCastFlow
///   wired — suitable for shape / dispatcher tests.
/// </summary>
[CardName("Demonic Dread")]
public static class DemonicDreadFactory
{
    public const string CardName = "Demonic Dread";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "demonic-dread";

    /// <summary>Cascade source mana value — Demonic Dread is MV 3 ({1}{B}{R}).</summary>
    public const int CascadeSourceManaValue = 3;

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches the cascade trigger structurally so the card shape is
    /// correct; no TriggerManager wiring and the cascade effect has no
    /// result callback. The targeted can't-block resolve effect is built
    /// on demand via <see cref="BuildDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner) =>
        Create(owner, triggers: null, willCast: null, onCascadeResolved: null);

    /// <summary>
    /// Fully-wired construction.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the cascade trigger
    /// against. May be null — the trigger is still attached to the card
    /// shape.</param>
    /// <param name="willCast">Controller's "you may" decision on the cascaded
    /// card (default = always cast).</param>
    /// <param name="onCascadeResolved">Receives the
    /// <see cref="CascadeAction.CascadeResult"/> for caller-driven free-cast.</param>
    public static Sorcery Create(
        Player owner,
        TriggerManager? triggers,
        Func<ICard, bool>? willCast = null,
        Action<CascadeAction.CascadeResult>? onCascadeResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Sorcery / {1}{B}{R}) from the embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // CR 702.85 — Cascade. Type-agnostic: keyed on
        // ReferenceEquals(e.Spell.Card, card). The SpellCastEvent fires when
        // Demonic Dread is announced on the stack (CR 601.2); ActiveZones =
        // { Stack } so the trigger is live while it is on the stack as a
        // spell. Same shape as Violent Outburst / Ardent Plea.
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

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Demonic
    /// Dread's spell body — one 1..1 "target creature" request; on
    /// resolution, register a single-target CannotBlock combat restriction
    /// (CR 509.1c) on the chosen creature, EOT-scoped (CR 514.2).
    ///
    /// CR 608.2b — illegal-target re-check at resolution: if the chosen
    /// target is no longer a creature on the battlefield, the restriction is
    /// skipped (no-op).
    /// </summary>
    /// <param name="caster">Demonic Dread's controller; used for the effect
    /// label and the target candidate context.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer: every creature on the battlefield across
                    // all players (Demonic Dread puts no further restriction
                    // on the target — any creature is legal).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: target creature can't block this turn (CR 509.1c)",
                        () => ApplyCannotBlock(resolved)),
                };
            });
    }

    /// <summary>
    /// CR 509.1c — register a single-target CannotBlock restriction on the
    /// chosen creature, scoped to this turn (CR 514.2). CR 608.2b — only
    /// applies if the target is still a creature on the battlefield at
    /// resolution. When the target has no live
    /// <see cref="ContinuousEffectsService"/> wired (shape-only tests) the
    /// grant silently no-ops.
    /// </summary>
    public static void ApplyCannotBlock(object resolved)
    {
        if (resolved is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b
        if (target.ActiveEffects == null) return;

        target.ActiveEffects.Register(
            new CombatRestrictionEffect(CombatRestriction.CannotBlock, target));
    }
}
