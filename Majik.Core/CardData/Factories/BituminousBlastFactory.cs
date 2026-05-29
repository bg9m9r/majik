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
/// Named-card factory for Bituminous Blast (Alara Reborn, {3}{B}{R}).
///
/// Instant. Oracle text (verified against Scryfall 2026-05-29):
///   "Cascade (When you cast this spell, exile cards from the top of your
///    library until you exile a nonland card that costs less. You may cast
///    it without paying its mana cost. Put the exiled cards on the bottom
///    in a random order.)
///    Bituminous Blast deals 4 damage to target creature."
///
/// Bituminous Blast pairs two analogue shapes already in the engine:
/// - <b>Cascade (CR 702.85)</b> — same <see cref="SpellCastEvent"/> trigger +
///   <see cref="CascadeAction.Cascade"/> routing as
///   <see cref="ArdentPleaFactory"/> / <see cref="BloodbraidElfFactory"/>.
/// - <b>4 damage to target creature (CR 119 / CR 120.3)</b> — same
///   single-creature-target <see cref="SpellDefinition"/> + resolution-time
///   legality re-check as <see cref="AbradeFactory"/>'s damage mode.
///
/// The base card shape (name / Instant type / {3}{B}{R} cost) is materialised
/// from the embedded JSON definition (<c>bituminous-blast.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the cascade triggered ability is
/// layered on here because the JSON <c>AbilityDefinition</c> schema does not
/// express Cascade yet (same posture as <see cref="ArdentPleaFactory"/>). The
/// resolve-time damage body lives in <see cref="BuildSpellDefinition"/> because
/// a <see cref="SpellDefinition"/> needs a target resolver supplied by the
/// caller's <see cref="Game.GameContext"/> (not expressible in the data-only
/// JSON schema — same posture as <see cref="PlayWithFireFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {3}{B}{R} (mana value 5 — a cascade
///   source whose hit pool is nonland MV ≤ 4).
/// - <b>Cascade trigger (CR 702.85)</b>: <see cref="TriggeredAbility"/> over
///   <see cref="SpellCastEvent"/> for this card,
///   <see cref="TriggeredAbility.ActiveZones"/> = { Stack } so it fires while
///   Bituminous Blast is on the stack as a spell. Cascade is type-agnostic —
///   the trigger keys on <c>ReferenceEquals(e.Spell.Card, card)</c>. On
///   resolution invokes <see cref="CascadeAction.Cascade"/> with
///   <c>sourceManaValue: 5</c>; the optional <c>willCast</c> predicate forwards
///   the controller's "you may" decision, and <c>onCascadeResolved</c> hands
///   the <see cref="CascadeAction.CascadeResult"/> back for caller-driven
///   free-cast through <see cref="Costs.CastFromExileAlternativeCost"/>.
/// - <b>4 damage to target creature</b>: single 1..1 "target creature" request
///   on the spell definition; on resolution deals 4 damage via
///   <see cref="OracleSpellBinder.DealDamage"/> (CR 120.3) after a CR 608.2b
///   resolution-time re-check that the target is still a creature on the
///   battlefield.
/// - <b>Cascade discovery</b>: registered in
///   <see cref="Players.Agents.CascadeAltCostProbe.DefaultIsCascadeCard"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Free-cast wiring</b>: caller-driven via the <c>onCascadeResolved</c>
///   hook (same shape as Ardent Plea / Bloodbraid Elf). The single-arg
///   dispatcher path attaches the trigger structurally with no SpellCastFlow
///   wired — suitable for shape / dispatcher tests.
/// </summary>
[CardName("Bituminous Blast")]
public static class BituminousBlastFactory
{
    public const string CardName = "Bituminous Blast";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "bituminous-blast";

    /// <summary>Cascade source mana value — Bituminous Blast is MV 5 ({3}{B}{R}).</summary>
    public const int CascadeSourceManaValue = 5;

    /// <summary>CR 119 — fixed 4 damage to target creature.</summary>
    public const int Damage = 4;

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches the cascade trigger structurally so the card shape is correct;
    /// the cascade effect has no result callback. No TriggerManager wiring.
    /// </summary>
    public static Instant Create(Player owner) =>
        Create(owner, triggers: null, willCast: null, onCascadeResolved: null);

    /// <summary>
    /// Fully-wired construction. <paramref name="triggers"/> registers the
    /// cascade trigger so a <see cref="SpellCastEvent"/> for this card lands on
    /// the stack automatically. <paramref name="willCast"/> is the controller's
    /// "you may" decision on the cascaded card (default = always cast).
    /// <paramref name="onCascadeResolved"/> receives the
    /// <see cref="CascadeAction.CascadeResult"/> for caller-driven free-cast
    /// through <see cref="Costs.CastFromExileAlternativeCost"/>.
    /// </summary>
    public static Instant Create(
        Player owner,
        TriggerManager? triggers,
        Func<ICard, bool>? willCast = null,
        Action<CascadeAction.CascadeResult>? onCascadeResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Instant / {3}{B}{R}) from the embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // CR 702.85 — Cascade. Type-agnostic: keyed on
        // ReferenceEquals(e.Spell.Card, card). The SpellCastEvent fires when
        // Bituminous Blast is announced on the stack (CR 601.2); ActiveZones =
        // { Stack } so the trigger is live while it is on the stack as a
        // spell. Same shape as Ardent Plea / Bloodbraid Elf.
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
    /// Build the <see cref="SpellDefinition"/> used when Bituminous Blast is
    /// cast. Single 1..1 "target creature" request, no X. On resolution deals
    /// <see cref="Damage"/> (4) damage to the chosen creature (CR 120.3) after
    /// a CR 608.2b resolution-time legality re-check (same shape as Abrade's
    /// damage mode).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand engine
    /// objects directly.</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                // Live gatherer: every creature on every battlefield (CR 301).
                // Bot ranks opponent creatures highest via Removal intent.
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen => new IEffect[]
            {
                new Effect($"{CardName} — deals 4 damage to target creature", () =>
                {
                    if (chosen.Targets.Count == 0) return;
                    var slot = chosen.Targets[0];
                    if (slot.Count == 0) return;
                    var resolved = targetResolver(slot[0]);

                    // CR 608.2b — resolution-time legality re-check: the target
                    // must still be a creature on the battlefield (same posture
                    // as Abrade's damage mode).
                    if (resolved is not Creature creature) return;
                    if (creature.Zone != ZoneType.Battlefield) return;

                    // CR 120.3 — deal 4 damage to the chosen creature.
                    OracleSpellBinder.DealDamage(creature, Damage);
                }),
            });
    }
}
