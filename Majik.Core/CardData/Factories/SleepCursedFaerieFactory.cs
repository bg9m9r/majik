using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sleep-Cursed Faerie (Wilds of Eldraine, {U}).
///
/// Creature — Faerie Wizard 3/3. Oracle text (Scryfall, verified 2026-06-14):
///   "Flying, ward {2}
///    This creature enters tapped with three stun counters on it. (If it would
///    become untapped, remove a stun counter from it instead.)
///    {1}{U}: Untap this creature."
///
/// The base shape (name / Creature — Faerie Wizard / {U} / 3/3) is materialised
/// from the embedded JSON definition (<c>sleep-cursed-faerie.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The keyword markers, the
/// enters-tapped-with-stun-counters ETB trigger, and the {1}{U} untap ability
/// are layered on here — the JSON ability schema expresses none of these (same
/// posture as <see cref="FloodpitsDrownerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>3/3 Creature — Faerie Wizard, {U}</b>, owner / controller wired.
/// - <b>Flying (CR 702.9) + Ward {2} (CR 702.21)</b> attached as
///   <see cref="KeywordAbility"/> markers (the NamedCardFactory path does not
///   run the KeywordBinder, so attach inline — same wiring as
///   <see cref="KappaCannoneerFactory"/>'s Ward marker). Ward's
///   battlefield-attached counter trigger is not yet a primitive; the marker is
///   structural-only, matching Kappa Cannoneer's posture.
/// - <b>ETB trigger (CR 603.6a)</b>, fired by <see cref="CardMovedEvent"/> into
///   <see cref="ZoneType.Battlefield"/>: "this creature enters tapped with
///   three stun counters on it." On resolution it taps itself (CR 701.20) and
///   places three <see cref="CounterType.Stun"/> counters on it (CR 122.1c).
///   The stun counters are honoured by the untap-step replacement in
///   <c>TurnDriver.UntapStep</c> (CR 122.1g) — exactly the same source of
///   truth Floodpits Drowner's stun counter reads.
/// - <b>{1}{U} activated ability (CR 602): "Untap this creature."</b> Its
///   effect routes through <see cref="StunAwareUntap"/>: per CR 122.1g a stun
///   counter is a replacement for ANY untap event (not just the untap step), so
///   if the Faerie has a stun counter the activation removes one stun counter
///   instead of untapping. Once the stun counters are gone the activation
///   untaps normally. This is the card's whole point — pay {1}{U} repeatedly to
///   burn through the three stun counters faster than the once-per-turn untap
///   step would.
///
/// ## Deferred (v1 gaps)
/// - <b>Ward {2} trigger wiring</b>: same deferral as Kappa Cannoneer — the
///   marker is attached but the spell/ability-targeting consultation that taxes
///   an opponent {2} is not yet plumbed onto a battlefield trigger primitive.
/// </summary>
[CardName("Sleep-Cursed Faerie")]
public static class SleepCursedFaerieFactory
{
    public const string CardName = "Sleep-Cursed Faerie";
    public const string Slug = "sleep-cursed-faerie";
    public const string ActivatedManaCost = "{1}{U}";

    /// <summary>CR 702.21 — printed Ward cost: {2}.</summary>
    public const string WardCost = "{2}";

    /// <summary>The Faerie enters with this many stun counters (CR 122.1g).</summary>
    public const int StunCountersOnEntry = 3;

    private const string FlyingKeyword = "Flying";
    private const string WardKeyword = "Ward";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Sleep-Cursed Faerie owned and controlled by
    /// <paramref name="owner"/>. The base shape is materialised from the
    /// embedded JSON definition; the keyword markers, ETB trigger, and {1}{U}
    /// untap ability are layered on here. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.9 (Flying) + CR 702.21 (Ward {2}) keyword markers.
        card.AddAbility(new KeywordAbility(FlyingKeyword, card, owner));
        card.AddAbility(new KeywordAbility(WardKeyword, card, owner));

        // ETB trigger (CR 603.6a) — enters tapped with three stun counters.
        card.AddAbility(BuildEtbTrigger(card, owner));

        // {1}{U} activated ability (CR 602) — "Untap this creature."
        card.AddAbility(BuildUntapAbility(card, owner));

        return card;
    }

    // --- ETB: enters tapped with three stun counters -----------------------

    private static TriggeredAbility BuildEtbTrigger(Creature card, Player owner)
    {
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Sleep-Cursed Faerie — enters tapped with three stun counters",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                // CR 701.20 — enters tapped. Tap() throws if already tapped, so
                // only tap when currently untapped (idempotent on re-entry).
                if (!card.IsTapped) card.Tap();

                // CR 122.1c / 122.1g — three stun counters. The untap-step
                // replacement in TurnDriver.UntapStep removes one per untap.
                card.Counters.Add(CounterType.Stun, StunCountersOnEntry);
            });

        return new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });
    }

    // --- {1}{U}: Untap this creature ---------------------------------------

    private static ActivatedAbility BuildUntapAbility(Creature card, Player owner)
    {
        var untapEffect = new Effect(
            "Sleep-Cursed Faerie — untap this creature",
            () => StunAwareUntap(card));

        return new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivatedManaCost) },
            effects: new IEffect[] { untapEffect });
    }

    /// <summary>
    /// CR 122.1g — a stun counter is a replacement for ANY untap event, not
    /// only the untap step. So "Untap this creature" first consults the stun
    /// counters: if the Faerie has at least one, remove a single stun counter
    /// instead of untapping (it stays tapped); only once they are all gone does
    /// the activation actually untap it. Mirrors the replacement applied in
    /// <c>TurnDriver.UntapStep</c> so the activated untap and the untap-step
    /// untap behave identically.
    /// </summary>
    private static void StunAwareUntap(Creature card)
    {
        if (card.Zone != ZoneType.Battlefield) return;

        if (card.Counters.Count(CounterType.Stun) > 0)
        {
            card.Counters.Remove(CounterType.Stun, 1);
            return;
        }

        // Untap() throws when already untapped — guard the no-op case.
        if (card.IsTapped) card.Untap();
    }
}
