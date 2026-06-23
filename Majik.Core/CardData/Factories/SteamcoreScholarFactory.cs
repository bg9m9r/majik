using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Steamcore Scholar (Murders at Karlov Manor,
/// {2}{U}). Creature — Weird Detective 2/2. Oracle text (verified against
/// Scryfall):
///   "Flying, vigilance
///    When this creature enters, draw two cards. Then discard two cards
///    unless you discard an instant or sorcery card or a creature card with
///    flying."
///
/// A flying/vigilance-bodied "ETB draw-then-conditional-discard" creature —
/// the same ETB-draw wiring family as <see cref="CloudkinSeerFactory"/>
/// (Cloudkin Seer / Mulldrifter draw the controller cards on enter) and the
/// central discard chokepoint shared with
/// <see cref="HostileInvestigatorFactory"/> / Deep-Cavern Bat. The unique
/// twist is the "discard two cards UNLESS you discard a qualifying card"
/// rider: a single instant/sorcery card or a creature card with flying
/// satisfies the requirement on its own and you discard only that one card.
///
/// ## Implemented (v1)
/// - 2/2 Creature — Weird Detective at {2}{U} (mana value 3, blue). Base
///   shape (name, Creature type, Weird + Detective subtypes, cost, P/T) is
///   materialised from the embedded JSON definition
///   (<c>steamcore-scholar.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The JSON carries no abilities
///   — keywords + the ETB are layered on here (JSON-backed convention shared
///   with <see cref="HostileInvestigatorFactory"/> / Deep-Cavern Bat).
/// - <b>Flying (CR 702.9)</b> + <b>Vigilance (CR 702.21)</b>:
///   <see cref="KeywordAbility"/> markers read by
///   <see cref="Majik.Core.Combat.CombatAbilities"/> for evasion / non-tapping
///   in combat — same marker shape as Cloudkin Seer's Flying.
/// - <b>ETB triggered ability (CR 603.1 / CR 603.6a)</b>: "When this creature
///   enters, draw two cards. Then discard two cards unless you discard an
///   instant or sorcery card or a creature card with flying." Unconditional
///   self-ETB via <see cref="Triggers.OnEnterBattlefieldSelf"/> (no
///   intervening-if — CR 603.4 does not apply). On resolution:
///   <list type="number">
///     <item>The controller draws two cards via <see cref="Fx.DrawCards"/>
///       (count routed once through the replacement bus per CR 121.1; an empty
///       library stamps the SBA loss flag per CR 704.5b without crashing).</item>
///     <item>"Then discard two cards unless you discard [a qualifying card]"
///       (CR 701.8): if the post-draw hand holds a qualifying card — an
///       instant or sorcery card, OR a creature card with flying (CR 702.9) —
///       discarding that single card satisfies the rider, so exactly ONE card
///       is discarded. Otherwise the full two cards are discarded. Each
///       discard funnels through the central <see cref="Fx.DiscardCard"/>
///       chokepoint so "Whenever you discard a card …" / madness triggers
///       observe it.</item>
///   </list>
///
/// ## Deferred (v1 gaps — shared with the discard family)
/// - <b>Player's-choice prompt</b>: CR 701.8 — both WHICH cards to discard and
///   whether to take the "unless" out are the controller's choice. v1 picks
///   deterministically: it prefers the "unless" out (discard one qualifying
///   card) when the hand offers one, choosing the first such card; otherwise
///   it discards the first two cards in hand. Same agent-choice posture as
///   Faithless Looting / <see cref="HostileInvestigatorFactory"/>'s ETB
///   discard. The "draw two" makes the qualifying-card path the common,
///   strictly-better-for-the-controller case, so the heuristic matches the
///   printed intent.
/// </summary>
[CardName("Steamcore Scholar")]
public static class SteamcoreScholarFactory
{
    public const string CardName = "Steamcore Scholar";
    public const string Slug = "steamcore-scholar";

    /// <summary>Cards the ETB draws (CR 121.1).</summary>
    public const int DrawAmount = 2;

    /// <summary>Cards discarded when the "unless" out is not taken (CR 701.8).</summary>
    public const int DiscardAmount = 2;

    /// <summary>
    /// Construct Steamcore Scholar with no live runtime services. The ETB
    /// trigger is attached to the card for shape inspection; not registered
    /// with any <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Steamcore Scholar with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the ETB trigger is registered
    /// so a matching enter event routes it to the stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature type,
        // Weird + Detective subtypes, {2}{U}, 2/2). The JSON carries no
        // abilities — keywords + the ETB are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 / CR 702.21 — Flying + Vigilance markers. Combat-side reads
        // via CombatAbilities; keeps the keyword-scan surface uniform (Cloudkin
        // Seer / Vault Skirge shape).
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, draw two cards. Then discard two
        //    cards unless you discard an instant or sorcery card or a
        //    creature card with flying."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: draw two, then discard two unless you discard an " +
            "instant/sorcery or a creature with flying (CR 603.6a / CR 701.8)",
            () => ResolveEtb(card.Controller ?? owner));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            // CR 603.6a — ETB trigger only active while on the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Resolve the ETB: <paramref name="controller"/> draws two cards
    /// (CR 121.1), then discards two cards UNLESS a single qualifying card is
    /// discarded (CR 701.8). Exposed for direct invocation by tests.
    ///
    /// <para>v1 deterministic choice: prefer the "unless" out — discard one
    /// qualifying card (an instant/sorcery card, or a creature card with
    /// flying) when the hand offers one; otherwise discard the first two cards
    /// in hand. The discarder's full agent choice is deferred (same posture as
    /// Faithless Looting / Hostile Investigator's ETB discard).</para>
    /// </summary>
    public static void ResolveEtb(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // CR 121.1 — draw two, routed through the replacement bus + SBA flag.
        Fx.DrawCards(controller, DrawAmount);

        // CR 701.8 — "unless you discard [a qualifying card]". A single
        // qualifying discard satisfies the rider, so discard ONLY that card.
        var qualifying = controller.Zones.Hand.GetCards()
            .FirstOrDefault(IsQualifyingDiscard);

        if (qualifying is not null)
        {
            Fx.DiscardCard(controller, qualifying, wasCost: false);
            return;
        }

        // No qualifying card available → discard the full two cards.
        Fx.Discard(controller, DiscardAmount);
    }

    /// <summary>
    /// CR 701.8 — true when discarding <paramref name="card"/> satisfies the
    /// "unless" rider: it is an instant card, a sorcery card, or a creature
    /// card with flying (CR 702.9). Flying on a card in hand is read from its
    /// printed <see cref="KeywordAbility"/> markers (the same source the
    /// combat layer falls back to).
    /// </summary>
    public static bool IsQualifyingDiscard(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (card.HasType(CardType.Instant) || card.HasType(CardType.Sorcery))
        {
            return true;
        }

        // "a creature card with flying" — CR 702.9. A card in hand is not a
        // permanent, so read flying from its printed keyword markers.
        return card.HasType(CardType.Creature) && HasFlyingMarker(card);
    }

    private static bool HasFlyingMarker(ICard card) =>
        card.Abilities
            .OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Flying", StringComparison.OrdinalIgnoreCase));
}
