using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Setessan Champion (Theros Beyond Death — {1}{G}{G}).
///
/// Creature — Human Warrior 2/2. Oracle text:
///   "Constellation — Whenever Setessan Champion or another enchantment
///    enters under your control, you may pay 1 life. If you do, draw a card."
///
/// ## Implementation
///
/// Constellation (CR 702.144) — a trigger-templating keyword that fires
/// whenever an enchantment enters under the controller's control. Unlike
/// <see cref="SythisHarvestsHandFactory"/> (an enchantment-typed permanent
/// that is itself a Nymph creature on a Nyx frame), Setessan Champion is a
/// plain creature — the trigger therefore fires on (a) Setessan Champion's
/// own ETB and (b) any other enchantment entering under the controller's
/// control.
///
/// Shape mirrors <see cref="SythisHarvestsHandFactory"/> (single
/// <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>), with
/// two predicate differences:
///   * Self-ETB qualifies — predicate is
///     <c>ReferenceEquals(e.Card, card) || e.Card.HasType(CardType.Enchantment)</c>.
///   * Resolution prompts the controller via
///     <see cref="IPlayerAgent.ChooseYesNoAsync"/> with
///     <see cref="BotIntent.CardAdvantage"/>. Default-accept posture
///     matches the legacy "auto-accept may-clauses" behaviour for upside
///     prompts (the deferred draw outweighs 1 life for any non-precarious
///     life total).
///
/// Effect on accept: lose 1 life (CR 119.3) then draw a card (top of
/// controller's library → hand, matching the inline DrawOne pattern used
/// by <see cref="UpTheBeanstalkFactory"/> / <see cref="SythisHarvestsHandFactory"/>).
///
/// ## Notes
/// - "If you do" gates the draw on actually paying the life — CR 117.11.
///   When the agent declines or the controller cannot pay (life ≤ 0
///   defensive guard), no life is lost and no card is drawn.
/// - Self-ETB triggers fire because Setessan Champion is a creature whose
///   battlefield-entry constitutes "[Setessan Champion] enters under your
///   control"; constellation cares about the enchantment-typed entrant,
///   and CR 702.144 explicitly reads "[this permanent] or another
///   enchantment" for any constellation permanent.
/// - Opponent enchantment ETBs do not qualify (controller filter).
/// - The single-arg dispatcher path attaches the trigger without
///   TriggerManager wiring; pass an <see cref="IPlayerAgent"/> and a live
///   <see cref="TriggerManager"/> via the overload for end-to-end firing.
/// </summary>
[CardName("Setessan Champion")]
public static class SetessanChampionFactory
{
    public const string CardName = "Setessan Champion";
    public const string PrintedManaCost = "{1}{G}{G}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int LifeCost = 1;

    /// <summary>
    /// Construct Setessan Champion with no live trigger-manager / agent
    /// wiring. The constellation trigger is attached to the card so
    /// structural shape tests can observe it; for end-to-end firing pass
    /// a live <see cref="TriggerManager"/> and optional
    /// <see cref="IPlayerAgent"/> via the overload.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, agent: null);

    /// <summary>
    /// Construct Setessan Champion with optional trigger-manager + agent
    /// wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager. When supplied, the
    /// constellation trigger is registered so the bus surfaces it as
    /// pending.</param>
    /// <param name="agent">Agent for the "may pay 1 life" prompt
    /// (<see cref="BotIntent.CardAdvantage"/>). Null → auto-accept (legacy
    /// posture matching pre-prompt may-clause factories).</param>
    public static Creature Create(Player owner, TriggerManager? triggers, IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Constellation trigger — "Whenever Setessan Champion or another
        // enchantment enters under your control, you may pay 1 life. If
        // you do, draw a card." (CR 702.144, 603.6a, 117.11)
        // ----------------------------------------------------------------
        var constellationCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!ReferenceEquals(e.Card.Controller, owner)) return false;
            // Setessan Champion itself qualifies (self-ETB) OR any other
            // enchantment entering under controller's control.
            return ReferenceEquals(e.Card, card) || e.Card.HasType(CardType.Enchantment);
        });

        var constellationEffect = new Effect(
            $"{CardName} — may pay 1 life to draw a card on enchantment ETB",
            () =>
            {
                // "You may pay 1 life" — consult agent when wired; else
                // auto-accept (matches legacy may-clause posture for
                // upside-tagged optional actions).
                bool yes = true;
                if (agent != null)
                {
                    yes = agent.ChooseYesNoAsync(
                        $"Pay 1 life to draw a card from {CardName}?",
                        BotIntent.CardAdvantage).GetAwaiter().GetResult();
                }
                if (!yes) return;

                // CR 119.3 — life loss to pay an optional cost. Guard
                // against life total dropping below 0 from a defensive
                // posture; the engine's SBA loop is responsible for
                // ending the game when life ≤ 0 (CR 704.5a).
                owner.LoseLife(LifeCost);

                // CR 117.11 — "If you do" succeeds when the optional cost
                // was paid. Draw a card (top → hand). Mirrors the inline
                // DrawOne in SythisHarvestsHandFactory / UpTheBeanstalkFactory.
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var constellationTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: constellationCondition,
            effects: new IEffect[] { constellationEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(constellationTrigger);
        triggers?.RegisterTriggeredAbility(constellationTrigger);

        return card;
    }
}
