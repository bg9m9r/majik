using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Might of Old Krosa (Time Spiral, {G}).
///
/// Instant. Oracle text (verified against Scryfall 2026-05-29):
///   "Target creature gets +2/+2 until end of turn. If you cast this spell
///    during your main phase, that creature gets +4/+4 until end of turn
///    instead."
///
/// ## Implementation
///
/// Same plain "+P/+P until end of turn to a target creature" shape as
/// <see cref="GiantGrowthFactory"/> / <see cref="BruteForceFactory"/>, with a
/// cast-time conditional that swaps the magnitude. The "If you cast this spell
/// during your main phase … instead" clause is a cast-time check (CR 608.2 —
/// the condition is locked in when the spell is cast, not re-checked at
/// resolution), so the magnitude is decided when the <see cref="SpellDefinition"/>
/// is built and the resulting <see cref="PumpUntilEndOfTurnEffect"/> registered
/// on resolve carries the chosen +P/+P (CR 514.2 — expires in cleanup).
///
/// "Your main phase" is the caster's own main phase. Only the active player has
/// a main phase (CR 505), so the conditional is satisfied exactly when the
/// caster is the active player AND the current step is a main phase
/// (<see cref="PhaseStateTypeExtensions.IsMain"/>). Casting at instant speed
/// outside a main phase — or during an opponent's main phase — yields the base
/// +2/+2.
///
/// Card shape comes from the embedded JSON (<c>might-of-old-krosa.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildDefinition(bool)"/> because the cast-time magnitude is not
/// expressible in the data-only JSON schema.
/// </summary>
[CardName("Might of Old Krosa")]
public static class MightOfOldKrosaFactory
{
    public const string CardName = "Might of Old Krosa";
    public const string Slug = "might-of-old-krosa";
    public const string PrintedManaCost = "{G}";

    /// <summary>Layer 7c +P/+T magnitude when cast outside a main phase
    /// (CR 613.1g).</summary>
    public const int BasePumpAmount = 2;

    /// <summary>Layer 7c +P/+T magnitude when cast during the caster's own
    /// main phase — the "instead" clause (CR 613.1g).</summary>
    public const int MainPhasePumpAmount = 4;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Convenience overload that derives the cast-time condition from the
    /// caller's <see cref="GameContext"/>: the "instead" clause applies when
    /// <paramref name="caster"/> is the active player and the current step is a
    /// main phase (CR 505 / CR 116.3a). Use this from cast paths that hold the
    /// live context.
    /// </summary>
    public static SpellDefinition BuildDefinition(GameContext ctx, Player caster)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(caster);

        var castDuringMainPhase =
            ReferenceEquals(ctx.ActivePlayer, caster)
            && ctx.CurrentPhase is { } phase
            && phase.IsMain();

        return BuildDefinition(castDuringMainPhase);
    }

    /// <summary>
    /// Build the "target creature gets +2/+2 (or +4/+4 if cast during your main
    /// phase) until end of turn" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the target is still a <see cref="Creature"/> on the
    /// Battlefield (CR 608.2b — illegal target → no-op). When valid, registers a
    /// <see cref="PumpUntilEndOfTurnEffect"/> with the cast-time magnitude on the
    /// target's <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires in
    /// cleanup). When ActiveEffects is null (shape-only tests without a live
    /// <see cref="ContinuousEffectsService"/>), the registration is a no-op.
    /// </summary>
    /// <param name="castDuringMainPhase">True when this spell was cast during the
    /// caster's own main phase, selecting the +4/+4 "instead" magnitude.</param>
    public static SpellDefinition BuildDefinition(bool castDuringMainPhase)
    {
        var amount = castDuringMainPhase ? MainPhasePumpAmount : BasePumpAmount;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        $"Might of Old Krosa — target creature gets +{amount}/+{amount} until end of turn",
                        () => Resolve(raw, amount)),
                };
            });
    }

    private static void Resolve(object raw, int amount)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        target.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(target, amount, amount));
    }
}
