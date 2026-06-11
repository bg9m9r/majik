using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Atarka's Command (Dragons of Tarkir, {R}{G}).
///
/// Instant. Oracle text:
///   "Choose two —
///     • Your opponents can't gain life this turn.
///     • Atarka's Command deals 3 damage to each opponent.
///     • You may put a land card from your hand onto the battlefield.
///     • Creatures you control get +1/+1 and gain reach until end of turn."
///
/// CR 700.2e — modal spells choose N distinct modes. This factory is the
/// same shape as <see cref="KolaghansCommandFactory"/> — four printed modes,
/// pick two — with the Atarka modes swapped in. The card shape itself loads
/// from the embedded JSON definition (<c>atarkas-command.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/>, mirroring
/// <see cref="ZealousPersecutionFactory.Create"/>.
///
/// Targets: every printed mode here is targetless (CR 114.1) — "each
/// opponent", "creatures you control", and the optional land-put all act on
/// snapshots/buses rather than chosen targets — so <c>TargetRequests</c> is
/// empty and the relevant game surfaces (<see cref="Player.Replacements"/>,
/// each opponent's life, the caster's battlefield + hand) are supplied to
/// <see cref="BuildDefinition"/> at cast time.
///
/// v1 defaults to modes 1+3 (3 damage to each opponent + creatures you
/// control get +1/+1 and reach) when no explicit mode selectors are given —
/// the aggressive Modern "burn + pump" line.
/// </summary>
[CardName("Atarka's Command")]
public static class AtarkasCommandFactory
{
    public const string CardName = "Atarka's Command";
    public const string Slug = "atarkas-command";

    public const int ModeNoLifeGain   = 0;
    public const int ModeDamageEach    = 1;
    public const int ModePutLand       = 2;
    public const int ModePumpAndReach  = 3;

    /// <summary>Number of modes to pick on cast (CR 700.2e — "Choose two —").</summary>
    public const int PickCount = 2;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 4;

    /// <summary>3 damage to each opponent (mode 1).</summary>
    public const int Damage = 3;

    /// <summary>+1/+1 magnitude on each creature you control (mode 3).</summary>
    public const int Pump = 1;

    /// <summary>Keyword granted by mode 3 (CR 702.17).</summary>
    public const string ReachKeyword = "Reach";

