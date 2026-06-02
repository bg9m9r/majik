using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bile Blight (Born of the Gods, {B}{B}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-01):
///   "Target creature and all other creatures with the same name as that
///    creature get -3/-3 until end of turn."
///
/// Bile Blight is the same-name <i>sweep</i> sibling of
/// <see cref="LastGaspFactory"/> (single-target -3/-3): it shares Last Gasp's
/// -3/-3-until-EOT per-creature effect (a Layer 7c
/// <see cref="PumpUntilEndOfTurnEffect"/>), but, like
/// <see cref="EchoingTruthFactory"/>'s "target permanent + all other
/// same-name permanents" shape, it additionally collects every other creature
/// sharing the target's name (CR 201.2) and applies the same -3/-3 to each.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {B}{B}. The base shape
///   (name / Instant type / {B}{B} cost) is materialised from the embedded
///   JSON definition (<c>bile-blight.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="EchoingTruthFactory"/> (the JSON <c>SpellDefinition</c> schema
///   does not yet express the same-name sweep, so the resolve behaviour is
///   layered on here via <see cref="BuildDefinition"/>).
/// - <b>Target creature</b> — a single 1..1 "target creature"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks every
///   player's battlefield, yielding creatures.
/// - <b>...and all other creatures with the same name</b> — on resolution,
///   after a CR 608.2b re-check that the target is still a creature on the
///   battlefield, the resolve snapshots every battlefield in the game and
///   collects every creature (target included) whose <see cref="ICard.Name"/>
///   equals the target's name. The match is by <i>name</i>, not card identity,
///   and is controller-agnostic (CR 201.2 — objects with the same English
///   name) — the caster's own same-name creatures get -3/-3 too.
/// - <b>-3/-3 until end of turn</b> — each collected creature gets its OWN
///   <see cref="PumpUntilEndOfTurnEffect"/>(-3, -3) registered on its OWN
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at EOT; CR 613
///   Layer 7c). A per-creature effect is required because
///   <see cref="PumpUntilEndOfTurnEffect.AppliesTo"/> matches by reference
///   identity. Same per-creature mechanic as <see cref="LastGaspFactory"/>.
///
/// ## Rules notes
/// - The same-name sweep is NOT separately targeted (the spell has a single
///   chosen target), so it ignores shroud / hexproof / protection on the
///   collateral creatures; only the single chosen target must be a legal
///   target (CR 608.2b). If the chosen target is illegal at resolution the
///   spell does nothing — no sweep without a legal target.
/// - When a swept creature's <see cref="Creature.ActiveEffects"/> is null
///   (shape-only tests without a live ContinuousEffectsService) the
///   registration for that creature is a silent no-op.
/// </summary>
[CardName("Bile Blight")]
public static class BileBlightFactory
{
    public const string CardName = "Bile Blight";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "bile-blight";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {B}{B}) from the
    /// embedded JSON definition. Resolve behaviour (-3/-3 to the target and
    /// every other same-name creature) is built on demand via
    /// <see cref="BuildDefinition"/>, mirroring <see cref="EchoingTruthFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the "target creature and all other creatures with the same name as
    /// that creature get -3/-3 until end of turn" <see cref="SpellDefinition"/>.
    /// On resolve:
    /// <list type="number">
    ///   <item>CR 608.2b re-check: the target must still be a
    ///     <see cref="Creature"/> on the Battlefield, else the spell does
    ///     nothing.</item>
    ///   <item>CR 201.2 — snapshot every battlefield and collect every creature
    ///     (target included) whose name equals the target's name,
    ///     controller-agnostic.</item>
    ///   <item>CR 514.2 / CR 613 Layer 7c — register a -3/-3
    ///     <see cref="PumpUntilEndOfTurnEffect"/> on each collected creature's
    ///     own <see cref="Creature.ActiveEffects"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="allPlayers">All players in the game. The same-name sweep
    /// walks every player's battlefield. Passed at cast time via
    /// <see cref="ChosenSpellParams.AllPlayers"/>; callers that skip the full
    /// cast flow supply it here directly (the closed-over fallback).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// creatures directly.</param>
    public static SpellDefinition BuildDefinition(
        IReadOnlyList<Player> allPlayers,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);
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
                    // Agent-prompt MVP: live gather every creature on any
                    // battlefield. Removal intent pushes the opponent's best
                    // small/multi-copy threat up the bot's ranker.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                // Prefer the live AllPlayers snapshot from ChosenSpellParams
                // (populated by SpellCastFlow); fall back to the closed-over
                // list when the caller built the SpellDefinition directly
                // (tests / bot probes). Same posture as Echoing Truth.
                var players = p.AllPlayers ?? allPlayers;
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: target creature and all other creatures "
                        + "with the same name get -3/-3 until end of turn",
                        () => Resolve(resolved, players)),
                };
            });
    }

    private static void Resolve(object resolved, IReadOnlyList<Player> allPlayers)
    {
        // CR 608.2b — resolution-time legality re-check. The chosen target must
        // still be a creature on the battlefield, else the entire spell does
        // nothing (no same-name sweep without a legal target).
        if (resolved is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        var targetName = target.Name;

        // CR 201.2 — collect every creature (target included) whose name
        // matches, across every battlefield, controller-agnostic. Snapshot
        // first; registering effects mutates ActiveEffects, not the
        // battlefield, but a snapshot keeps the iteration stable and mirrors
        // the Echoing Truth / Maelstrom Pulse sweep pattern.
        var toAffect = allPlayers
            .SelectMany(pl => pl.Zones.Battlefield.GetCards())
            .OfType<Creature>()
            .Where(c => string.Equals(c.Name, targetName, StringComparison.Ordinal))
            .ToList();

        foreach (var creature in toAffect)
        {
            // CR 514.2 / CR 613 Layer 7c — each creature needs its OWN -3/-3
            // effect because PumpUntilEndOfTurnEffect.AppliesTo matches by
            // reference identity. When ActiveEffects is null (shape tests
            // without a live ContinuousEffectsService) the registration is a
            // silent no-op for that creature.
            if (creature.ActiveEffects == null) continue;
            creature.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(creature, -3, -3));
        }
    }
}
