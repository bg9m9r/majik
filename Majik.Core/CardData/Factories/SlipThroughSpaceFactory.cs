using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Slip Through Space (Oath of the Gatewatch, {U}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-02):
///   "Devoid (This card has no color.)
///    Target creature can't be blocked this turn.
///    Draw a card."
///
/// Slip Through Space composes three analogue shapes already in the engine:
/// - <b>Devoid (CR 702.114)</b> — stamped on the card via
///   <see cref="Card.SetDevoid"/> so <see cref="CardColors.GetColors"/>
///   returns empty regardless of the {U} pip; the
///   <see cref="KeywordAbility"/> marker is attached for ability-scan
///   discoverability. Same posture as <see cref="KozileksReturnFactory"/>.
/// - <b>"Target creature can't be blocked this turn" (CR 509.1c)</b> — a
///   single 1..1 <see cref="TargetRequest"/> resolving into a single-target
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBeBlocked"/>, EOT-scoped
///   (CR 514.2). Same restriction the {4},{T} ability of
///   <see cref="RoguesPassageFactory"/> installs (Demonic Dread installs the
///   mirror-image <see cref="CombatRestriction.CannotBlock"/>).
/// - <b>"Draw a card" (CR 121.1)</b> — routed through
///   <see cref="Fx.DrawCards"/> so any active replacement effect (Dredge
///   etc.) gets a shot; an empty library flags the SBA-driven loss
///   (CR 704.5b) inside Fx. Same cantrip path as
///   <see cref="SerumVisionsFactory"/>.
///
/// The base card shape (name / Sorcery type / {U} cost) is materialised from
/// the embedded JSON definition (<c>slip-through-space.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; Devoid and the targeted
/// unblockable + cantrip resolve effect are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither Devoid nor a targeted
/// combat-restriction grant yet (same posture as
/// <see cref="DemonicDreadFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Sorcery shape</b> at printed cost {U} (mana value 1), Devoid →
///   colorless.
/// - <b>Spell resolution</b>: <see cref="BuildDefinition"/> builds the
///   resolve-time <see cref="SpellDefinition"/> with one 1..1 "target
///   creature" <see cref="TargetRequest"/>. On resolution the chosen creature
///   is validated as still on the battlefield (CR 608.2b illegal-target
///   check), then a single-target <see cref="CombatRestrictionEffect"/>
///   (<see cref="CombatRestriction.CannotBeBlocked"/>,
///   <c>expiresAtEndOfTurn: true</c>) is registered on the target creature's
///   <see cref="Permanent.ActiveEffects"/>. Independently — the draw is not
///   gated on the target being legal — the caster draws one card (CR 121.1).
///   When the target has no live <see cref="ContinuousEffectsService"/> wired
///   (shape-only tests) the unblockable grant is a no-op; the draw still
///   happens.
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter the chosen object to "a creature on the battlefield" — the
///   resolution-time guard handles illegal targets (CR 608.2b), same posture
///   as Demonic Dread / Rogue's Passage.
/// </summary>
[CardName("Slip Through Space")]
public static class SlipThroughSpaceFactory
{
    public const string CardName = "Slip Through Space";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "slip-through-space";

    /// <summary>CR 702.114 — Devoid keyword marker string.</summary>
    public const string DevoidKeyword = "Devoid";

    /// <summary>
    /// Build Slip Through Space from the embedded JSON, stamp Devoid, and
    /// return the Sorcery shape. The targeted unblockable + cantrip resolve
    /// effect is built on demand via <see cref="BuildDefinition"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        // CR 702.114 — Devoid. Stamp IsDevoid so CardColors.GetColors returns
        // empty regardless of the {U} pip; attach the KeywordAbility marker
        // for ability-scan discoverability.
        card.SetDevoid(true);
        card.AddAbility(new KeywordAbility(DevoidKeyword, card, owner));

        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Slip Through
    /// Space's spell body — one 1..1 "target creature" request; on
    /// resolution register a single-target CannotBeBlocked combat restriction
    /// (CR 509.1c) on the chosen creature, EOT-scoped (CR 514.2), then draw a
    /// card (CR 121.1).
    ///
    /// CR 608.2b — illegal-target re-check at resolution: if the chosen
    /// target is no longer a creature on the battlefield, the unblockable
    /// grant is skipped (no-op). The draw is a separate instruction and
    /// happens regardless.
    /// </summary>
    /// <param name="caster">Slip Through Space's controller; draws the
    /// cantrip card and supplies the target candidate context.</param>
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
                    Intent: BotIntent.CombatTrick,
                    // Live gatherer: every creature on the battlefield across
                    // all players (Slip Through Space puts no further
                    // restriction on the target — any creature is legal).
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
                        $"{CardName}: target creature can't be blocked this turn (CR 509.1c)",
                        () => ApplyCannotBeBlocked(resolved)),
                    new Effect(
                        $"{CardName}: draw a card (CR 121.1)",
                        () => Fx.DrawCards(caster, 1)),
                };
            });
    }

    /// <summary>
    /// CR 509.1c — register a single-target CannotBeBlocked restriction on the
    /// chosen creature, scoped to this turn (CR 514.2). CR 608.2b — only
    /// applies if the target is still a creature on the battlefield at
    /// resolution. When the target has no live
    /// <see cref="ContinuousEffectsService"/> wired (shape-only tests) the
    /// grant silently no-ops.
    /// </summary>
    public static void ApplyCannotBeBlocked(object resolved)
    {
        if (resolved is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b
        if (target.ActiveEffects == null) return;

        // expiresAtEndOfTurn defaults to true → "this turn" (CR 514.2).
        target.ActiveEffects.Register(
            new CombatRestrictionEffect(CombatRestriction.CannotBeBlocked, target));
    }
}
