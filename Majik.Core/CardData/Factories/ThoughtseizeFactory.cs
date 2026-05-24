using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thoughtseize (Lorwyn / Theros, {B}).
///
/// Sorcery. Oracle text:
///   "Target player reveals their hand. You choose a nonland card from
///    it. That player discards that card. You lose 2 life."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target player"
///   request.
/// - On resolution the target player reveals their hand; the caster
///   deterministically picks the first nonland card from it and moves
///   that card to the target's graveyard (CR 701.8 — discard).
/// - The caster loses 2 life unconditionally after the discard step
///   (CR 119.3 — even if no nonland card was present).
///
/// ## Deferred (v1 gaps)
/// - <b>Caster's choice prompt</b>: v1 picks the first nonland card
///   deterministically (mirrors <see cref="GriefFactory"/>'s ETB
///   "ThoughtseizePattern" and <see cref="CabalTherapyFactory"/>'s
///   name-selector posture). Real agent-driven card-pick deferred —
///   same queue as Connive / Liliana of the Veil / Yawgmoth.
/// - <b>Reveal event</b>: the hand reveal is observable through the
///   engine's hand state; dedicated <c>CardRevealedEvent</c> fan-out
///   for portal clients is deferred (same gap as Grief's ETB reveal).
/// </summary>
public static class ThoughtseizeFactory
{
    public const string CardName = "Thoughtseize";
    public const string PrintedManaCost = "{B}";
    public const int LifeLoss = 2;

    /// <summary>
    /// Build a Thoughtseize sorcery owned by <paramref name="owner"/>.
    /// Card shape only — see <see cref="BuildSpellDefinition"/> for the
    /// resolve-time discard + life-loss effect.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Thoughtseize is
    /// cast. Single 1..1 "target player" request; on resolution the
    /// target player's first nonland card is discarded (CR 701.8) and
    /// the caster loses 2 life (CR 119.3).
    /// </summary>
    /// <param name="caster">Spell controller — loses 2 life on
    /// resolution regardless of whether a nonland card was present.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Thoughtseize: reveal hand, discard nonland, caster loses 2 life", () =>
                    {
                        // CR 608.2b — illegal-target check (target player left
                        // the game, etc.). Guard defensively; cast-flow's own
                        // pass catches most cases.
                        if (raw is not Player targetPlayer) return;

                        // CR 701.16 — "Target player reveals their hand."
                        // Hand state is observable to all agents. A dedicated
                        // CardRevealedEvent fan-out for portal clients is
                        // deferred — same gap as GriefFactory.

                        // v1: deterministic pick — first nonland card in
                        // target's hand. Mirrors GriefFactory's ETB discard
                        // body (CR 701.8: move from hand to owner's graveyard).
                        var pick = targetPlayer.Zones.Hand.GetCards()
                            .FirstOrDefault(c => !c.HasType(CardType.Land));

                        if (pick != null)
                        {
                            targetPlayer.Zones.Hand.RemoveCard(pick);
                            targetPlayer.Zones.Graveyard.AddCard(pick);
                            pick.SetZone(ZoneType.Graveyard);
                        }

                        // CR 119.3 — caster loses 2 life unconditionally,
                        // regardless of whether a nonland card was discarded.
                        caster.LoseLife(LifeLoss);
                    }),
                };
            });
    }
}
