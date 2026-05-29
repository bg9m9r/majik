using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kozilek's Command (Modern Horizons 3, {X}{C}{C}).
///
/// Kindred Instant — Eldrazi. Oracle text:
///   "Choose two —
///     • Target player creates X 0/1 colorless Eldrazi Spawn creature tokens
///       with "Sacrifice this token: Add {C}."
///     • Target player scries X, then draws a card.
///     • Exile target creature with mana value X or less.
///     • Exile up to X target cards from graveyards."
///
/// CR 700.2e — modal spells choose N distinct modes ("Choose two —", PickCount = 2).
/// Same overall shape as <see cref="KolaghansCommandFactory"/> (four modes, pick 2)
/// combined with <see cref="SpellDefinition.HasVariableX"/> = true (the {X} cost,
/// modelled like <see cref="ShatterskullSmashingFactory"/>).
///
/// Targets are addressed by index into <see cref="ChosenSpellParams.Targets"/>:
///   Targets[0] — target player (mode 0 — create spawn).
///   Targets[1] — target player (mode 1 — scry/draw).
///   Targets[2] — target creature (mode 2 — exile, mv X or less).
///   Targets[3] — up to X target cards in graveyards (mode 3 — exile).
///
/// ## v1 notes / deferrals
/// - "Eldrazi Spawn" tokens are produced by <see cref="Fx.CreateEldraziSpawn"/>;
///   the spawn's "Sacrifice this token: Add {C}." carries the documented
///   v1 sac-cost deferral already noted on <see cref="Tokens.TokenFactory.CreateEldraziSpawn"/>.
/// - Scry partition is agent-driven when an <see cref="IPlayerAgent"/> is
///   registered, else defaults to "all to bottom" (matches
///   <see cref="ReadTheBonesFactory"/> / <see cref="SerumVisionsFactory"/>).
/// - Mode 2's mana-value gate is checked at resolution (CR 202.3 / 608.2b),
///   mirroring <see cref="AbruptDecayFactory"/>.
/// </summary>
[CardName("Kozilek's Command")]
public static class KozileksCommandFactory
{
    public const string CardName = "Kozilek's Command";
    public const string PrintedManaCost = "{X}{C}{C}";

    public const int ModeCreateSpawn   = 0;
    public const int ModeScryDraw       = 1;
    public const int ModeExileCreature  = 2;
    public const int ModeExileGraveyard = 3;

