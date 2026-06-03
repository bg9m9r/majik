using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Rules;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Solitary Confinement (Judgment, {2}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall 2026-06-02):
///   "At the beginning of your upkeep, sacrifice this enchantment unless you
///    discard a card."
///   "Skip your draw step."
///   "You have shroud. (You can't be the target of spells or abilities.)"
///   "Prevent all damage that would be dealt to you."
///
/// ## Implemented (v1 — full)
/// - <b>Enchantment shape</b> {2}{W}, owner / controller.
/// - <b>"You have shroud" (CR 702.18)</b> — the controller is registered into
///   <see cref="Majik.Core.Rules.PlayerStaticAbilities"/> via a
///   <see cref="PlayerShroudEffect"/> while Solitary Confinement is on the
///   battlefield. <see cref="Majik.Core.Rules.ActionValidator"/> /
///   <see cref="Majik.Core.Targeting.TargetLegality"/> reject ALL targeting of
///   the controller (CR 702.18a — including the controller's own, unlike
///   hexproof).
/// - <b>"Prevent all damage that would be dealt to you" (CR 615)</b> —
///   registers a persistent <see cref="PreventAllDamageToPlayerShield"/> on the
///   controller's <see cref="ReplacementBus"/>. It self-gates on this
///   enchantment's zone, so combat and spell damage to the controller is
///   cancelled while it stays on the battlefield.
/// - <b>"Skip your draw step" (CR 117.5)</b> — wired against
///   <see cref="SkipDrawRegistry"/>; <see cref="Game.TurnDriver"/> suppresses
///   the controller's draw-step draw while this enchantment is on the
///   battlefield.
/// - <b>"At the beginning of your upkeep, sacrifice this enchantment unless you
///   discard a card" (CR 603.1 / 701.16)</b> — an upkeep
///   <see cref="TriggeredAbility"/>. On resolution the controller's agent is
///   prompted (yes/no) whether to discard a card; declining (or an empty hand)
///   sacrifices the enchantment. The chosen / first card is discarded
///   (hand→graveyard).
///
/// ## v1 gaps
/// - <b>Lifecycle auto-cleanup</b>: the skip-draw token + the damage shield are
///   self-gated on this enchantment's zone (no spurious effect once it leaves),
///   but the skip-draw registry entry isn't proactively removed on LTB — the
///   predicate just stops matching (mirrors <see cref="NecropotenceFactory"/>'s
///   documented LTB-cleanup gap). The shroud lifecycle DOES auto-detach via its
///   <see cref="CardMovedEvent"/> subscription.
/// </summary>
[CardName("Solitary Confinement")]
public static class SolitaryConfinementFactory
{
    public const string CardName = "Solitary Confinement";
    public const string PrintedManaCost = "{2}{W}";

    /// <summary>Shape-only build (no live registries). The shroud static still
    /// registers on Attach.</summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, replacements: null, triggers: null);

    /// <summary>
    /// Fully wired build. <paramref name="replacements"/> backs the damage
    /// shield, <paramref name="triggers"/> the upkeep sacrifice-unless-discard,
    /// and <paramref name="eventBus"/> the shroud LTB teardown. Any may be null
    /// in shape paths (the corresponding clause is skipped).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ── "You have shroud." (CR 702.18) ──────────────────────────────────
        var shroud = new PlayerShroudEffect(
            source: card,
            eventBus: eventBus,
            affectedPlayersResolver: () =>
            {
                var controller = card.Controller;
                return controller != null ? new[] { controller } : Array.Empty<Player>();
            });
        shroud.Attach();

        // ── "Prevent all damage that would be dealt to you." (CR 615) ───────
        if (replacements != null)
        {
            replacements.Register<DamageIntent>(new PreventAllDamageToPlayerShield(card));
        }

        // ── "Skip your draw step." (CR 117.5) ───────────────────────────────
        var skipToken = new object();
        SkipDrawRegistry.AddSkip(skipToken, p =>
            ReferenceEquals(p, card.Controller) && card.Zone == ZoneType.Battlefield);
        card.AddAbility(new StaticAbility(
            source: card, controller: owner,
            description: "Skip your draw step.",
            isActiveCheck: () => card.Zone == ZoneType.Battlefield,
            applyEffect: null));

        // ── Upkeep: "sacrifice this enchantment unless you discard a card."
        //    (CR 603.1 / 701.16) ──────────────────────────────────────────────
        var upkeepEffect = new Effect(
            $"{CardName}: discard a card or sacrifice",
            () => ResolveUpkeep(card, eventBus));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 603.1 — resolve the upkeep trigger: the controller may discard a card
    /// to keep Solitary Confinement; otherwise it is sacrificed. Exposed for
    /// tests / bots. The controller's agent (when registered) decides whether to
    /// discard and which card; with no agent the default is to discard the
    /// first hand card if able (keeping the enchantment), else sacrifice.
    /// </summary>
    public static void ResolveUpkeep(Enchantment card, IEventBus? eventBus)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        var controller = card.Controller;
        if (controller == null) return;

        var hand = controller.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0)
        {
            Sacrifice(card, controller, eventBus);
            return;
        }

        var agent = AgentRegistry.Get(controller);
        bool willDiscard = true;
        ICard pick = hand[0];

        if (agent != null)
        {
            willDiscard = agent
                .ChooseYesNoAsync(
                    $"{CardName}: discard a card to keep it? (otherwise it is sacrificed)",
                    BotIntent.Discard)
                .GetAwaiter().GetResult();

            if (willDiscard)
            {
                var chosen = agent
                    .ChooseFromHandAsync(controller, hand.Cast<ICard>().ToList(), BotIntent.Discard)
                    .GetAwaiter().GetResult();
                if (chosen != null && chosen.Zone == ZoneType.Hand) pick = chosen;
            }
        }

        if (!willDiscard)
        {
            Sacrifice(card, controller, eventBus);
            return;
        }

        // CR 701.8 — discard the chosen card (hand → graveyard).
        controller.Zones.Hand.RemoveCard(pick);
        controller.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }

    private static void Sacrifice(Enchantment card, Player controller, IEventBus? eventBus)
    {
        if (eventBus != null) Fx.Sacrifice(card, controller, eventBus);
        else Fx.Sacrifice(card);
    }
}
