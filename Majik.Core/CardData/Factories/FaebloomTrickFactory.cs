using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Faebloom Trick (Bloomburrow, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Create two 1/1 blue Faerie creature tokens with flying. When you do,
///    tap target creature an opponent controls."
///
/// ## Shape source
/// Card identity (name, {1}{U}, Instant) is loaded from
/// <c>Majik.Core/CardData/Cards/faebloom-trick.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="ChillingGraspFactory"/>. The token-creation body + tap rider are
/// attached in code below (the JSON ability schema does not yet express token
/// creation or a tap effect).
///
/// ## Implemented (v1)
/// - Instant {1}{U} (blue, mana value 2 per CR 202.2c / 202.3).
/// - A single 1..1 "target creature an opponent controls" request
///   (BotIntent.Removal — tapping a blocker/attacker is a tempo play), mirroring
///   <see cref="IntoTheFloodMawFactory"/>'s opponent-controlled single-target
///   shape.
/// - Resolution body (CR 608.2 ordering, top-to-bottom):
///     1. Create two 1/1 blue Faerie creature tokens with flying under the
///        caster (CR 111 / 111.4 — one token per "create"; Flying per CR 702.9).
///     2. Tap the chosen target (CR 701.20) — but only if it is still a Creature
///        on the battlefield that an opponent controls (CR 608.2b / CR 109.1).
///
/// ## Reflexive-trigger simplification (CR 603.2e)
/// The printed text places the tap inside a reflexive trigger ("When you do,
/// tap target creature an opponent controls"), which would normally put the tap
/// on the stack as a separate object that targets after the tokens are created.
/// Because token creation is unconditional — the spell ALWAYS makes the two
/// Faeries — the reflexive trigger always fires, so v1 collapses it into the
/// spell's own resolution: the single target is chosen at cast time
/// (CR 601.2c) and tapped during resolution, immediately after the tokens are
/// created. This is gameplay-equivalent whenever the tap target is mandatory
/// (an opponent controls a creature), and matches the cast-time single-target
/// posture already shipped for <see cref="IntoTheFloodMawFactory"/>. A future
/// pass can split the tap onto its own reflexive trigger if a card cares about
/// the token-ETB happening before the tap targets.
/// </summary>
[CardName("Faebloom Trick")]
public static class FaebloomTrickFactory
{
    public const string CardName = "Faebloom Trick";
    public const string Slug = "faebloom-trick";

    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Faebloom Trick as an Instant from the embedded JSON def, with
    /// owner / controller wired. Card shape only — the resolve closure is
    /// produced by <see cref="BuildDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Instant)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Faebloom Trick.
    ///
    /// One 1..1 "target creature an opponent controls" request (CR 601.2c). On
    /// resolution the two flying Faerie tokens are created under
    /// <paramref name="caster"/> (CR 111 / 111.4) and the chosen target — if it
    /// is still a Creature on the battlefield controlled by an opponent
    /// (CR 608.2b / CR 109.1) — is tapped (CR 701.20).
    /// </summary>
    /// <param name="caster">The player casting Faebloom Trick. The tokens are
    /// created under this player, and the "an opponent controls" gate at
    /// resolution is checked against it (CR 109.1). May be null in shape tests,
    /// in which case the opponent-control gate is skipped.</param>
    /// <param name="zones">Optional ZoneService for replacement-bus-aware token
    /// creation. When null, raw zone manipulation is used (shape / dispatcher
    /// path).</param>
    public static SpellDefinition BuildDefinition(
        Player? caster = null,
        ZoneService? zones = null) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: create two 1/1 blue flying Faerie tokens, then tap target creature an opponent controls",
                        () =>
                        {
                            CreateFaeries(caster, zones);
                            TapTarget(raw, caster);
                        }),
                };
            });

    /// <summary>
    /// CR 111 / 111.4 — create two 1/1 blue Faerie creature tokens with flying
    /// under <paramref name="caster"/>. Routed through
    /// <see cref="TokenFactory.CreateOnBattlefield"/> so CardMovedEvent fires
    /// (token-tribal / ETB triggers see the Faeries). Flying (CR 702.9) is
    /// attached via the TokenSpec keyword list — same shape as
    /// <see cref="BitterblossomFactory"/>'s Faerie token.
    /// </summary>
    private static void CreateFaeries(Player? caster, ZoneService? zones)
    {
        if (caster == null) return;

        var spec = new TokenFactory.TokenSpec(
            Name: "Faerie",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Faerie },
            Keywords: new[] { "Flying" },
            Colors: new[] { ManaColor.Blue });

        // CR 111 — one token per "create"; "two" = two separate create events.
        TokenFactory.CreateOnBattlefield(spec, caster, zones);
        TokenFactory.CreateOnBattlefield(spec, caster, zones);
    }

    /// <summary>
    /// CR 701.20 — tap the chosen target, gated at resolution by CR 608.2b
    /// (still a Creature on the battlefield) and CR 109.1 (an opponent — any
    /// player other than <paramref name="caster"/> — controls it). When
    /// <paramref name="caster"/> is null the opponent gate is skipped (shape
    /// tests). Already-tapped is a clean no-op (Permanent.Tap is guarded).
    /// </summary>
    private static void TapTarget(object raw, Player? caster)
    {
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        var controller = target.Controller ?? target.Owner;

        // CR 109.1 — "an opponent controls". Self-targeting is illegal at
        // resolution → the tap does nothing (tokens were still created).
        if (caster != null && ReferenceEquals(controller, caster)) return;

        if (!target.IsTapped) target.Tap();
    }
}
