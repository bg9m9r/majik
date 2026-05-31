using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Force of Rage (Modern Horizons, {2}{R}{R}).
///
/// Sorcery. Oracle text:
///   "If it's not your turn, you may exile a red card from your hand rather
///    than pay this spell's mana cost.
///    Create three 3/1 red Elemental creature tokens with trample and haste.
///    Sacrifice them at the beginning of the next end step."
///
/// ## Implemented (v1)
/// - Sorcery card shape ({2}{R}{R}, Red) — built via the fluent
///   <see cref="CardDef"/> DSL.
/// - Pitch alternative cost (<see cref="Majik.Core.Costs.PitchAlternativeCost"/>,
///   <c>RequiredColor = Red</c>, <c>LifeCost = 0</c>) — same primitive +
///   not-your-turn timing gate as Force of Despair / Force of Negation.
/// - Bot probe wired through <see cref="PitchAltCostProbe.DefaultLookup"/>
///   (Red / 0-life).
/// - Resolve effect (<see cref="BuildSpellDefinition"/>):
///   1. Creates three 3/1 red Elemental tokens via
///      <see cref="TokenFactory.CreateOnBattlefield"/> on the caster's
///      battlefield, each carrying the Trample (CR 702.19) and Haste
///      (CR 702.10) keyword markers. <see cref="ZoneService"/> threads the
///      ETB <see cref="CardMovedEvent"/> when supplied so soul-warden /
///      Impact-Tremors-style triggers fire correctly.
///   2. Registers a single one-shot
///      <see cref="DelayedTriggeredAbility"/> (CR 603.7) on the supplied
///      <see cref="TriggerManager"/> that sacrifices every still-alive
///      token at the start of the next end step (CR 500.4 / CR 701.16).
///      The trigger fence-checks <c>e.Timestamp &gt; resolvedAt</c> so the
///      current end step (if any) doesn't trip it — same activation-time
///      fence Through the Breach uses.
///
/// ## Deferred (v1 gaps)
/// - <b>Shape-only resolve</b>: when no <see cref="ZoneService"/> is wired
///   the tokens still spawn on the battlefield via raw zone manipulation
///   (<see cref="TokenFactory.CreateOnBattlefield"/>'s fallback), but no
///   <see cref="CardMovedEvent"/> publishes — same posture as every
///   other token-creating factory in dispatcher-only mode.
/// - <b>Shape-only delayed sac</b>: when no <see cref="TriggerManager"/>
///   is wired the tokens spawn but the EOT sacrifice never fires — the
///   tokens persist until SBAs reap them (CR 704.5d removes tokens that
///   leave the battlefield, but doesn't remove ones on it). Production
///   callers must thread the live trigger manager.
/// </summary>
[CardName("Force of Rage")]
public static class ForceOfRageFactory
{
    public const string CardName = "Force of Rage";
    public const string PrintedManaCost = "{2}{R}{R}";

    /// <summary>Token P/T (CR 111.4). 3/1 red Elemental, trample + haste.</summary>
    public const int TokenCount = 3;
    public const int TokenPower = 3;
    public const int TokenToughness = 1;
    public const string TokenName = "Elemental";

    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Creates three
    /// 3/1 red Elemental tokens with Trample + Haste and registers a
    /// delayed end-step sacrifice trigger for all of them.
    /// </summary>
    /// <param name="caster">The spell's controller — token controller +
    /// delayed-trigger controller. CR 111.2 — the token's controller is
    /// the controller of the effect that created it.</param>
    /// <param name="zoneService">Optional. Threads <see cref="CardMovedEvent"/>
    /// on token ETB. Shape-only callers pass null and tokens still spawn
    /// via raw zone manipulation.</param>
    /// <param name="triggers">Optional. When supplied, the EOT sacrifice
    /// trigger is registered. Shape-only callers can pass null — the
    /// tokens spawn but won't be sacrificed automatically.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zoneService = null,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                Fx.Inline(
                    $"{CardName} — create three 3/1 red Elemental tokens (trample + haste), sac next end step",
                    () => Resolve(caster, zoneService, triggers)),
            });
    }

    /// <summary>
    /// Shared resolve helper. Spawns the three tokens, then (when triggers
    /// is supplied) registers ONE delayed end-step trigger that sacrifices
    /// every spawned token still on the battlefield (CR 701.16).
    /// </summary>
    private static void Resolve(
        Player caster,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        // CR 111.4 — spec stamps the token's printed characteristics.
        // Red colour identity (CR 105 / CR 111.4) is the printed text;
        // we pass [Red] explicitly so CardColors.GetColors reports the
        // correct set for protection / lord interactions.
        var spec = new TokenFactory.TokenSpec(
            Name: TokenName,
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Elemental },
            Keywords: new[] { "Trample", "Haste" },
            Colors: new[] { ManaColor.Red });

        var spawned = new List<Creature>(TokenCount);
        for (int i = 0; i < TokenCount; i++)
        {
            var tok = TokenFactory.CreateOnBattlefield(spec, caster, zoneService);
            // Haste lifts summoning sickness for attack-declaration this
            // turn (CR 702.10b). TokenFactory defaults HasSummoningSickness
            // = true; the printed "with haste" requires we clear it.
            tok.HasSummoningSickness = false;
            spawned.Add(tok);
        }

        if (triggers == null) return;

        // CR 603.7 — one-shot delayed triggered ability registered on the
        // spell's controller. Fires on the first StepStartedEvent(End)
        // strictly after this resolve (activation-time fence mirrors
        // Through the Breach / Mishra's Bauble / Wrenn's Resolve).
        // Sacrifices every token in `spawned` still on the battlefield
        // (CR 701.16 — controller's battlefield → owner's graveyard, then
        // SBA 704.5d removes the token).
        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var sacEffect = new Effect(
            $"{CardName} — sacrifice the three Elemental tokens at next end step",
            () =>
            {
                foreach (var tok in spawned)
                {
                    if (tok.Zone != ZoneType.Battlefield) continue;
                    var battlefield = tok.Controller?.Zones.Battlefield;
                    if (battlefield == null) continue;
                    if (!battlefield.GetCards().Contains(tok)) continue;

                    var bfPlayer = tok.Controller!;
                    var graveyardOwner = tok.Owner ?? caster;
                    if (zoneService != null)
                    {
                        zoneService.MoveCard(
                            tok, ZoneType.Battlefield, ZoneType.Graveyard, bfPlayer);
                    }
                    else
                    {
                        bfPlayer.Zones.Battlefield.RemoveCard(tok);
                        graveyardOwner.Zones.Graveyard.AddCard(tok);
                        tok.SetZone(ZoneType.Graveyard);
                    }
                    // SBA 704.5d — tokens cease to exist in any zone other
                    // than the battlefield. Drop from graveyard to mirror
                    // the live SBA pass (tests that don't run SBAs still
                    // observe the correct end-state).
                    graveyardOwner.Zones.Graveyard.RemoveCard(tok);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: caster,
            controller: caster,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { sacEffect });

        triggers.RegisterDelayed(delayed);
    }
}