    /// <summary>
    /// Build the card shape from the embedded JSON definition. Behaviour is
    /// supplied at cast time via <see cref="BuildDefinition"/> — same
    /// data-backed posture as <see cref="ZealousPersecutionFactory.Create"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>The printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Your opponents can't gain life this turn.",
        "Atarka's Command deals 3 damage to each opponent.",
        "You may put a land card from your hand onto the battlefield.",
        "Creatures you control get +1/+1 and gain reach until end of turn.",
    };

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Atarka's Command.
    /// </summary>
    /// <param name="caster">The casting player — "you" in the oracle text.</param>
    /// <param name="allPlayers">All players in the game. Opponents (every
    /// player other than <paramref name="caster"/>, CR 102.1) are the targets
    /// of modes 0 (no lifegain) and 1 (3 damage each).</param>
    /// <param name="zoneService">Optional live <see cref="ZoneService"/> so a
    /// land put onto the battlefield (mode 2) routes through zone movement and
    /// fires ETB triggers (CR 603.6a). Null → raw zone manipulation fallback.</param>
    /// <param name="chosenModes">Defaults to <c>new[]{1,3}</c> when null.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        IReadOnlyList<Player>? allPlayers,
        ZoneService? zoneService = null,
        IReadOnlyList<int>? chosenModes = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var defaultModes = chosenModes ?? new[] { ModeDamageEach, ModePumpAndReach };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            // CR 114.1 — every Atarka mode is targetless; no target requests.
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                BotIntent.None,     // deny opponents' lifegain (no clean classifier)
                BotIntent.Burn,     // 3 damage to each opponent
                BotIntent.Ramp,     // put a land
                BotIntent.Buff,     // creatures you control +1/+1 and reach
            },
            EffectFactory: p =>
            {
                // Prefer ModeIndexes; fall back to legacy scalar ModeIndex;
                // finally fall back to defaultModes.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : defaultModes);

                var players = p.AllPlayers ?? allPlayers;

                var effects = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;      // CR 700.2e — no duplicates
                    if (seen.Count > PickCount) break; // honour printed pick count

                    switch (raw)
                    {
                        case ModeNoLifeGain:
                            effects.Add(BuildNoLifeGainEffect(caster, players));
                            break;
                        case ModeDamageEach:
                            effects.Add(BuildDamageEachOpponentEffect(caster, players));
                            break;
                        case ModePutLand:
                            effects.Add(BuildPutLandEffect(caster, zoneService));
                            break;
                        case ModePumpAndReach:
                            effects.Add(BuildPumpAndReachEffect(caster));
                            break;
                    }
                }
                return effects;
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0 — Your opponents can't gain life this turn (CR 614 / CR 119.6)
    // -----------------------------------------------------------------------

    private static IEffect BuildNoLifeGainEffect(
        Player caster,
        IReadOnlyList<Player>? allPlayers) =>
        new Effect("Atarka's Command — your opponents can't gain life this turn", () =>
        {
            if (allPlayers == null) return;

            // CR 102.1 — "your opponents" = every player other than the
            // caster. Each player owns its own ReplacementBus, so registering
            // the EOT-expirable lifegain blocker only on opponents' buses
            // scopes the static to opponents (not the caster). Mirrors
            // SkullcrackFactory's "players can't gain life this turn", but
            // opponent-scoped. Buses absent (shape tests) → silent no-op.
            foreach (var opp in allPlayers)
            {
                if (ReferenceEquals(opp, caster)) continue;
                opp.Replacements?.Register(new SkullcrackFactory.SkullcrackLifeGainBlocker());
            }
        });

    // -----------------------------------------------------------------------
    // Mode 1 — Atarka's Command deals 3 damage to each opponent (CR 800.4)
    // -----------------------------------------------------------------------

    private static IEffect BuildDamageEachOpponentEffect(
        Player caster,
        IReadOnlyList<Player>? allPlayers) =>
        new Effect($"Atarka's Command — deal {Damage} damage to each opponent", () =>
        {
            if (allPlayers == null) return;

            // CR 800.4 — "each opponent" = every player who is not the caster.
            // CR 119.3 — damage to a player reduces their life total;
            // Fx.DealDamage routes Player → Player.LoseLife (CR 119.8).
            foreach (var opp in allPlayers)
            {
                if (ReferenceEquals(opp, caster)) continue;
                Fx.DealDamage(opp, Damage);
            }
        });

    // -----------------------------------------------------------------------
    // Mode 2 — You may put a land card from your hand onto the battlefield
    // -----------------------------------------------------------------------

    private static IEffect BuildPutLandEffect(
        Player caster,
        ZoneService? zoneService) =>
        new Effect("Atarka's Command — you may put a land from hand onto the battlefield", async ctx =>
        {
            // CR 113.6c — putting a land onto the battlefield via an effect is
            // NOT a land play; it bypasses the per-turn land-drop cap
            // (CR 305.2). v1 auto-accepts the "you may" and takes the first
            // land in hand (shared no-agent posture with Growth Spiral /
            // Sakura-Tribe Scout). No land in hand → clean no-op.
            var land = caster.Zones.Hand.GetCards()
                .FirstOrDefault(c => c.HasType(CardType.Land));
            if (land == null) return;

            // CR 603.6a — prefer ZoneService.MoveCardAsync so ETB triggers /
            // replacements on the played land fire (bounce-land ETB bounce,
            // Lotus Cobra landfall) AND a prompting ETB replacement (shock-land
            // "pay 2 life?") awaits the controller's agent off the
            // ResolutionContext instead of auto-deciding on the sync path.
            // Fall back to the registry, then raw zone manipulation (test path).
            var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(caster);
            if (effectiveZones != null)
            {
                await effectiveZones.MoveCardAsync(
                    land, ZoneType.Hand, ZoneType.Battlefield, ctx, caster)
                    .ConfigureAwait(false);
            }
            else
            {
                caster.Zones.Hand.RemoveCard(land);
                caster.Zones.Battlefield.AddCard(land);
                land.SetZone(ZoneType.Battlefield);
                land.SetController(caster);
            }
        });

    // -----------------------------------------------------------------------
    // Mode 3 — Creatures you control get +1/+1 and gain reach until EOT
    // -----------------------------------------------------------------------

    private static IEffect BuildPumpAndReachEffect(Player caster) =>
        new Effect(
            $"Atarka's Command — creatures you control get +{Pump}/+{Pump} and gain reach until end of turn",
            () =>
            {
                // CR 109.5 / CR 700 — "creatures you control" is a one-shot
                // snapshot taken at resolution (CR 608.2); creatures that enter
                // afterward do NOT pick up either rider (same posture as
                // ZealousPersecutionFactory). CR 613.1c Layer 7c +P/+T and
                // CR 613.1d Layer 6 keyword grant, both expiring at the cleanup
                // step (CR 514.2).
                var creatures = caster.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .ToList();

                foreach (var creature in creatures)
                {
                    if (creature.Zone != ZoneType.Battlefield) continue;

                    // Shape-only safety — without a live ContinuousEffectsService
                    // wired onto the creature the riders silently no-op rather
                    // than NRE'ing. Same posture as ZealousPersecutionFactory.
                    if (creature.ActiveEffects == null) continue;

                    creature.ActiveEffects.Register(
                        new PumpUntilEndOfTurnEffect(creature, Pump, Pump));
                    creature.ActiveEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(creature, ReachKeyword));
                }
            });
}
