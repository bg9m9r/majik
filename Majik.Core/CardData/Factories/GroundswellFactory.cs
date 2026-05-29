using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Groundswell (Worldwake, {G}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target creature gets +2/+2 until end of turn.
///    Landfall — If you had a land enter the battlefield under your control
///    this turn, that creature gets +4/+4 until end of turn instead."
///
/// ## Implementation
///
/// Groundswell combines two shapes the engine already supports:
///   - <b>Single-target +X/+X pump</b> — exactly Giant Growth's body
///     (<see cref="GiantGrowthFactory"/>): a 1..1 "target creature"
///     <see cref="TargetRequest"/> whose resolve registers a
///     <see cref="PumpUntilEndOfTurnEffect"/> on the target's
///     <see cref="Creature.ActiveEffects"/> (Layer 7c CR 613.1g; expires in
///     the cleanup step, CR 514.2).
///   - <b>Landfall resolution-time gate</b> — exactly Searing Blaze's
///     condition (<see cref="SearingBlazeFactory.IsLandfallActive"/>): CR
///     702.142 describes a triggered-ability shape, but Groundswell uses a
///     landfall-style condition on an instant — a resolution-time state check
///     ("if you had a land enter under your control this turn"), not a printed
///     trigger. The flag is sampled from
///     <see cref="TurnState.LandEnteredThisTurn(Player)"/> at resolution.
///
/// The "+4/+4 ... instead" wording (CR 608.2 — a single conditional
/// replacement of the magnitude) means exactly ONE pump is applied: +2/+2 if
/// landfall is inactive, +4/+4 if active. We never stack a +2/+2 and a
/// +4/+4.
///
/// Card shape comes from the embedded JSON (<c>groundswell.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> — same pattern as
/// <see cref="PlayWithFireFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver (and the controller's live <see cref="TurnState"/>)
/// supplied by the caller's <see cref="GameContext"/> — neither is expressible
/// in the data-only JSON schema.
///
/// CR 608.2b: if the chosen target is no longer a creature on the battlefield
/// at resolution, the pump no-ops (illegal target).
/// </summary>
[CardName("Groundswell")]
public static class GroundswellFactory
{
    public const string CardName = "Groundswell";
    public const string Slug = "groundswell";
    public const string PrintedManaCost = "{G}";

    /// <summary>Layer 7c +P/+T magnitude with landfall inactive (CR 613.1g).</summary>
    public const int BasePump = 2;

    /// <summary>Layer 7c +P/+T magnitude with landfall active — applied
    /// "instead" of <see cref="BasePump"/> (CR 613.1g / CR 608.2).</summary>
    public const int LandfallPump = 4;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Groundswell is cast.
    /// Single 1..1 "target creature" request, no X. On resolution: sample the
    /// controller's per-turn landfall tally (CR 702.142) and register a single
    /// <see cref="PumpUntilEndOfTurnEffect"/> of the appropriate magnitude on
    /// the target's <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires in
    /// cleanup).
    /// </summary>
    /// <param name="controller">Spell controller — whose per-turn landfall
    /// tally drives the conditional +4/+4 upgrade.</param>
    /// <param name="turnStateResolver">Callback returning the live
    /// <see cref="TurnState"/> at resolution time. When the callback returns
    /// null (no driver wired — typical for shape / dispatcher tests) the gate
    /// is treated as inactive (base +2/+2 applies).</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<TurnState?> turnStateResolver,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(turnStateResolver);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        "Groundswell — target creature gets +2/+2 (or +4/+4 with landfall) until end of turn",
                        () => Resolve(raw, controller, turnStateResolver)),
                };
            });
    }

    /// <summary>
    /// Sample the controller's per-turn landfall tally (CR 702.142): true iff
    /// at least one land has entered the battlefield under
    /// <paramref name="controller"/>'s control this turn. When no
    /// <see cref="TurnState"/> is wired the gate is treated as inactive (base
    /// +2/+2 applies). Mirrors
    /// <see cref="SearingBlazeFactory.IsLandfallActive"/>.
    /// </summary>
    public static bool IsLandfallActive(
        Player controller,
        Func<TurnState?> turnStateResolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(turnStateResolver);
        var turnState = turnStateResolver.Invoke();
        return turnState != null && turnState.LandEnteredThisTurn(controller);
    }

    private static void Resolve(object raw, Player controller, Func<TurnState?> turnStateResolver)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // CR 608.2 — apply exactly ONE pump: +4/+4 "instead" when landfall is
        // active this turn, otherwise the base +2/+2.
        var pump = IsLandfallActive(controller, turnStateResolver) ? LandfallPump : BasePump;

        target.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(target, pump, pump));
    }
}
