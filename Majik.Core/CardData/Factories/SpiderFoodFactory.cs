using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spider Food (Bloomburrow, {2}{G}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-24):
///   "Destroy up to one target artifact, enchantment, or creature with flying.
///    Create a Food token. (It's an artifact with "{2}, {T}, Sacrifice this
///    token: You gain 3 life.")"
///
/// Spider Food is the green "up to one"-destroy cousin of
/// <see cref="BakeIntoAPieFactory"/> (Bake into a Pie — the {2}{B}{B} instant
/// that destroys a creature and mints a Food token): the same destroy-then-mint
/// resolve, but (a) the destroy is OPTIONAL — "up to one target" (CR 115.1a) so
/// the target request is 0..1, modelled on
/// <see cref="WildfireHowlFactory"/>'s safe 0..1 target indexing, and (b) the
/// target filter is widened to "artifact, enchantment, or creature WITH FLYING"
/// — a flier-only creature clause borrowed from
/// <see cref="LoranOfTheThirdPathFactory"/>'s artifact-or-enchantment destroy
/// plus the <see cref="CombatAbilities.HasFlying"/> predicate.
///
/// ## Implemented (v1)
/// - <b>Sorcery shape</b> at printed cost {2}{G}. The base shape (name /
///   Sorcery type / {2}{G} cost) is materialised from the embedded JSON
///   definition (<c>spider-food.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="BakeIntoAPieFactory"/> (the JSON <c>SpellDefinition</c> schema
///   does not express an up-to-one filtered destroy + token mint, so the
///   resolve behaviour is layered on here via <see cref="BuildDefinition"/>).
/// - <b>Destroy up to one target artifact, enchantment, or creature with
///   flying</b> — <see cref="BuildDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 0..1 target request
///   (CR 115.1a — "up to one" is optional, so zero chosen targets resolves as a
///   clean no-op for the destroy half). The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding cards that are an artifact, an
///   enchantment, or a creature with Flying
///   (<see cref="CombatAbilities.HasFlying"/> — CR 702.9). The bot's
///   <see cref="BotIntent.Removal"/> ranker pushes opponent permanents up.
/// - On resolution: re-checks the chosen target is still a legal artifact /
///   enchantment / flying-creature on the Battlefield (CR 608.2b illegal-target
///   gate), then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
///   <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
///   indestructible (CR 702.12) / regeneration (CR 701.15) shields are honoured.
/// - <b>Create a Food token</b> — unconditionally mints one Food token
///   (CR 111.10) for the caster via <see cref="TokenFactory.CreateFood"/>. The
///   Food half is NOT gated on the destroy half: the printed wording is two
///   independent sentences, so the token is created even when no target was
///   chosen or the chosen target is illegal at resolution.
/// </summary>
[CardName("Spider Food")]
public static class SpiderFoodFactory
{
    public const string CardName = "Spider Food";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "spider-food";

    /// <summary>
    /// Materialise the Sorcery card shape (name / Sorcery / {2}{G}) from the
    /// embedded JSON definition. Resolve behaviour (destroy up to one filtered
    /// target + create a Food token) is built on demand via
    /// <see cref="BuildDefinition"/>, mirroring <see cref="BakeIntoAPieFactory"/>.
    /// </summary>
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

        return card;
    }

    /// <summary>
    /// Build the "destroy up to one target artifact, enchantment, or creature
    /// with flying; create a Food token" <see cref="SpellDefinition"/>. The
    /// target request is 0..1 (CR 115.1a — optional). On resolve: when a target
    /// was chosen, validates it is still a legal artifact / enchantment /
    /// flying-creature on the Battlefield (CR 608.2b — illegal/absent target →
    /// destroy half is a no-op); when valid, destroys via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
    /// indestructible / regeneration shields are honoured. Then mints one Food
    /// token for the caster (CR 111.10) — unconditionally, since the printed
    /// wording is two independent sentences.
    /// </summary>
    /// <param name="caster">Controller of Spider Food — also the player who
    /// receives the Food token on resolve (CR 111.10 — a token is created under
    /// the control of the player the spell instructs).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    /// <param name="zoneService">Optional ZoneService so the minted Food token's
    /// battlefield ETB publishes <c>CardMovedEvent</c>; null in shape tests.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                // 0..1 "up to one" optional destroy target (CR 115.1a). The
                // base spell never gates the cast on a target — a Spider Food
                // cast with no legal/chosen target still mints the Food.
                new TargetRequest(
                    Description: "up to one target artifact, enchantment, or creature with flying",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every artifact / enchantment
                    // / flying-creature on any battlefield. Removal intent in the
                    // bot's ranker pushes opponent permanents up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(IsLegalDestroyTarget)
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                // "Up to one": Targets[0] is empty when no target was chosen
                // (CR 115.1a) — safe index, same posture as Wildfire Howl's
                // optional any-target.
                var raw = p.Targets.Count > 0 && p.Targets[0].Count > 0
                    ? p.Targets[0][0]
                    : null;
                var resolved = raw != null ? targetResolver(raw) : null;

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy up to one artifact/enchantment/flier; create a Food token",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check for
                            // the destroy half only. A missing/illegal target
                            // does not stop the second sentence (the Food mint).
                            if (resolved is Permanent target
                                && target.Zone == ZoneType.Battlefield
                                && IsLegalDestroyTarget(target))
                            {
                                // CR 701.7 — Destroy. Indestructible (CR 702.12)
                                // / regeneration (CR 701.15) honoured via the
                                // Destroy-reason gate.
                                OracleSpellBinder.MoveToGraveyard(
                                    target,
                                    Majik.Core.Zones.ZoneMoveReason.Destroy);
                            }

                            // CR 111.10 — create one Food token for the caster.
                            // Unconditional: the printed second sentence is not
                            // gated on the destroy resolving.
                            TokenFactory.CreateFood(caster, zoneService);
                        }),
                };
            });
    }

    /// <summary>
    /// "artifact, enchantment, or creature with flying" — CR 608.2b target
    /// filter. The flying clause binds ONLY to the creature branch (CR 702.9 —
    /// an artifact or enchantment qualifies regardless of flying; a creature
    /// qualifies only if it has Flying). An artifact-creature or
    /// enchantment-creature qualifies via its artifact/enchantment type
    /// (CR 700.4 — a permanent may have multiple card types) even without flying.
    /// </summary>
    private static bool IsLegalDestroyTarget(ICard card) =>
        card.HasType(CardType.Artifact)
        || card.HasType(CardType.Enchantment)
        || (card.HasType(CardType.Creature)
            && card is Permanent perm
            && CombatAbilities.HasFlying(perm));
}
