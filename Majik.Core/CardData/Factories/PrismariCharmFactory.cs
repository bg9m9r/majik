using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Prismari Charm (Strixhaven: School of Mages,
/// {U}{R}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Choose one —
///     • Surveil 2, then draw a card.
///     • Prismari Charm deals 1 damage to each of one or two targets.
///     • Return target nonland permanent to its owner's hand."
///
/// CR 700.2d — modal "Choose one —" spell. Three <see cref="TargetRequest"/>s
/// (one per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so unchosen modes don't gate the cast per CR 601.2c).
///
/// Mode 0 — "Surveil 2, then draw a card":
///   CR 701.42 — surveil 2 (look at the top 2 cards of your library; put any
///   number into your graveyard and the rest back on top in any order).
///   Then CR 121 — draw 1 card. Routes the surveil decision through the
///   caster's <see cref="AgentRegistry"/> entry (same pattern as
///   <see cref="SpellTemplates.Templates.Library.LibrarySpellFactory"/>'s
///   <c>SurveilSelfSpell</c>); falls back to all-to-graveyard when no agent
///   is registered. Uses <see cref="Fx.Surveil"/> so the surveil publishes
///   a <see cref="SurveilEvent"/> (for "whenever you surveil" triggers) and
///   <see cref="Fx.DrawCards"/> for the draw.
///
/// Mode 1 — "Prismari Charm deals 1 damage to each of one or two targets":
///   Variable target slot (<see cref="TargetRequest"/> with MinTargets=0,
///   MaxTargets=2). Note: this is "1 damage to EACH" of the chosen targets,
///   not divided — every chosen target takes exactly 1 (unlike
///   <see cref="ForkedBoltFactory"/>'s "2 damage divided"). At resolution,
///   every legal target takes 1 damage via <see cref="Fx.DealDamageAny"/>
///   (Player / Creature / Planeswalker — CR 306.7 planeswalker routing).
///   Illegal-at-resolution targets are filtered per CR 608.2b.
///
/// Mode 2 — "Return target nonland permanent to its owner's hand":
///   Mirrors <see cref="DisperseFactory"/> — CR 701.10 / CR 608.2b. Lands
///   are not legal targets; non-permanents and off-battlefield targets are
///   filtered at resolution. Uses <see cref="ZoneService"/> when supplied
///   for replacement-bus-aware moves, otherwise raw zone manipulation.
///
/// Pattern mirrors <see cref="IzzetCharmFactory"/> / <see cref="BorosCharmFactory"/>
/// / <see cref="ThrabenCharmFactory"/> for the choose-one modal shape.
/// </summary>
[CardName("Prismari Charm")]
public static class PrismariCharmFactory
{
    public const string CardName = "Prismari Charm";
    public const string PrintedManaCost = "{U}{R}";

    public const int ModeSurveilDraw = 0;
    public const int ModeDamage      = 1;
    public const int ModeBounce      = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Surveil amount for mode 0.</summary>
    public const int SurveilCount = 2;

    /// <summary>Damage dealt to each chosen target by mode 1.</summary>
    public const int DamagePerTarget = 1;

    /// <summary>Max number of targets in mode 1's variable slot.</summary>
    public const int Mode1MaxTargets = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Surveil 2, then draw a card.",
        "Prismari Charm deals 1 damage to each of one or two targets.",
        "Return target nonland permanent to its owner's hand.",
    };

    /// <summary>Construct Prismari Charm as an Instant owned by <paramref name="owner"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Prismari Charm. All three modes are wired.
    /// </summary>
    /// <param name="caster">The player casting the spell.</param>
    /// <param name="targetResolver">Resolver from the caller's GameContext.</param>
    /// <param name="allPlayers">All players in the game.</param>
    /// <param name="zoneService">Optional ZoneService for replacement-bus-aware
    /// zone moves on mode 2 (bounce). When null, raw zone manipulation is used.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        IReadOnlyList<Player> allPlayers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);
        ArgumentNullException.ThrowIfNull(allPlayers);

        // CR 601.2c — target requests per mode. MinTargets=0 so unchosen
        // modes don't gate the cast (mirrors IzzetCharmFactory /
        // BorosCharmFactory / ThrabenCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — no target (self-only surveil + draw).
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Draw),
            // Mode 1 — 1 or 2 "any targets" (variable). When this mode is
            // CHOSEN, cast-flow validation enforces ≥1 target; when not
            // chosen, MinTargets=0 prevents the unchosen slot from gating.
            new TargetRequest("any target", 0, Mode1MaxTargets, Array.Empty<object>(), BotIntent.Burn),
            // Mode 2 — target nonland permanent (bounce). Disperse-style.
            new TargetRequest("target nonland permanent", 0, 1, Array.Empty<object>(), BotIntent.Bounce),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Draw,
                BotIntent.Burn,
                BotIntent.Bounce,
            },
            EffectFactory: p =>
            {
                // Honor either the multi-pick list (first entry wins for a
                // Choose-one card) or the legacy scalar ModeIndex.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;       // CR 700.2d — each mode at most once
                    if (seen.Count > PickCount) break;  // CR 700.2d — pick count cap

                    switch (raw)
                    {
                        case ModeSurveilDraw:
                            effectsOut.Add(BuildSurveilDrawEffect(caster));
                            break;
                        case ModeDamage:
                            effectsOut.Add(BuildDamageEffect(p, targetResolver));
                            break;
                        case ModeBounce:
                            effectsOut.Add(BuildBounceEffect(p, targetResolver, zoneService));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0: surveil 2, then draw a card
    // -----------------------------------------------------------------------

    private static IEffect BuildSurveilDrawEffect(Player caster) =>
        new Effect($"Prismari Charm — surveil {SurveilCount}, then draw a card", async ctx =>
        {
            // CR 701.42 — surveil 2 (agent-driven via AgentRegistry; falls
            // back to all-to-graveyard when no agent is registered, same as
            // LibrarySpellFactory.SurveilSelfSpell).
            var peeked = SurveilAction.Peek(caster, SurveilCount);
            if (peeked.Count > 0)
            {
                SurveilAction.SurveilDecision decision;
                var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                if (agent != null)
                {
                    // TODO: remove sync-over-async once IEffect.Execute becomes
                    // async (same posture as LibrarySpellFactory.SurveilSelfSpell).
                    decision = (await agent.ChooseSurveilDecisionAsync( ctx.Game, peeked).ConfigureAwait(false));
                }
                else
                {
                    decision = new SurveilAction.SurveilDecision(
                        ToGraveyard: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                // Fx.Surveil publishes the SurveilEvent so "whenever you
                // surveil" triggers (Ledger Shredder etc.) fire.
                Fx.Surveil(caster, SurveilCount, decision);
            }

            // CR 121 — then draw a card. Empty-library mid-draw flags loss
            // via Fx.DrawCards (CR 704.5b).
            Fx.DrawCards(caster, 1);
        });

    // -----------------------------------------------------------------------
    // Mode 1: 1 damage to EACH of 1 or 2 targets (NOT divided)
    // -----------------------------------------------------------------------

    private static IEffect BuildDamageEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"Prismari Charm — deals {DamagePerTarget} damage to each of one or two targets", () =>
        {
            if (p.Targets.Count <= ModeDamage) return;
            var slot = p.Targets[ModeDamage];
            if (slot.Count == 0) return;

            // Resolve all chosen target tokens, dropping illegal-at-
            // resolution picks (CR 608.2b). Legal targets are Player /
            // Creature / Planeswalker — same shape as Lightning Bolt.
            foreach (var token in slot)
            {
                var resolved = resolver(token);
                if (!IsLegalAnyTarget(resolved)) continue;
                Fx.DealDamageAny(resolved, DamagePerTarget);
            }
        });

    /// <summary>"Any target" — Player / Creature / Planeswalker.</summary>
    private static bool IsLegalAnyTarget(object live) =>
        live is Player || live is Creature || live is Planeswalker;

    // -----------------------------------------------------------------------
    // Mode 2: return target nonland permanent to its owner's hand
    // -----------------------------------------------------------------------

    private static IEffect BuildBounceEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        ZoneService? zoneService) =>
        new Effect("Prismari Charm — return target nonland permanent to its owner's hand", () =>
        {
            if (p.Targets.Count <= ModeBounce) return;
            var slot = p.Targets[ModeBounce];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — target must still be a permanent on the battlefield.
            if (resolved is not Permanent target) return;
            if (target.Zone != ZoneType.Battlefield) return;

            // Nonland gate (Disperse-style). Lands are NOT legal targets.
            if (target.HasType(CardType.Land)) return;

            var targetOwner = target.Owner;
            if (targetOwner == null) return;

            var controller = target.Controller ?? targetOwner;

            // CR 701.10 — return to owner's hand.
            if (zoneService != null)
            {
                zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
            }
            else
            {
                controller.Zones.Battlefield.RemoveCard(target);
                targetOwner.Zones.Hand.AddCard(target);
                target.SetZone(ZoneType.Hand);
                target.SetController(targetOwner);
            }
        });
}
