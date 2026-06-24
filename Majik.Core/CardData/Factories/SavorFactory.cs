using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Savor (Bloomburrow, {1}{B}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Target creature gets -2/-2 until end of turn. Create a Food token.
///    (It's an artifact with "{2}, {T}, Sacrifice this token: You gain 3
///    life.")"
///
/// Savor is the -2/-2 cousin of <see cref="DisfigureFactory"/> (the same
/// "target creature gets -2/-2 until end of turn" resolve) bolted to the
/// Food-token mint borrowed from <see cref="BakeIntoAPieFactory"/> /
/// <see cref="TokenFactory.CreateFood"/>. Same two-independent-sentences
/// shape as Bake into a Pie: the Food half is NOT gated on the -2/-2 half
/// resolving against a legal target.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {1}{B}. The base shape (name /
///   Instant type / {1}{B} cost) is materialised from the embedded JSON
///   definition (<c>savor.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="BakeIntoAPieFactory"/> (the JSON <c>SpellDefinition</c>
///   schema does not yet express a pump-then-mint-token resolve, so the
///   resolve behaviour is layered on here via <see cref="BuildDefinition"/>).
/// - <b>Target creature gets -2/-2 until end of turn</b> — a single 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolve: re-checks the
///   target is still a Creature on the Battlefield (CR 608.2b illegal-target
///   gate), then registers a <see cref="PumpUntilEndOfTurnEffect"/>(-2, -2)
///   on the target's <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires
///   at EOT). Same pattern as <see cref="DisfigureFactory"/>. When
///   ActiveEffects is null (shape-only tests without a live
///   ContinuousEffectsService) the registration is a no-op.
/// - <b>Create a Food token</b> — unconditionally mints one Food token
///   (CR 111.10) for the caster via <see cref="TokenFactory.CreateFood"/>.
///   The Food half is NOT gated on the -2/-2 half succeeding: the printed
///   wording is two independent sentences, so the token is created even if
///   the pump fizzles to an illegal target.
/// </summary>
[CardName("Savor")]
public static class SavorFactory
{
    public const string CardName = "Savor";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "savor";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {1}{B}) from the
    /// embedded JSON definition. Resolve behaviour (target creature gets
    /// -2/-2 until end of turn + create a Food token) is built on demand via
    /// <see cref="BuildDefinition"/>, mirroring <see cref="BakeIntoAPieFactory"/>.
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
    /// Build the "target creature gets -2/-2 until end of turn; create a Food
    /// token" <see cref="SpellDefinition"/>. On resolve: validates the target
    /// is still a Creature on the Battlefield (CR 608.2b — illegal target →
    /// pump half is a no-op); when valid, registers a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(-2, -2) on the target's
    /// <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at EOT). Then
    /// mints one Food token for the caster (CR 111.10) — unconditionally,
    /// since the printed wording is two independent sentences.
    /// </summary>
    /// <param name="caster">Controller of Savor — also the player who receives
    /// the Food token on resolve (CR 111.10 — a token is created under the
    /// control of the player the spell instructs).</param>
    /// <param name="zoneService">Optional ZoneService so the minted Food
    /// token's battlefield ETB publishes <c>CardMovedEvent</c>; null in shape
    /// tests.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

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
                    // battlefield. Removal intent in the bot's ranker pushes
                    // the opponent's biggest small threat up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: target creature gets -2/-2 until end of turn; create a Food token",
                        () =>
                        {
                            ResolvePump(raw);

                            // CR 111.10 — create one Food token for the caster.
                            // Unconditional: the printed second sentence is not
                            // gated on the -2/-2 half resolving.
                            TokenFactory.CreateFood(caster, zoneService);
                        }),
                };
            });
    }

    /// <summary>
    /// Resolve "Target creature gets -2/-2 until end of turn." CR 608.2b —
    /// validates the target is still a Creature on the Battlefield (clean
    /// no-op otherwise), then registers a -2/-2 EOT-scoped Layer 7c effect on
    /// the target's <see cref="Creature.ActiveEffects"/> (CR 514.2). When
    /// ActiveEffects is null (shape tests without a live
    /// ContinuousEffectsService) the registration is a no-op.
    /// </summary>
    private static void ResolvePump(object raw)
    {
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;
        target.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(target, -2, -2));
    }
}
