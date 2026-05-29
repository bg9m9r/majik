using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Infernal Grasp (Innistrad: Midnight Hunt, {1}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Destroy target creature. You lose 2 life."
///
/// ## Implementation
///
/// Same single-target destroy-creature instant shape as
/// <see cref="DoomBladeFactory"/>, with two differences:
///   1. NO colour filter — Infernal Grasp destroys ANY target creature
///      (Doom Blade is restricted to nonblack creatures).
///   2. A fixed 2-life-loss rider on the caster appended to the resolution.
///
/// Card shape comes from the embedded JSON (<c>infernal-grasp.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's <see cref="GameContext"/>
/// (not expressible in the data-only JSON schema), and the life-loss clause
/// needs a handle on the caster.
///
/// On resolution (CR 608.2e — left-to-right clause ordering):
///   1. <b>Destroy target creature.</b> Re-checks the target is still a
///      Creature on the Battlefield (CR 608.2b — illegal-target filter at
///      resolution → that clause is a no-op) and destroys it via
///      <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
///      <see cref="ZoneMoveReason.Destroy"/> (CR 701.7), so indestructible
///      (CR 702.12) and regeneration shields (CR 701.15) are honoured at the
///      destroy site.
///   2. <b>You lose 2 life.</b> The caster loses 2 life (CR 119.3) via
///      <see cref="Fx.LoseLife(Player, int)"/>. This clause does not target
///      (CR 608.2) — it resolves even when the destroy clause was a no-op
///      because the creature had already left the battlefield.
/// </summary>
[CardName("Infernal Grasp")]
public static class InfernalGraspFactory
{
    public const string CardName = "Infernal Grasp";
    public const string Slug = "infernal-grasp";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>CR 119.3 — fixed self-life-loss rider.</summary>
    public const int LifeLoss = 2;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Infernal Grasp is
    /// cast. Single 1..1 "target creature" request, no X. On resolution:
    ///   1. Destroys the chosen creature via
    ///      <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
    ///      <see cref="ZoneMoveReason.Destroy"/> (CR 701.7), iff it is still a
    ///      Creature on the Battlefield (CR 608.2b).
    ///   2. The caster loses 2 life (CR 119.3) — unconditionally, since that
    ///      clause does not target.
    /// </summary>
    /// <param name="caster">The player who cast Infernal Grasp; loses 2 life
    /// on resolution.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object). Pass
    /// <c>o =&gt; o</c> for tests that hand creatures directly.</param>
    public static SpellDefinition BuildSpellDefinition(Player caster, Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every creature on any
                    // battlefield. Removal intent pushes the opponent's
                    // biggest threat up the bot's ranker.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var resolved = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline($"{CardName}: destroy target creature, you lose {LifeLoss} life", () =>
                    {
                        // CR 608.2e step 1 — Destroy target creature.
                        // CR 608.2b — resolution-time legality re-check; an
                        // illegal target makes this clause a no-op.
                        if (resolved is Creature target && target.Zone == ZoneType.Battlefield)
                        {
                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) honoured via the
                            // Destroy-reason gate in MoveToGraveyard.
                            Fx.MoveToGraveyard(target, ZoneMoveReason.Destroy);
                        }

                        // CR 608.2e step 2 / CR 119.3 — "You lose 2 life."
                        // Does not target, so it resolves even when the
                        // destroy clause above was a no-op.
                        Fx.LoseLife(caster, LifeLoss);
                    }),
                };
            });
    }
}
