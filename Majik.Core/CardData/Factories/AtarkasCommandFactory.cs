using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Atarka's Command (Dragons of Tarkir, {R}{G}).
///
/// Instant. Oracle text (per Scryfall, canonical):
///   "Choose two —
///     • Your opponents can't gain life this turn.
///     • Atarka's Command deals 3 damage to each opponent.
///     • You may put a land card from your hand onto the battlefield.
///     • Creatures you control get +1/+1 and gain reach until end of turn."
///
/// CR 700.2e — modal spells choose N distinct modes. Same shape as
/// <see cref="KolaghansCommandFactory"/> — four modes, pick 2.
///
/// None of the four modes is targeted (mode 0 affects "your opponents" as
/// a set, mode 1 distributes damage to "each opponent", modes 2 and 3 are
/// self-scoped). The factory therefore declares no <see cref="TargetRequest"/>s
/// and the EffectFactory enumerates opponents from
/// <see cref="ChosenSpellParams.AllPlayers"/> at resolve time.
///
/// As with the rest of the Commands cycle, the cast-flow only collects a
/// single <c>ModeIndex</c> today (see <see cref="SpellCastFlow"/>);
/// callers that want full multi-pick wire <c>ModeIndexes</c> directly into
/// <see cref="ChosenSpellParams"/>. Default modes if none provided: 1 + 3
/// (the BR-aggro "burn + pump-with-reach" line — the deck-defining
/// post-combat finisher pattern).
/// </summary>
[CardName("Atarka's Command")]
public static class AtarkasCommandFactory
{
    public const string CardName = "Atarka's Command";
    public const string PrintedManaCost = "{R}{G}";

    public const int ModeNoLifeGain = 0;
    public const int ModeDealDamage = 1;
    public const int ModePlayLand   = 2;
    public const int ModePumpAll    = 3;

    /// <summary>Number of modes to pick on cast (CR 700.2e — "Choose two —").</summary>
    public const int PickCount = 2;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 4;

