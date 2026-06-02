using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Halimar Depths (Worldwake).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, look at the top three cards of your library,
///    then put them back in any order.
///    {T}: Add {U}."
///
/// <para>
/// Composed from existing engine primitives — the two cited analogues:
/// <list type="bullet">
/// <item><see cref="AkoumRefugeFactory"/> — the enters-tapped tapland shell.
///   The Land identity + the single <b>{T}: Add {U}</b> mana ability (CR 605.1,
///   no stack) are declared declaratively in
///   <c>Majik.Core/CardData/Cards/halimar-depths.json</c> and materialized via
///   <see cref="CardDefinitionFactory"/>; the unconditional enters-tapped
///   restriction (CR 614.1c) is registered against the supplied
///   <see cref="ReplacementBus"/> exactly as the Refuge cycle does (shape-only
///   path skips it — no bus). The production load path also matches the printed
///   "This land enters tapped." clause via
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/>.</item>
/// <item><see cref="SenseisDiviningTopFactory"/> — the
///   <b>look-at-top-three-and-reorder</b> effect. Halimar Depths fires it off an
///   enters-the-battlefield-<i>self</i> trigger (CR 603.6e) rather than from a
///   {T} activated ability, but the resolution is identical: peek up to three
///   cards off the controller's library via <see cref="ScryAction.Peek"/> and
///   apply the agent-supplied reorder via <see cref="ScryAction.Apply"/> with
///   <c>ToBottom = []</c> — Halimar Depths is reorder-only and never bottoms a
///   card, so CR 701.20 (scry) does not apply. The reorder decision is sourced
///   from the registered <see cref="IPlayerAgent"/> (same pattern as Sensei's
///   Divining Top / Ponder); the pre-agent default preserves the peeked order.
///   Short libraries (&lt; 3 cards) and empty libraries are handled — Peek
///   returns what exists; an empty peek skips the apply altogether.</item>
/// </list>
/// </para>
///
/// <para>
/// "Look at ... then put them back in any order" has no "may" clause and never
/// moves a card to another zone, so — unlike scry/surveil — there is no
/// dedicated declarative effect verb today. The ETB ability is therefore wired
/// in C# off the live <see cref="ScryAction"/> reorder primitive (mirroring the
/// SenseisDiviningTop peek path), rather than through a JSON effect spec.
/// </para>
///
/// <para>
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for the reorder choice</b>: plumbed through the registry
///   path (mirrors Sensei's Divining Top); the pre-agent default preserves the
///   peeked order. The oracle text has no "may", so resolving unconditionally is
///   structurally correct.
/// </para>
/// </summary>
[CardName("Halimar Depths")]
public static class HalimarDepthsFactory
{
    public const string Slug = "halimar-depths";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Halimar Depths owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted, no
    /// <see cref="ReplacementBus"/>; the ETB trigger is attached but not
    /// registered). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.</summary>
    public static Land Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>Construct Halimar Depths with optional runtime wiring.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB look-3-and-reorder trigger
    /// is registered so the bus surfaces it as pending when the land enters
    /// (CR 603.6e).</param>
    /// <param name="replacements">When supplied, the unconditional enters-tapped
    /// restriction (CR 614.1c) is registered against it.</param>
    public static Land Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // "This land enters tapped." — CR 614.1c. Unconditional. Shape-only
        // path (no ReplacementBus) skips registration; same posture as
        // AkoumRefugeFactory. The production load path also matches the
        // clause via EntersTappedBinder off the printed oracle text.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // "When this land enters, look at the top three cards of your
        // library, then put them back in any order." — CR 603.6e
        // (enters-the-battlefield trigger). Reorder-only look (never bottoms a
        // card), so CR 701.20 (scry) does not apply; modelled off the
        // SenseisDiviningTop peek-and-reorder path via ScryAction.
        // ----------------------------------------------------------------
        var etbLookEffect = new Effect(
            $"{land.Name}: when this enters, look at top 3 of your library, reorder",
            ctx => LookAtTopThreeAndReorderAsync(land, owner, ctx));

        var etbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbLookEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return land;
    }

    /// <summary>
    /// Peek up to three cards off the controller's library and apply the
    /// agent-supplied reorder. Mirrors the SenseisDiviningTop reorder path:
    /// ToBottom is collapsed into TopOrder defensively so any agent returning a
    /// partition still ends up putting every peeked card back on top (Halimar
    /// Depths is reorder-only — it never bottoms a card). The pre-agent default
    /// preserves the peeked order.
    /// </summary>
    private static async ValueTask LookAtTopThreeAndReorderAsync(
        Land land, Player owner, ResolutionContext ctx)
    {
        var controller = land.Controller ?? owner;

        var peeked = ScryAction.Peek(controller, 3);
        if (peeked.Count == 0) return;

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        ScryAction.ScryDecision decision;
        if (agent != null)
        {
            var agentDecision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked)
                .ConfigureAwait(false);
            if (agentDecision.ToBottom.Count > 0)
            {
                // Halimar Depths never bottoms — fold any bottom-bound picks
                // back onto the top so every peeked card returns to the top.
                var collapsed = agentDecision.TopOrder
                    .Concat(agentDecision.ToBottom)
                    .ToList();
                decision = new ScryAction.ScryDecision(
                    ToBottom: Array.Empty<ICard>(),
                    TopOrder: collapsed);
            }
            else
            {
                decision = agentDecision;
            }
        }
        else
        {
            decision = new ScryAction.ScryDecision(
                ToBottom: Array.Empty<ICard>(),
                TopOrder: peeked.ToList());
        }

        ScryAction.Apply(controller, peeked.Count, decision);
    }
}
