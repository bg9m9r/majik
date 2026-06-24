using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rabbit Response (Bloomburrow, {2}{W}{W}).
///
/// Instant. Oracle text (verified against the embedded Scryfall seed):
///   "Creatures you control get +2/+1 until end of turn. If you control a
///    Rabbit, scry 2. (Look at the top two cards of your library, then put
///    any number of them on the bottom and the rest on top in any order.)"
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {2}{W}{W}; mana value 4; colors W.</item>
///   <item>Type line: Instant.</item>
/// </list>
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Instant {2}{W}{W}, loaded from the embedded JSON
///   definition (<c>rabbit-response.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
///   <see cref="CardDefinitionFactory"/> — same data-backed posture as
///   <see cref="ZealousPersecutionFactory"/>. Color W is derived from the
///   {W}{W} pips by the layer pipeline; no explicit color stamp needed.
/// - <b>Resolve-time team pump</b> (via <see cref="BuildResolveEffect"/>): on
///   resolution it snapshots the controller's battlefield creatures (CR 608.2)
///   and registers a <see cref="PumpUntilEndOfTurnEffect"/>(+2, +1) on each —
///   a CR 613.1c Layer 7c +P/+T modification that expires at the cleanup step
///   (CR 514.2). Same team-pump shape as the +P/+0 half of
///   <see cref="GoblinSurpriseFactory"/> / the +1/+1 half of
///   <see cref="ZealousPersecutionFactory"/>. Tokens or creatures entering
///   after resolution do NOT pick up the rider (one-shot snapshot, CR 608.2).
/// - <b>Conditional scry 2 rider</b>: "If you control a Rabbit, scry 2."
///   (CR 701.20 — scry). The intervening-if condition is checked at
///   RESOLUTION (CR 608.2 — effects act on current game state): the effect
///   reads whether the controller controls a creature with the Rabbit subtype
///   (CR 205.3m) AT THE MOMENT IT RUNS. If so it scries 2 via the same path as
///   <c>ScrySelfEffectDef</c> — <see cref="ScryAction.Peek"/> the top two,
///   prompt the controller's live agent (off the
///   <see cref="ResolutionContext"/>, falling back to the
///   <see cref="AgentRegistry"/>, then all-to-bottom), then commit via
///   <see cref="Fx.Scry"/>. No Rabbit controlled → the scry is skipped (the
///   spell still resolves, just without the rider).
///
/// ## Why a named factory
/// The board-wide +2/+1 team pump is not expressible in the single-target
/// pump DSL primitive, and the "If you control a Rabbit" intervening-if gate
/// on the scry rider has no spell-template coverage. The
/// <c>CreaturesYouControlPump</c> template matches the leading pump clause but
/// silently drops the conditional scry tail — hence the bespoke factory.
/// </summary>
[CardName("Rabbit Response")]
public static class RabbitResponseFactory
{
    public const string CardName = "Rabbit Response";
    public const string Slug = "rabbit-response";

    /// <summary>+P magnitude on each creature you control — +2/+1.</summary>
    public const int PumpPower = 2;

    /// <summary>+T magnitude on each creature you control — +2/+1.</summary>
    public const int PumpToughness = 1;

    /// <summary>Scry amount on the Rabbit-gated rider (CR 701.20).</summary>
    public const int ScryAmount = 2;

    /// <summary>
    /// Build the card shape from the embedded JSON definition. Behaviour (the
    /// team pump + conditional scry) is supplied at resolution via
    /// <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build Rabbit Response's resolve effect — creatures the
    /// <paramref name="controller"/> controls get +2/+1 until end of turn, and
    /// if the controller controls a Rabbit at resolution, scry 2. Returns a
    /// single composite <see cref="IEffect"/> so the legacy <c>.Single()</c>
    /// caller contract holds (pump and scry run in oracle order inside the one
    /// effect body).
    /// </summary>
    /// <param name="controller">The spell's controller — "you" in the oracle
    /// text. Their creatures get the pump; the Rabbit check reads their
    /// battlefield.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: creatures you control get +{PumpPower}/+{PumpToughness} until end of turn; " +
                $"if you control a Rabbit, scry {ScryAmount}.",
                async ctx =>
                {
                    ApplyTeamPump(controller);
                    await MaybeScryAsync(controller, ctx).ConfigureAwait(false);
                }),
        };
    }

    /// <summary>
    /// CR 608.2 — snapshot the controller's battlefield creatures at
    /// resolution and register a +2/+1 EOT pump (CR 613.1c Layer 7c, CR 514.2
    /// cleanup expiry) on each. Mirrors
    /// <see cref="ZealousPersecutionFactory.BuildResolveEffect"/>'s friendly
    /// half.
    /// </summary>
    public static void ApplyTeamPump(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // Snapshot before applying so any same-step zone-move side effects
        // don't disturb enumeration. Same posture as Zealous Persecution.
        var creatures = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();

        foreach (var creature in creatures)
        {
            if (creature.Zone != ZoneType.Battlefield) continue;

            // Shape-only safety — without a live ContinuousEffectsService
            // wired onto the creature the rider silently no-ops rather than
            // NRE'ing. Same posture as Zealous Persecution / Goblin Surprise.
            if (creature.ActiveEffects == null) continue;

            creature.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));
        }
    }

    /// <summary>
    /// "If you control a Rabbit, scry 2." CR 205.3m — the Rabbit subtype.
    /// The intervening-if is checked at RESOLUTION (CR 608.2): only if the
    /// controller controls a creature with the Rabbit subtype right now does
    /// the scry fire. The scry path mirrors <c>ScrySelfEffectDef</c> exactly —
    /// peek the top N (CR 701.20), prompt the controller's live agent (off the
    /// resolution context, then the registry, then all-to-bottom), then commit.
    /// </summary>
    private static async ValueTask MaybeScryAsync(Player controller, ResolutionContext ctx)
    {
        // CR 205.3m — "a Rabbit" is any permanent with the Rabbit creature
        // subtype the controller controls (CR 109.5 — controlled at the moment
        // the effect checks). Tokens count; a non-creature with the subtype
        // would count too, but Rabbit is creature-only in practice.
        var controlsRabbit = controller.Zones.Battlefield.GetCards()
            .Any(c => c.HasSubtype(CardSubtype.Rabbit));
        if (!controlsRabbit) return;

        var peeked = ScryAction.Peek(controller, ScryAmount);
        if (peeked.Count == 0) return;

        // Prompt the live agent off the resolution context; fall back to the
        // registry, then all-to-bottom (same posture as BuildScrySelfEffect).
        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        ScryAction.ScryDecision decision;
        if (agent != null)
        {
            decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked, ctx.Ct)
                .ConfigureAwait(false);
        }
        else
        {
            decision = new ScryAction.ScryDecision(
                ToBottom: peeked.ToList(),
                TopOrder: Array.Empty<ICard>());
        }

        Fx.Scry(controller, ScryAmount, decision);
    }
}