    public const int DamageAmount = 3;
    public const int PumpPower = 1;
    public const int PumpToughness = 1;

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>The printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Your opponents can't gain life this turn.",
        "Atarka's Command deals 3 damage to each opponent.",
        "You may put a land card from your hand onto the battlefield.",
        "Creatures you control get +1/+1 and gain reach until end of turn.",
    };

    /// <summary>Granted keyword for mode 3 — CR 702.17 Reach.</summary>
    public const string GrantedReach = "Reach";

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Atarka's Command is
    /// cast. The caller resolves targets through
    /// <paramref name="targetResolver"/> and supplies the optional
    /// <see cref="ReplacementBus"/> for mode 0's life-gain rider and the
    /// optional <see cref="ZoneService"/> for mode 2's hand → battlefield
    /// land move (so ETB triggers / replacements on the played land fire —
    /// matches <see cref="SakuraTribeScoutFactory"/>'s posture).
    /// </summary>
    /// <param name="caster">The casting player — read for mode 2's "your
    /// hand" + mode 3's "creatures you control" scoping; opponents are
    /// the rest of <paramref name="allPlayers"/>.</param>
    /// <param name="allPlayers">Full table — required so modes 0 and 1
    /// can enumerate opponents at resolve time. Null = those modes
    /// gracefully degrade to no-op (matches Kolaghan's Command's
    /// AllPlayers-optional posture).</param>
    /// <param name="replacements">Replacement bus for mode 0's EOT-expirable
    /// life-gain lockout (CR 614). Null = mode 0 silently no-ops.</param>
    /// <param name="zoneService">Zone service for mode 2's hand →
    /// battlefield move. Null = falls back to raw zone manipulation
    /// (no <see cref="CardMovedEvent"/> emission; matches Sakura-Tribe
    /// Scout's shape-only path).</param>
    /// <param name="chosenModes">Defaults to <c>new[]{1,3}</c> (burn +
    /// pump — the BR-aggro Commands line) when null.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        IReadOnlyList<Player>? allPlayers = null,
        ReplacementBus? replacements = null,
        ZoneService? zoneService = null,
        IReadOnlyList<int>? chosenModes = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        // No mode is targeted (oracle uses "your opponents" / "each
        // opponent" / self-scoped). Modes 0 and 1 enumerate opponents
        // from p.AllPlayers (set by SpellCastFlow) falling back to the
        // factory-supplied allPlayers list when present.
        var defaultModes = chosenModes ?? new[] { ModeDealDamage, ModePumpAll };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                BotIntent.Burn,        // mode 0 — life-gain lockout on opponents
                BotIntent.Burn,        // mode 1 — 3 damage to each opponent
                BotIntent.Ramp,        // mode 2 — extra land drop
                BotIntent.Buff,        // mode 3 — mass +1/+1 + Reach
            },
            EffectFactory: p =>
            {
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : defaultModes);

                // Opponents pool: prefer p.AllPlayers (set by SpellCastFlow),
                // fall back to the factory's allPlayers param. Self-filtered.
                var pool = p.AllPlayers ?? allPlayers;

                var effects = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;       // CR 700.2e — no duplicates
                    if (seen.Count > PickCount) break;  // honour printed pick count

                    switch (raw)
                    {
                        case ModeNoLifeGain:
                            effects.Add(BuildNoLifeGainEffect(caster, pool, replacements));
                            break;
                        case ModeDealDamage:
                            effects.Add(BuildDealDamageEffect(caster, pool));
                            break;
                        case ModePlayLand:
                            effects.Add(BuildPlayLandEffect(caster, zoneService));
                            break;
                        case ModePumpAll:
                            effects.Add(BuildPumpAllEffect(caster));
                            break;
                    }
                }
                return effects;
            });
    }

    // -----------------------------------------------------------------------
    // Mode bodies
    // -----------------------------------------------------------------------

    private static IEffect BuildNoLifeGainEffect(
        Player caster,
        IReadOnlyList<Player>? pool,
        ReplacementBus? replacements) =>
        new Effect($"{CardName}: your opponents can't gain life this turn", () =>
        {
            if (replacements == null) return;
            if (pool == null) return;

            // CR 614 / 119.6 — register one EOT-expirable life-gain
            // blocker per opponent (each scoped to that player's intents).
            // Shape lifted from SkullcrackFactory; dropped at cleanup via
            // IEndOfTurnExpirable.
            foreach (var opp in pool)
            {
                if (ReferenceEquals(opp, caster)) continue;
                var captured = opp;
                replacements.Register(new EotLambdaReplacement<LifeGainIntent>(
                    applies: (intent, _) => ReferenceEquals(intent.Target, captured),
                    replace: (intent, _) => intent with { Amount = 0 },
                    tag: $"{CardName}:NoGain:{captured.Name}"));
            }
        });

    private static IEffect BuildDealDamageEffect(
        Player caster,
        IReadOnlyList<Player>? pool) =>
        new Effect($"{CardName}: deal {DamageAmount} damage to each opponent", () =>
        {
            if (pool == null) return;
            foreach (var opp in pool)
            {
                if (ReferenceEquals(opp, caster)) continue;
                if (opp.HasLost) continue;
                SearingBlazeFactory.DealDamageWithPlaneswalker(opp, DamageAmount);
            }
        });

    private static IEffect BuildPlayLandEffect(
        Player caster,
        ZoneService? zoneService) =>
        new Effect($"{CardName}: you may put a land card from your hand onto the battlefield", () =>
        {
            // Lifted verbatim from SakuraTribeScoutFactory's "{T}: You may
            // put a land card from your hand onto the battlefield." — same
            // CR 305.9 / 113.6c bypass (not a land drop). v1 deterministic
            // pick: first land in hand. Agent-prompted picks happen at
            // resolution via ChooseFromHandAsync when an agent is wired —
            // omitted here because Atarka's Command's resolve path doesn't
            // thread an agent through ChosenSpellParams yet (matches
            // Cryptic / Kolaghan's Command — auto-resolve at this layer).
            var candidates = caster.Zones.Hand.GetCards()
                .Where(c => c.HasType(CardType.Land))
                .ToList();
            if (candidates.Count == 0) return; // No lands → may no-op.

            var land = candidates[0];

            if (zoneService != null)
            {
                zoneService.MoveCard(land, ZoneType.Hand, ZoneType.Battlefield, caster);
            }
            else
            {
                caster.Zones.Hand.RemoveCard(land);
                caster.Zones.Battlefield.AddCard(land);
                land.SetZone(ZoneType.Battlefield);
                land.SetController(caster);
            }
        });

    private static IEffect BuildPumpAllEffect(Player caster) =>
        new Effect(
            $"{CardName}: creatures you control get +{PumpPower}/+{PumpToughness} and gain reach until end of turn",
            () =>
            {
                // CR 613.1c Layer 7c — per-target PumpUntilEndOfTurnEffect on
                // each controlled creature's ActiveEffects. CR 613.1c Layer 6
                // — Reach keyword grant EOT. Without a per-creature
                // ContinuousEffectsService wired both grants silently no-op
                // (matches Slickshot Show-Off's posture).
                foreach (var card in caster.Zones.Battlefield.GetCards())
                {
                    if (card is not Creature creature) continue;
                    if (creature.ActiveEffects == null) continue;
                    creature.ActiveEffects.Register(
                        new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));
                    creature.ActiveEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(creature, GrantedReach));
                }
            });
}
