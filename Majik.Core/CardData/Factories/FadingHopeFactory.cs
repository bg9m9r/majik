using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fading Hope (Zendikar Rising, {U}).
///
/// Instant. Oracle text:
///   "Return target creature to its owner's hand. If its mana value was 3 or
///    less, scry 1. (Look at the top card of your library. You may put that
///    card on the bottom.)"
///
/// The plain bounce (<see cref="UnsummonFactory"/>) plus a conditional scry-1
/// rider gated on the bounced creature's mana value. "was 3 or less" is past
/// tense — CR 608.2g: the engine uses the creature's last-known mana value as
/// it existed on the battlefield immediately before it left, so we capture the
/// mana value BEFORE the zone move and gate the scry on it.
///
/// ## Implemented (v1)
/// - Instant {U}, owner/controller wired.
/// - <see cref="BuildDefinition"/> wires the resolve effect:
///   1. Reads target from ChosenSpellParams (1..1 "target creature").
///   2. CR 608.2b — if the target is no longer a creature on the battlefield
///      at resolution, the whole effect (bounce AND scry) does nothing.
///   3. Captures the target's mana value, then returns it to its owner's hand
///      (CR 701.10), routed through <see cref="ZoneService.MoveCard"/> when a
///      ZoneService is supplied or raw zone manipulation otherwise.
///   4. If the captured mana value was 3 or less, scry 1 for the caster via
///      the standard <see cref="ScryAction"/> pipeline — the registered
///      <see cref="IPlayerAgent"/> decides whether to bottom the peeked card;
///      with no agent registered the pre-agent default bottoms it (same
///      posture as <see cref="OptFactory"/>). An empty library short-circuits
///      the scry (peek returns nothing).
/// </summary>
[CardName("Fading Hope")]
public static class FadingHopeFactory
{
    public const string CardName = "Fading Hope";
    public const string PrintedManaCost = "{U}";

    /// <summary>
    /// Construct Fading Hope as an Instant card with owner/controller wired.
    /// Mirrors <see cref="UnsummonFactory.Create"/> / <see cref="VaporSnagFactory.Create"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "return target creature to its owner's hand; if its mana value
    /// was 3 or less, scry 1" SpellDefinition.
    /// </summary>
    /// <param name="caster">The spell's controller — the player who scries
    /// (CR 701.20 scry is performed by the spell's controller).</param>
    /// <param name="zoneService">Optional ZoneService for replacement-bus-aware
    /// zone moves. When null, raw zone manipulation is used (shape tests /
    /// dispatcher path).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Fading Hope — return target creature to its owner's hand; if its mana value was 3 or less, scry 1",
                        () => Resolve(raw, caster, zoneService)),
                };
            });
    }

    private static void Resolve(object raw, Player caster, ZoneService? zoneService)
    {
        // CR 608.2b — target must still be a creature on the battlefield, or
        // neither the bounce nor the scry happens.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        var targetOwner = target.Owner;
        if (targetOwner == null) return;

        var controller = target.Controller ?? targetOwner;

        // CR 608.2g — "If its mana value was 3 or less" is past tense; capture
        // the mana value while the creature is still on the battlefield, before
        // the bounce strips its last-known characteristics.
        var manaValue = target.ManaCostValue.TotalValue;

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

        // "If its mana value was 3 or less, scry 1." CR 701.20 — the spell's
        // controller looks at the top card and may put it on the bottom.
        // Reuse the standard ScryAction N=1 pipeline so registered agents drive
        // the decision; with no agent the pre-agent default bottoms the card
        // (same posture as OptFactory). An empty library short-circuits.
        if (manaValue <= 3)
        {
            var peeked = ScryAction.Peek(caster, 1);
            if (peeked.Count > 0)
            {
                var agent = AgentRegistry.Get(caster);
                ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    // TODO: drop sync-over-async once IEffect.Execute becomes async.
                    decision = agent.ChooseScryDecisionAsync(null, peeked)
                        .GetAwaiter().GetResult();
                }
                else
                {
                    decision = new ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                ScryAction.Apply(caster, peeked.Count, decision);
            }
        }
    }
}
