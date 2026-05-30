using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Simic Charm (Gatecrash, {G}{U}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Choose one —
///     • Target creature gets +3/+3 until end of turn.
///     • Permanents you control gain hexproof until end of turn.
///     • Return target creature to its owner's hand."
///
/// CR 700.2d — modal "Choose one —" spell. Three <see cref="TargetRequest"/>s
/// (one per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so unchosen modes don't gate the cast per CR 601.2c).
///
/// Mode 0 — "Target creature gets +3/+3 until end of turn":
///   Registers a <see cref="PumpUntilEndOfTurnEffect"/>(+3, +3) on the
///   target creature's <see cref="Creature.ActiveEffects"/> (CR 613.1g
///   Layer 7c, expiring at cleanup CR 514.2). Same shape as the pump
///   family (e.g. Vines of Vastwood's kicked +4/+4). Non-creature /
///   off-battlefield targets no-op per CR 608.2b.
///
/// Mode 1 — "Permanents you control gain hexproof until end of turn":
///   Mirrors <see cref="BorosCharmFactory"/>'s "permanents you control gain
///   indestructible" mode. Enumerates the caster's battlefield and registers
///   a <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "Hexproof"
///   (CR 702.11b, Layer 6 grant CR 613.1f, expiring at cleanup CR 514.2) to
///   every <see cref="Creature"/> the caster controls. Hexproof drives the
///   engine's existing target-legality handling in
///   <see cref="Majik.Core.Targeting.TargetLegality"/>. Non-creature
///   permanents (lands, artifacts, enchantments, planeswalkers) are not yet
///   wired for the EOT keyword grant — same creatures-only v1 limitation as
///   <see cref="BorosCharmFactory"/> / <see cref="SelflessSpiritFactory"/>.
///
/// Mode 2 — "Return target creature to its owner's hand":
///   Bounce. Mirrors <see cref="PrismariCharmFactory"/>'s mode-2 bounce but
///   restricted to creatures (Simic Charm targets a creature, not a nonland
///   permanent). CR 701.10 — return to owner's hand. CR 608.2b — the target
///   must still be a creature on the battlefield. Uses <see cref="ZoneService"/>
///   when supplied (replacement-bus-aware moves), otherwise raw zone
///   manipulation.
///
/// Pattern mirrors <see cref="IzzetCharmFactory"/> / <see cref="BorosCharmFactory"/>
/// / <see cref="PrismariCharmFactory"/> for the choose-one modal shape.
/// </summary>
[CardName("Simic Charm")]
public static class SimicCharmFactory
{
    public const string CardName = "Simic Charm";
    public const string PrintedManaCost = "{G}{U}";

    public const int ModePump     = 0;
    public const int ModeHexproof = 1;
    public const int ModeBounce   = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Layer 7c +P/+T magnitude for mode 0 (CR 613.1g).</summary>
    public const int PumpAmount = 3;

    /// <summary>Granted keyword for mode 1 — CR 702.11 Hexproof.</summary>
    public const string GrantedHexproof = "Hexproof";

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        $"Target creature gets +{PumpAmount}/+{PumpAmount} until end of turn.",
        "Permanents you control gain hexproof until end of turn.",
        "Return target creature to its owner's hand.",
    };

    /// <summary>Construct Simic Charm as an Instant owned by <paramref name="owner"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Simic Charm. All three modes are wired.
    /// </summary>
    /// <param name="caster">The player casting the spell.</param>
    /// <param name="targetResolver">Resolver from the caller's GameContext.</param>
    /// <param name="allPlayers">All players in the game.</param>
    /// <param name="continuousEffects">Optional per-turn continuous-effects
    /// service. Required for mode 1 (hexproof) to register layer-6 grants on
    /// permanents the caster controls. When null that mode performs no layer
    /// registration (shape-only path). Mode 0 falls back to the target
    /// creature's own <see cref="Creature.ActiveEffects"/>.</param>
    /// <param name="zoneService">Optional ZoneService for replacement-bus-aware
    /// zone moves on mode 2 (bounce). When null, raw zone manipulation is used.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        IReadOnlyList<Player> allPlayers,
        ContinuousEffectsService? continuousEffects = null,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);
        ArgumentNullException.ThrowIfNull(allPlayers);

        // CR 601.2c — target requests for every mode that takes a target.
        // MinTargets=0 so unchosen modes don't gate the cast
        // (mirrors IzzetCharmFactory / BorosCharmFactory / PrismariCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — target creature (+3/+3).
            new TargetRequest("target creature", 0, 1, Array.Empty<object>(), BotIntent.CombatTrick),
            // Mode 1 — no target (permanents you control).
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Protection),
            // Mode 2 — target creature (bounce).
            new TargetRequest("target creature", 0, 1, Array.Empty<object>(), BotIntent.Bounce),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.CombatTrick,
                BotIntent.Protection,
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
                        case ModePump:
                            effectsOut.Add(BuildPumpEffect(p, targetResolver));
                            break;
                        case ModeHexproof:
                            effectsOut.Add(BuildHexproofEffect(caster, continuousEffects));
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
    // Mode 0: target creature gets +3/+3 until end of turn
    // -----------------------------------------------------------------------

    private static IEffect BuildPumpEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"Simic Charm — target creature gets +{PumpAmount}/+{PumpAmount} until end of turn", () =>
        {
            if (p.Targets.Count <= ModePump) return;
            var slot = p.Targets[ModePump];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — target must still be a creature on the battlefield.
            if (resolved is not Creature target) return;
            if (target.Zone != ZoneType.Battlefield) return;
            if (target.ActiveEffects == null) return;

            // CR 613.1g Layer 7c — +3/+3 with EOT expiry (CR 514.2).
            target.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(target, PumpAmount, PumpAmount));
        });

    // -----------------------------------------------------------------------
    // Mode 1: permanents you control gain hexproof until end of turn
    // -----------------------------------------------------------------------

    private static IEffect BuildHexproofEffect(
        Player caster,
        ContinuousEffectsService? continuousEffects) =>
        new Effect("Simic Charm — permanents you control gain hexproof until end of turn", () =>
        {
            if (continuousEffects == null) return;

            // CR 702.11b / 613.1f — grant Hexproof until end of turn to every
            // creature the caster controls on the battlefield.
            // GrantKeywordUntilEndOfTurnEffect targets Creature objects; for
            // non-creature permanents (lands, artifacts, enchantments) the
            // EOT keyword grant path is not yet wired — same creatures-only
            // limitation as BorosCharmFactory / SelflessSpiritFactory.
            foreach (var creature in caster.Zones.Battlefield
                .GetCards()
                .OfType<Creature>()
                .ToList())
            {
                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, GrantedHexproof));
            }
        });

    // -----------------------------------------------------------------------
    // Mode 2: return target creature to its owner's hand
    // -----------------------------------------------------------------------

    private static IEffect BuildBounceEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        ZoneService? zoneService) =>
        new Effect("Simic Charm — return target creature to its owner's hand", () =>
        {
            if (p.Targets.Count <= ModeBounce) return;
            var slot = p.Targets[ModeBounce];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — target must still be a creature on the battlefield.
            if (resolved is not Creature target) return;
            if (target.Zone != ZoneType.Battlefield) return;

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
