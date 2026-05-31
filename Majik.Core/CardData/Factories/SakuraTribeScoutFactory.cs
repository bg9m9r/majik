using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sakura-Tribe Scout (Champions of Kamigawa, {G}).
///
/// Creature — Snake Scout 1/1. Oracle text:
///   "{T}: You may put a land card from your hand onto the battlefield."
///
/// ## Implemented (v1)
/// - 1/1 Snake Scout shape, mana cost {G}.
/// - Activated ability with <see cref="AdditionalCost.Tap"/> as the
///   sole printed cost (CR 605.1 — NOT a mana ability; the effect doesn't
///   add mana to a pool, so this still uses the stack like a normal
///   activated ability). The "{T}: …" notation is the printed cost; no
///   mana pip is in the cost line.
/// - Resolution prompts the controller's agent via
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/> (intent
///   <see cref="BotIntent.Ramp"/>) for the "you may" opt-in, then via
///   <see cref="IPlayerAgent.ChooseFromHandAsync"/> (intent
///   <see cref="BotIntent.Ramp"/>) for which land card to play. When no
///   agent is registered the activation auto-accepts and picks the first
///   land in hand deterministically (mirrors <see cref="UroTitanFactory"/>'s
///   land-from-hand fallback). Movement routes through
///   <see cref="ZoneService.MoveCard"/> when supplied so ETB triggers /
///   replacements on the played land fire (CR 603.6a) — crucial for the
///   Amulet Titan engine where Sakura-Tribe Scout drops a bounce land and
///   the bounce-land ETB trigger needs to fire on the
///   <see cref="CardMovedEvent"/>.
///
/// ## Why "put a land card onto the battlefield" is NOT a land drop
/// CR 305.2 — a player may play one land per turn, only during a main
/// phase, when the stack is empty. "Put a land onto the battlefield"
/// (printed text on Sakura-Tribe Scout, Azusa, Sylvan Awakening's land
/// drops, Knight of the Reliquary, etc.) sidesteps this rule entirely
/// (CR 305.9 / 113.6c — it's an effect that bypasses the per-turn cap +
/// timing restriction). Implementation here doesn't touch
/// <see cref="Majik.Core.Game.LandDropTracker"/>; the land enters
/// regardless of how many land drops the controller used this turn and
/// regardless of phase / stack state.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven land pick</b>: the v1 deterministic-fallback pick
///   takes the first land in hand. When an agent is registered, the
///   `ChooseFromHandAsync` candidate list is the controller's hand
///   filtered to lands — the agent chooses which one (or returns null to
///   decline). Smart bots (HeuristicBotAgent) classify the prompt under
///   <see cref="BotIntent.Ramp"/> and accept by default.
/// - <b>Per-activation tap restriction</b>: Sakura-Tribe Scout enters
///   the battlefield with summoning sickness (CR 302.1), so it can't be
///   tapped to activate its ability the turn it enters unless it has
///   Haste. This is enforced engine-side by <see cref="AdditionalCost.Tap"/>'s
///   summoning-sickness check + the action validator's untapped /
///   non-sick gate at activation time, not by this factory.
/// </summary>
[CardName("Sakura-Tribe Scout")]
public static class SakuraTribeScoutFactory
{
    public const string CardName = "Sakura-Tribe Scout";
    public const string PrintedManaCost = "{G}";

    /// <summary>
    /// Construct Sakura-Tribe Scout with no live <see cref="ZoneService"/>
    /// wiring (the shape/dispatcher path). The activated ability is
    /// attached to the card's <see cref="Card.Abilities"/> collection;
    /// when activated, the land-from-hand move falls back to raw zone
    /// manipulation — suitable for shape / unit tests. For end-to-end
    /// firing with ETB triggers / replacements on the played land, pass
    /// a <see cref="ZoneService"/> via the overload.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null);

    /// <summary>
    /// Construct Sakura-Tribe Scout with optional runtime services.
    /// When <paramref name="zoneService"/> is supplied the hand →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers + replacement effects on the played land fire
    /// (CR 603.6a, CR 614).
    /// </summary>
    public static Creature Create(Player owner, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Snake, CardSubtype.Scout });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: You may put a land card from your hand onto the battlefield.
        //
        // CR 605.1 — not a mana ability (effect doesn't add mana to a
        // pool), so it uses the stack like a normal activated ability.
        // CR 117.1a — the "you may" opt-in and the "which land" pick
        // happen at resolution, not at activation.
        // CR 305.9 / 113.6c — putting a land directly onto the
        // battlefield is NOT a land drop; bypasses CR 305.2's
        // per-turn / main-phase / empty-stack gate (handled by
        // LandDropTracker for the printed-cost land-play surface only).
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            "Sakura-Tribe Scout: put a land card from hand onto the battlefield",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // Candidate set: every land card in the controller's hand.
                var candidates = controller.Zones.Hand.GetCards()
                    .Where(c => c.HasType(CardType.Land))
                    .ToList();
                if (candidates.Count == 0) return; // No lands → "may" no-op.

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);

                // "You may" — CR 117.1a optional gesture. Smart agent path
                // via ChooseYesNoAsync(BotIntent.Ramp); no-agent fallback
                // auto-accepts (matches the v1 posture every "may" factory
                // uses pre-prompt — Sneak Attack / Through the Breach /
                // Stoneforge Mystic).
                if (agent != null)
                {
                    var optIn = (await agent.ChooseYesNoAsync(
                            "Put a land card from your hand onto the battlefield?",
                            BotIntent.Ramp).ConfigureAwait(false));
                    if (!optIn) return;
                }

                // Pick which land. Agent-driven via ChooseFromHandAsync
                // with candidates pre-filtered to lands; no-agent fallback
                // takes the first deterministically (mirrors Uro / Stoneforge).
                ICard? land;
                if (agent != null)
                {
                    land = (await agent.ChooseFromHandAsync(controller, candidates, BotIntent.Ramp).ConfigureAwait(false));
                    // Re-validate at resolution (CR 608.2b).
                    if (land == null || !candidates.Contains(land))
                    {
                        return; // Agent declined / illegal pick → no-op.
                    }
                }
                else
                {
                    land = candidates[0];
                }

                // Hand → battlefield. Prefer ZoneService so ETB triggers
                // + replacements on the played land fire (CR 603.6a /
                // CR 614 — Amulet of Vigor / bounce-land ETB bounce
                // triggers / Lotus Cobra all need the
                // CardMovedEvent publication that ZoneService.MoveCard
                // emits). Raw zone manipulation fallback for shape tests.
                if (zoneService != null)
                {
                    zoneService.MoveCard(land, ZoneType.Hand, ZoneType.Battlefield, controller);
                }
                else
                {
                    controller.Zones.Hand.RemoveCard(land);
                    controller.Zones.Battlefield.AddCard(land);
                    land.SetZone(ZoneType.Battlefield);
                    land.SetController(controller);
                }
            });

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }
}
