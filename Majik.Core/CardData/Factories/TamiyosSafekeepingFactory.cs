using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tamiyo's Safekeeping (Streets of New Capenna, {G}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target permanent you control gains hexproof and indestructible until end
///    of turn. You gain 2 life. (A permanent with hexproof and indestructible
///    can't be the target of spells or abilities your opponents control. Damage
///    and effects that say "destroy" don't destroy it.)"
///
/// ## Implemented (v1)
/// - Instant card with printed mana cost {G}. Card shape comes from the
///   embedded JSON (<c>tamiyos-safekeeping.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> — same load path as
///   <see cref="BlossomingDefenseFactory"/>. The resolve-time body lives in
///   <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/>
///   carries a target request not expressible in the data-only JSON schema.
/// - <see cref="BuildDefinition"/> wires a 1..1 "target permanent you control"
///   <see cref="TargetRequest"/>. On resolve (CR 608.2b illegal-target guard
///   first):
///   1. Register a <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
///      "Hexproof" (CR 702.11b — "can't be the target of spells or abilities
///      your opponents control"). Honoured by
///      <see cref="Majik.Core.Targeting.TargetLegality"/>. EOT expiry CR 514.2.
///   2. Register a <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
///      "Indestructible" (CR 702.12b — "damage and effects that say 'destroy'
///      don't destroy it"). EOT expiry CR 514.2.
///   3. The caster gains 2 life (CR 119.3) via <see cref="Fx.GainLife"/>.
///
/// Mirrors <see cref="BlossomingDefenseFactory"/>'s single-target
/// "you control" + EOT keyword-grant shape, swapping the +2/+2 pump for a
/// second keyword grant (Indestructible) and adding the 2-life rider.
///
/// ## Deferred (v1 gaps — shared with the existing EOT keyword-grant family)
/// - <b>Non-creature permanents</b>: the printed target is any "permanent you
///   control", but <see cref="GrantKeywordUntilEndOfTurnEffect"/> attaches to
///   a <see cref="Creature"/> only (the continuous-effects / layer plumbing for
///   lands, artifacts, enchantments, and planeswalkers is not yet wired). When
///   the chosen permanent is not a creature the keyword grants are skipped —
///   the same documented limitation as <see cref="BorosCharmFactory"/> and
///   <see cref="SelflessSpiritFactory"/>. The 2-life rider still resolves.
/// - <b>Target-selection controller predicate</b>: the
///   <see cref="TargetRequest.Description"/> conveys "permanent you control"
///   but the engine's structural target filtering does not yet enforce control
///   predicates from the description string. The resolve body double-checks the
///   controller at resolution (CR 608.2b) and skips the grants when the chosen
///   permanent is not controlled by the caster — same posture as
///   <see cref="BlossomingDefenseFactory"/>'s "you control" rider.
/// </summary>
[CardName("Tamiyo's Safekeeping")]
public static class TamiyosSafekeepingFactory
{
    public const string CardName = "Tamiyo's Safekeeping";
    public const string Slug = "tamiyos-safekeeping";
    public const string PrintedManaCost = "{G}";

    /// <summary>Granted keyword — CR 702.11 Hexproof.</summary>
    public const string GrantedHexproof = "Hexproof";

    /// <summary>Granted keyword — CR 702.12 Indestructible.</summary>
    public const string GrantedIndestructible = "Indestructible";

    /// <summary>Life gained by the caster on resolution (CR 119.3).</summary>
    public const int LifeGain = 2;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve <see cref="SpellDefinition"/>. Single 1..1
    /// "target permanent you control" request, no X. On resolution:
    /// <list type="bullet">
    /// <item>If the chosen permanent is a <see cref="Creature"/> the caster
    ///   controls on the battlefield with a live effects service, register
    ///   "Hexproof" (CR 702.11b) and "Indestructible" (CR 702.12b) grants,
    ///   both expiring at cleanup (CR 514.2).</item>
    /// <item>The caster gains <see cref="LifeGain"/> life (CR 119.3). Per the
    ///   printed text the life gain is part of the same resolution, so it is
    ///   gated on the spell's single target still being legal at resolution:
    ///   if the only target has become illegal the spell does not resolve
    ///   (CR 608.2b) and the caster gains no life.</item>
    /// </list>
    /// </summary>
    public static SpellDefinition BuildDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target permanent you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Tamiyo's Safekeeping — target permanent you control gains hexproof and indestructible until end of turn; you gain 2 life",
                        () => Resolve(caster, raw)),
                };
            });
    }

    private static void Resolve(Player caster, object raw)
    {
        // CR 608.2b — the spell has exactly one target. If that target has
        // become illegal (not a permanent the caster controls on the
        // battlefield) the spell does not resolve at all: no keyword grants and
        // no life gain.
        if (raw is not Permanent target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (!ReferenceEquals(target.Controller, caster)) return;

        GrantKeywords(target);

        // CR 119.3 — the caster gains 2 life, part of the same resolution.
        Fx.GainLife(caster, LifeGain);
    }

    private static void GrantKeywords(Permanent target)
    {
        // The EOT keyword-grant path (GrantKeywordUntilEndOfTurnEffect) attaches
        // to a Creature only; the continuous-effects plumbing for non-creature
        // permanents (lands, artifacts, enchantments, planeswalkers) is not yet
        // wired. For those permanents the grants are skipped — a documented v1
        // gap shared with BorosCharmFactory / SelflessSpiritFactory. The 2-life
        // rider still resolves because the target was legal.
        if (target is not Creature creature) return;
        if (creature.ActiveEffects == null) return;

        // CR 702.11b — Hexproof. Layer-6 keyword grant with EOT expiry
        // (CR 514.2); honoured by TargetLegality.
        creature.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(creature, GrantedHexproof));

        // CR 702.12b — Indestructible. Layer-6 keyword grant with EOT expiry
        // (CR 514.2).
        creature.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(creature, GrantedIndestructible));
    }
}
