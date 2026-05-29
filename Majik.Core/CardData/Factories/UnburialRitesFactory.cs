using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Unburial Rites (Avacyn Restored, {4}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-05-29):
///   "Return target creature card from your graveyard to the battlefield.
///    Flashback {3}{W} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// Unburial Rites is the "graveyard → battlefield, your graveyard only, no
/// life cost" sibling of <see cref="ReanimateFactory"/> /
/// <see cref="AgadeemsAwakeningFactory"/>: the reanimation body is the same
/// <see cref="Fx.ReturnFromGraveyardToBattlefield"/> path, but the target is
/// a single creature card restricted to the CASTER's graveyard ("your
/// graveyard") and there is no life-loss tail. It additionally carries the
/// Flashback keyword (CR 702.34), wired the same way as
/// <see cref="FaithfulMendingFactory"/>.
///
/// ## Card identity comes from JSON
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>unburial-rites.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="DreadboreFactory"/> / <see cref="AgadeemsAwakeningFactory"/>.
/// The "target creature card from your graveyard" resolve body is layered on
/// in code because the JSON <see cref="SpellDefinition"/> schema does not yet
/// express a graveyard-scoped target request.
///
/// ## Implemented (v1)
/// - Sorcery shape at printed cost {4}{B} (mono-black), owner / controller
///   wired from JSON.
/// - <see cref="BuildSpellDefinition"/> — a single 1..1 "target creature card
///   in your graveyard" <see cref="TargetRequest"/> (Intent:
///   <see cref="BotIntent.Reanimate"/>). The candidate gatherer yields creature
///   cards in the caster's graveyard only ("your graveyard"). On resolution,
///   the target is re-checked per CR 608.2b (must still be a creature card in
///   the caster's graveyard); on success it is returned to the caster's
///   battlefield via <see cref="Fx.ReturnFromGraveyardToBattlefield"/>
///   (ZoneService-routed when supplied so ETB triggers fire — CR 603.6a).
///   No life loss (unlike Reanimate).
/// - <b>Flashback {3}{W}</b> alt-cost via <see cref="BuildFlashbackCost"/>,
///   derived from the printed oracle text through
///   <see cref="FlashbackOracleParser"/> so the named-factory path and the
///   data-driven oracle binder path agree on cost shape. The graveyard-zone
///   gating, alt-cost-replaces-printed semantics, and post-resolve exile
///   (CR 702.34b) are all serviced by the existing
///   <see cref="FlashbackAlternativeCost"/> path — no new spell-cast plumbing.
///
/// ## Relevant rules
/// - CR 701.20 — return a card from a graveyard to the battlefield.
/// - CR 110.2 — a permanent enters under the control of the player who put
///   it onto the battlefield.
/// - CR 603.6a — ETB triggers fire on the returned creature.
/// - CR 608.2b — illegal target at resolution → no-op.
/// - CR 702.34 / 702.34b — Flashback (cast from graveyard for flashback cost,
///   then exile).
///
/// ## Deferred (v1 gaps)
/// - <b>Real targeting prompt</b>: the live cast flow supplies the chosen
///   target through <see cref="ChosenSpellParams.Targets"/>; the resolver maps
///   tokens to live cards. Same posture as <see cref="ReanimateFactory"/> /
///   <see cref="FlashbackFactory"/>.
/// </summary>
[CardName("Unburial Rites")]
public static class UnburialRitesFactory
{
    public const string CardName = "Unburial Rites";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "unburial-rites";

    /// <summary>
    /// Oracle text used by <see cref="BuildFlashbackCost"/> to derive the
    /// flashback cost via <see cref="FlashbackOracleParser"/>. Kept on the
    /// factory so the production load path (Scryfall row → oracle text →
    /// parser) and the named-factory test path bind the same cost shape.
    /// </summary>
    public const string OracleText =
        "Return target creature card from your graveyard to the battlefield.\n" +
        "Flashback {3}{W}";

    /// <summary>
    /// Materialise the Sorcery card shape (name / Sorcery / {4}{B}) from the
    /// embedded JSON definition. Resolve behaviour ("return target creature
    /// card from your graveyard") is built on demand via
    /// <see cref="BuildSpellDefinition"/>, mirroring
    /// <see cref="DreadboreFactory"/> / <see cref="AgadeemsAwakeningFactory"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the resolve-time "return target creature card from your
    /// graveyard to the battlefield" <see cref="SpellDefinition"/>. Single
    /// 1..1 target request scoped to the caster's graveyard; on resolution
    /// validates the target per CR 608.2b and returns it to the caster's
    /// battlefield.
    /// </summary>
    /// <param name="caster">Spell controller — the graveyard whose creature
    /// card is returned ("your graveyard") and the destination battlefield
    /// (CR 110.2).</param>
    /// <param name="resolver">Maps the agent-supplied raw target token to the
    /// live engine object. Pass <c>o =&gt; o</c> for tests that hand cards
    /// directly.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/> so
    /// ETB triggers on the returned creature fire (CR 603.6a).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature card from your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Reanimate,
                    // "your graveyard" — only the caster's graveyard is a
                    // legal source (CR 608.2b enforced again at resolution).
                    CandidateGatherer: _ => caster.Zones.Graveyard.GetCards()
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen => new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: return target creature card from your graveyard to the battlefield",
                    () => Resolve(caster, chosen, resolver, zoneService)),
            });
    }

    /// <summary>
    /// Resolve the return. CR 608.2b — the target must still be a creature
    /// card in the caster's graveyard; otherwise the spell does nothing.
    /// </summary>
    private static void Resolve(
        Player caster,
        ChosenSpellParams chosen,
        Func<object, object> resolver,
        ZoneService? zoneService)
    {
        if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0) return;

        var live = resolver(chosen.Targets[0][0]);

        // CR 608.2b — illegal-on-resolution checks: must be a creature card,
        // still in the graveyard, still owned by the caster ("your graveyard").
        if (live is not Creature creature) return;
        if (creature.Zone != ZoneType.Graveyard) return;
        if (!ReferenceEquals(creature.Owner, caster)) return;

        // CR 701.20 — graveyard → battlefield under the caster's control
        // (CR 110.2). ZoneService-routed when supplied so ETB triggers fire
        // (CR 603.6a). No life loss — Unburial Rites has no such clause.
        Fx.ReturnFromGraveyardToBattlefield(creature, caster, zoneService);
    }

    /// <summary>
    /// Build the Flashback {3}{W} alternative cost by running
    /// <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>.
    /// Going through the parser (rather than hard-coding the cost here) keeps
    /// the named-factory path and the data-driven oracle binder path agreeing
    /// on shape. Post-resolve exile (CR 702.34b) is handled by the cost.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Unburial Rites' oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}
