using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Felonious Rage (Murders at Karlov Manor, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target creature you control gets +2/+0 and gains haste until end of
///    turn. When that creature dies this turn, create a 2/2 white and blue
///    Detective creature token."
///
/// ## Implementation
///
/// A combination of the pump + haste grant shared with the Giant-Growth /
/// Berserk pump family and the delayed "when that creature dies this turn"
/// clause shared with <see cref="SearingBloodFactory"/> — except here the
/// death trigger creates a token (CR 111 / CR 701.39-style mint via
/// <see cref="TokenFactory.CreateOnBattlefield"/>) rather than dealing
/// damage. The token half mirrors <see cref="NoviceInspectorFactory"/>'s
/// Detective flavour (the token IS a Detective).
///
/// Card shape comes from the embedded JSON (<c>felonious-rage.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a controller-scoped target gatherer, a target resolver, and a
/// trigger manager supplied by the caller's <see cref="GameContext"/> (not
/// expressible in the data-only JSON schema). This mirrors the live
/// binder-chain discipline — the named-factory <c>BuildSpellDefinition</c>
/// is the test-facing surface; production resolution flows through the
/// oracle spell-template registry.
///
/// On resolution:
///   1. CR 608.2b — re-check the target is still a Creature the caster
///      controls on the battlefield; otherwise no-op.
///   2. Layer 7c — register a <see cref="PumpUntilEndOfTurnEffect"/>(+2,+0)
///      on the target's <see cref="Creature.ActiveEffects"/> (CR 613.1g /
///      CR 514.2 — expires in cleanup).
///   3. Layer 6 — register a <see cref="GrantKeywordUntilEndOfTurnEffect"/>
///      granting Haste (CR 702.10) until end of turn.
///   4. CR 603.7 — register a one-shot <see cref="DelayedTriggeredAbility"/>
///      watching <see cref="CardMovedEvent"/> for the EXACT targeted
///      creature moving Battlefield→Graveyard strictly after this
///      resolution (timestamp fence; CR 700.4 — Battlefield→Graveyard =
///      dies). On fire, create one 2/2 white-and-blue Detective creature
///      token under the spell's controller.
///
/// When no <see cref="TriggerManager"/> / continuous-effects service is
/// supplied (shape / dispatcher tests) the relevant clause is skipped — the
/// same defensive posture every pump + delayed-trigger card uses.
/// </summary>
[CardName("Felonious Rage")]
public static class FeloniousRageFactory
{
    public const string CardName = "Felonious Rage";
    public const string Slug = "felonious-rage";

    /// <summary>Layer 7c +P magnitude (CR 613.1g).</summary>
    public const int PumpPower = 2;

    /// <summary>Layer 7c +T magnitude — Felonious Rage is power-only.</summary>
    public const int PumpToughness = 0;

    /// <summary>Granted keyword — CR 702.10 Haste.</summary>
    public const string GrantedHaste = "Haste";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time SpellDefinition. Single 1..1 "target creature
    /// you control" request. On resolution: +2/+0 and Haste until end of
    /// turn; arm a delayed end-of-life trigger that mints a 2/2 W/U
    /// Detective token if the creature dies this turn.
    /// </summary>
    /// <param name="caster">Spell controller — the "you control" filter on
    /// the target gatherer and the controller the token is minted under.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="triggers">Optional trigger manager. When supplied the
    /// delayed "dies this turn → create token" clause is registered
    /// (CR 603.7). When null the clause is skipped (pump + haste still
    /// apply — shape-only tests).</param>
    /// <param name="zones">Optional zone service. When supplied the death
    /// token is placed via the ZoneService so its arrival event fires.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        TriggerManager? triggers = null,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    // CR 109.5 / CR 608.2b — "you control" reads
                    // Permanent.Controller at choose-time (controller-scoped).
                    CandidateGatherer: ctx => caster.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Where(c => ReferenceEquals(c.Controller, caster))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                {
                    return Array.Empty<IEffect>();
                }

                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: +2/+0 + haste EOT + delayed Detective token on death",
                        () => Resolve(raw, caster, triggers, zones)),
                };
            });
    }

    private static void Resolve(
        object raw,
        Player caster,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        // CR 608.2b — illegal target / non-Creature resolver → no-op.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        // CR 109.5 — "you control" re-checked at resolution.
        if (!ReferenceEquals(target.Controller, caster)) return;

        // Layer 7c — +2/+0 until end of turn (CR 613.1g / CR 514.2). Skipped
        // when no continuous-effects service is wired (shape-only tests).
        if (target.ActiveEffects != null)
        {
            target.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));

            // Layer 6 — grant Haste until end of turn (CR 702.10).
            target.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(target, GrantedHaste));
        }

        // No trigger manager (shape-only path) — skip the delayed clause.
        if (triggers == null) return;

        // CR 603.7 — one-shot delayed triggered ability. Fires on the first
        // CardMovedEvent of the EXACT targeted creature moving
        // Battlefield→Graveyard strictly after this resolution (timestamp
        // fence — a creature that already died earlier this turn isn't
        // retroactively counted). CR 700.4 — Battlefield→Graveyard = dies.
        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var tokenEffect = new Effect(
            $"{CardName}: create a 2/2 W/U Detective token ({target.Name} died this turn)",
            // CR 111 — mint one 2/2 white-and-blue Detective creature token
            // under the spell's controller.
            () => TokenFactory.CreateOnBattlefield(
                new TokenFactory.TokenSpec(
                    Name: "Detective",
                    Power: 2,
                    Toughness: 2,
                    Subtypes: new[] { CardSubtype.Detective },
                    Colors: new[] { ManaColor.White, ManaColor.Blue }),
                caster,
                zones));

        var delayed = new DelayedTriggeredAbility(
            source: target,
            controller: caster,
            condition: new EventTriggerCondition<CardMovedEvent>(
                (e, _) => ReferenceEquals(e.Card, target)
                          && e.FromZone == ZoneType.Battlefield
                          && e.ToZone == ZoneType.Graveyard
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { tokenEffect });

        triggers.RegisterDelayed(delayed);
    }
}