    /// <summary>Number of modes to pick on cast (CR 700.2e — "Choose two —").</summary>
    public const int PickCount = 2;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 4;

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Eldrazi });

        // CR 308 — Kindred (legacy "Tribal") card type, layered onto the
        // Instant so the Eldrazi subtype is grammatically grounded (CR 308.2).
        // Idempotent — AddCardType skips duplicates. Same pattern as
        // AllIsDustFactory.
        card.AddCardType(CardType.Tribal);

        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>The printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Target player creates X 0/1 colorless Eldrazi Spawn creature tokens with \"Sacrifice this token: Add {C}.\"",
        "Target player scries X, then draws a card.",
        "Exile target creature with mana value X or less.",
        "Exile up to X target cards from graveyards.",
    };

    /// <summary>
    /// Build the SpellDefinition for Kozilek's Command.
    /// <see cref="SpellDefinition.HasVariableX"/> is true; the cast flow
    /// prompts for X and stores it in <see cref="ChosenSpellParams.X"/>.
    /// </summary>
    /// <param name="caster">The casting player.</param>
    /// <param name="targetResolver">Resolves targets at effect time.</param>
    /// <param name="allPlayers">All players (fallback target for the
    /// player-targeting modes).</param>
    /// <param name="chosenModes">Defaults to <c>new[]{0,1}</c> when null.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        IReadOnlyList<Player>? allPlayers,
        IReadOnlyList<int>? chosenModes = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — a target request is emitted for every mode that takes
        // a target, regardless of whether that mode was chosen at declare
        // time. MinTargets=0 so unchosen modes' slots don't block casting.
        var targetRequests = new[]
        {
            // Mode 0 — target player (creates spawn).
            new TargetRequest("target player", 0, 1, Array.Empty<object>(), BotIntent.Token),
            // Mode 1 — target player (scry/draw).
            new TargetRequest("target player", 0, 1, Array.Empty<object>(), BotIntent.CardAdvantage),
            // Mode 2 — target creature with mana value X or less.
            new TargetRequest("target creature with mana value X or less", 0, 1, Array.Empty<object>(), BotIntent.Removal),
            // Mode 3 — up to X target cards in graveyards. MaxTargets is the
            // engine ceiling; the resolution body honours the "up to X" cap.
            new TargetRequest("up to X target cards from graveyards", 0, int.MaxValue, Array.Empty<object>(), BotIntent.Removal),
        };

        var defaultModes = chosenModes ?? new[] { ModeCreateSpawn, ModeScryDraw };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: true,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Token,
                BotIntent.CardAdvantage,
                BotIntent.Removal,
                BotIntent.Removal,
            },
            EffectFactory: p =>
            {
                var x = p.X ?? 0;

                // Prefer ModeIndexes; fall back to legacy scalar ModeIndex;
                // finally fall back to defaultModes.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : defaultModes);

                var effects = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;      // CR 700.2e — no duplicates
                    if (seen.Count > PickCount) break; // honour printed pick count

                    switch (raw)
                    {
                        case ModeCreateSpawn:
                            effects.Add(BuildCreateSpawnEffect(caster, p, x, targetResolver, allPlayers));
                            break;
                        case ModeScryDraw:
                            effects.Add(BuildScryDrawEffect(caster, p, x, targetResolver, allPlayers));
                            break;
                        case ModeExileCreature:
                            effects.Add(BuildExileCreatureEffect(p, x, targetResolver));
                            break;
                        case ModeExileGraveyard:
                            effects.Add(BuildExileGraveyardEffect(p, x, targetResolver));
                            break;
                    }
                }
                return effects;
            });
    }

    // -----------------------------------------------------------------------
    // Mode bodies
    // -----------------------------------------------------------------------

    private static Player? ResolveTargetPlayer(
        ChosenSpellParams p,
        int modeIndex,
        Func<object, object> resolver,
        IReadOnlyList<Player>? allPlayers,
        Player caster)
    {
        if (p.Targets.Count > modeIndex)
        {
            var slot = p.Targets[modeIndex];
            if (slot.Count > 0 && resolver(slot[0]) is Player tp) return tp;
        }
        // v1 fallback: the caster (player-targeting modes default to "you").
        return allPlayers?.FirstOrDefault() ?? caster;
    }

    private static IEffect BuildCreateSpawnEffect(
        Player caster,
        ChosenSpellParams p,
        int x,
        Func<object, object> resolver,
        IReadOnlyList<Player>? allPlayers) =>
        Fx.Inline(
            $"{CardName}: target player creates {x} Eldrazi Spawn token(s)",
            () =>
            {
                if (x <= 0) return;
                var target = ResolveTargetPlayer(p, ModeCreateSpawn, resolver, allPlayers, caster);
                if (target == null) return;
                // CR 111.10 — each token is a 0/1 colourless Eldrazi Spawn
                // with "Sacrifice this token: Add {C}." (sac-cost deferral
                // documented on TokenFactory.CreateEldraziSpawn).
                for (var i = 0; i < x; i++)
                {
                    Fx.CreateEldraziSpawn(target);
                }
            });

    private static IEffect BuildScryDrawEffect(
        Player caster,
        ChosenSpellParams p,
        int x,
        Func<object, object> resolver,
        IReadOnlyList<Player>? allPlayers) =>
        Fx.Inline(
            $"{CardName}: target player scries {x}, then draws a card",
            () =>
            {
                var target = ResolveTargetPlayer(p, ModeScryDraw, resolver, allPlayers, caster);
                if (target == null) return;

                // CR 701.20 — Scry X. Agent partitions bottom/top when
                // registered; pre-agent default sends peeked cards to the
                // bottom (matches ReadTheBonesFactory).
                if (x > 0)
                {
                    var peeked = ScryAction.Peek(target, x);
                    if (peeked.Count > 0)
                    {
                        var agent = AgentRegistry.Get(target);
                        ScryAction.ScryDecision decision;
                        if (agent != null)
                        {
                            // TODO: drop sync-over-async once IEffect.Execute becomes async.
                            decision = agent.ChooseScryDecisionAsync(null, peeked)
                                .GetAwaiter().GetResult();
                        }
                        else
                        {
                            decision = new ScryAction.ScryDecision(
                                ToBottom: peeked.ToList(),
                                TopOrder: Array.Empty<ICard>());
                        }
                        ScryAction.Apply(target, peeked.Count, decision);
                    }
                }

                // CR 121.1 — draw a card (always, even if X = 0). Routed
                // through Fx.DrawCards so the replacement bus gets a shot and
                // empty-library stamps the SBA loss flag (CR 704.5b).
                Fx.DrawCards(target, 1);
            });

    private static IEffect BuildExileCreatureEffect(
        ChosenSpellParams p,
        int x,
        Func<object, object> resolver) =>
        Fx.Inline(
            $"{CardName}: exile target creature with mana value {x} or less",
            () =>
            {
                if (p.Targets.Count <= ModeExileCreature) return;
                var slot = p.Targets[ModeExileCreature];
                if (slot.Count == 0) return;
                if (resolver(slot[0]) is not Creature creature) return;

                // CR 608.2b — resolution-time legality check.
                if (creature.Zone != ZoneType.Battlefield) return;
                // CR 202.3 — mana value is checked at resolution.
                if (creature.ManaCostValue.TotalValue > x) return;

                // CR 701.20 — exile (not "destroy"); Indestructible does not
                // protect against exile.
                Fx.MoveToExile(creature);
            });

    private static IEffect BuildExileGraveyardEffect(
        ChosenSpellParams p,
        int x,
        Func<object, object> resolver) =>
        Fx.Inline(
            $"{CardName}: exile up to {x} target card(s) from graveyards",
            () =>
            {
                if (x <= 0) return;
                if (p.Targets.Count <= ModeExileGraveyard) return;
                var slot = p.Targets[ModeExileGraveyard];
                if (slot.Count == 0) return;

                var exiled = 0;
                foreach (var token in slot)
                {
                    if (exiled >= x) break; // "up to X"
                    if (resolver(token) is not ICard card) continue;
                    // CR 608.2b — only cards still in a graveyard at
                    // resolution are exiled.
                    if (card.Zone != ZoneType.Graveyard) continue;
                    Fx.MoveToExile(card);
                    exiled++;
                }
            });
}
