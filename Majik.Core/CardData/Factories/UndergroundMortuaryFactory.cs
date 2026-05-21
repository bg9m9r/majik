using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Underground Mortuary (Murders at Karlov Manor).
///
/// U/B dual surveil land. Oracle text:
///   "Underground Mortuary enters tapped unless you control two or more
///    other lands.
///    When Underground Mortuary enters untapped, surveil 1.
///    {T}: Add {U} or {B}."
///
/// ## Implemented (v1)
/// - Two mana abilities: {T}: Add {U} and {T}: Add {B} (player selects at
///   activation time — two distinct <see cref="ManaAbility"/> instances,
///   matching the Treasure token pattern).
/// - ETB triggered ability: surveil 1. Default decision sends the top card
///   to the graveyard (matches the existing binder pattern for default-all-
///   graveyard surveil decisions). Real player choice via prompt is deferred.
///
/// ## Deferred (v1 gaps)
/// - <b>ETB-tapped restriction</b>: "enters tapped unless you control two or
///   more other lands" requires a replacement-effect check on enter-the-
///   battlefield that counts permanents of type Land under your control.
///   Deferred until ETB replacement-effect infrastructure is ready.
/// - <b>"If it entered untapped" gate on surveil trigger</b>: the trigger
///   should only fire when the land entered untapped. v1 always fires (no
///   tapped-state tracking on ETB at trigger evaluation time).
/// - <b>Surveil decision player prompt</b>: agent-driven via
///   <see cref="IPlayerAgent.ChooseSurveilDecisionAsync"/> when the owner's
///   agent is registered in <see cref="AgentRegistry"/>; falls back to
///   all-to-graveyard default when none is registered.
/// </summary>
public static class UndergroundMortuaryFactory
{
    /// <summary>
    /// Construct Underground Mortuary owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Underground Mortuary");
        land.SetOwner(owner);
        land.SetController(owner);

        // --------------------------------------------------------------------
        // {T}: Add {U}
        // {T}: Add {B}
        // Two separate mana abilities — player chooses which to activate.
        // CR 605.1: mana abilities do not use the stack.
        // --------------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        // --------------------------------------------------------------------
        // ETB triggered ability: surveil 1.
        // v1: fires unconditionally on ETB (untapped-entry gate deferred).
        // Default decision: send top card to graveyard (all-graveyard default).
        // CR 701.42: surveil N — look at top N, put any into graveyard, rest on top.
        // --------------------------------------------------------------------
        var surveilEffect = new Effect(
            "Underground Mortuary: surveil 1",
            () =>
            {
                var peeked = SurveilAction.Peek(owner, 1);
                if (peeked.Count == 0) return;

                // Consult the registered agent when available; fall back to the
                // pre-agent default (all-to-graveyard) when none is registered.
                // TODO: remove sync-over-async once IEffect.Execute becomes async.
                var agent = AgentRegistry.Get(owner);
                SurveilAction.SurveilDecision decision;
                if (agent != null)
                {
                    decision = agent.ChooseSurveilDecisionAsync(null, peeked)
                        .GetAwaiter().GetResult();
                }
                else
                {
                    decision = new SurveilAction.SurveilDecision(
                        ToGraveyard: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                SurveilAction.Apply(owner, 1, decision);
            });

        var etbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { surveilEffect });

        land.AddAbility(etbTrigger);

        return land;
    }
}
