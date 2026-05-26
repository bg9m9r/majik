using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aetherworks Marvel (Kaladesh, {4}).
///
/// Legendary Artifact. Oracle text:
///   "Whenever a permanent you control is put into a graveyard, you get
///    {E} (an energy counter).
///    {T}, Pay {E}{E}{E}{E}{E}{E}: Look at the top six cards of your
///    library. You may cast a spell from among them without paying its
///    mana cost. Put the rest on the bottom of your library in a random
///    order."
///
/// Modern + Standard energy mainstay — the engine of the
/// "Marvel-into-Ulamog" combo line and a cornerstone of the Boros/
/// Temur Energy substrate. v1 ships the energy-gain triggered ability
/// (Marvel's input-side of the engine) — the {T} + 6-energy activated
/// "Marvel into a free spell" output is deferred pending a cast-from-
/// library-without-paying-mana primitive (see "Deferred (v1 gaps)" below).
///
/// ## Implemented (v1)
///
/// - Legendary Artifact, mana cost {4}. Owner / controller wired;
///   <see cref="CardSupertype.Legendary"/> stamped so the Legend Rule
///   (CR 704.5j) applies.
/// - <b>"Whenever a permanent you control is put into a graveyard, you
///   get {E}"</b> (CR 603.6a + CR 106.13): wired via an inline
///   <see cref="EventTriggerCondition{CardMovedEvent}"/> with these
///   predicates:
///     - <c>e.FromZone == Battlefield</c> AND <c>e.ToZone == Graveyard</c>
///       (CR 603.10 — "is put into a graveyard from the battlefield"
///       reading; Marvel's printed wording is "is put into a graveyard"
///       without an explicit FROM, but CR 603.6d ledgers a permanent's
///       death as the battlefield→graveyard zone change. Stamping
///       FromZone == Battlefield matches the printed shape).
///     - <c>e.Card.HasType(Permanent-shaped type)</c> — the entering
///       graveyard card must have been a permanent on the battlefield
///       (CR 110.4 — permanents are artifacts, creatures, enchantments,
///       lands, planeswalkers, battles). Predicate calls
///       <see cref="IsPermanentType"/>.
///     - <c>e.Card.Controller == Marvel's controller</c> (CR 603.10b —
///       LKI on the controller field at the time of the zone change).
///   On resolution: <see cref="Player.GainEnergy"/>(1) on Marvel's
///   controller (CR 106.13b — energy is a player-scoped resource).
/// - Marvel's own death also feeds its trigger: the printed "a permanent
///   you control" wording does not exclude Marvel itself (no "another"
///   rider). When Marvel hits the graveyard via destruction or sacrifice,
///   the trigger fires once (LKI controller match), nets the controller
///   one final energy, then Marvel sits in the graveyard inert. Same
///   posture as Bridge from Below's self-inclusive trigger.
/// - The trigger's <c>activeZones</c> spans Battlefield + Graveyard so
///   the self-death case still fires after Marvel has already moved
///   (mirrors <see cref="NihilSpellbombFactory"/>'s sacrifice trigger
///   reading from the graveyard).
///
/// ## Deferred (v1 gaps)
///
/// - <b>{T}, Pay {E}{E}{E}{E}{E}{E}: Look at top 6 → may cast a spell
///   without paying its mana cost → rest to bottom in random order</b>.
///   Three primitives are missing for a fully wired implementation:
///     1. A library-peek-N + agent prompt (Sleight of Hand has a 2-card
///        variant; scaling to 6 is straightforward when the activated
///        ability surface lands).
///     2. <em>Cast a spell from a non-hand zone without paying its mana
///        cost</em> — analogous to Cascade / Bloodbraid / Crashing
///        Footfalls, but Marvel's activated ability is the activator,
///        not a cast-trigger rider, so the existing cascade plumbing
///        doesn't transfer cleanly. A generic
///        <c>SpellCastFlow.CastFromZoneWithoutPaying</c> entry point
///        would close this gap (also unblocks Pyromancer Ascension /
///        Lurrus reanimator / Through the Breach's cast-from-hand
///        sibling). Tracked as the canonical follow-up.
///     3. "Put the rest on the bottom of your library in a random
///        order" — currently no first-class "put N cards on bottom in
///        random order" library helper. CR 701.21a defines the
///        operation; a <c>LibraryShuffle.PutOnBottomRandom</c> sibling
///        of the existing <c>ShuffleLibrary</c> would suffice.
///   Until those land, Marvel ships as "energy producer only"
///   (matches the brief's explicit fallback posture).
/// - <b>Live TriggerManager wiring</b>: the single-arg
///   <see cref="Create(Player)"/> attaches the death trigger
///   structurally but does NOT register it with a
///   <see cref="TriggerManager"/>. A 2-arg
///   <c>Create(owner, triggers)</c> overload follows the
///   <see cref="AetherHubFactory.Create(Player, IEventBus?, TriggerManager?)"/>
///   posture when bus-driven firing is needed.
/// </summary>
[CardName("Aetherworks Marvel")]
public static class AetherworksMarvelFactory
{
    public const string CardName = "Aetherworks Marvel";
    public const string PrintedManaCost = "{4}";
    public const int EnergyPerDeath = 1;
    public const int ActivatedEnergyCost = 6;

    /// <summary>
    /// Construct Aetherworks Marvel — a {4} Legendary Artifact with the
    /// "permanent you control dies → gain energy" death trigger
    /// attached. The trigger fires for Marvel's own death too (no
    /// "another" rider). Single-arg dispatcher path; no
    /// <see cref="TriggerManager"/> wiring.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: PrintedManaCost,
            supertypes: new[] { CardSupertype.Legendary });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Triggered ability — CR 603.6a + CR 106.13.
        //   "Whenever a permanent you control is put into a graveyard,
        //    you get {E}."
        //
        // CardMovedEvent filter:
        //   - FromZone == Battlefield (CR 603.10 — "is put into a
        //     graveyard FROM the battlefield" is the canonical permanent-
        //     death reading; covers destruction, sacrifice, lethal
        //     damage SBA, etc.)
        //   - ToZone == Graveyard
        //   - Card is a permanent type (CR 110.4)
        //   - Card.Controller == Marvel's controller (CR 603.10b — LKI
        //     at the moment of the zone change)
        //
        // Includes Marvel's own death (no "another" rider). The
        // activeZones span Battlefield + Graveyard so the self-death
        // case fires after Marvel has already transitioned.
        // ----------------------------------------------------------------
        var deathCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) =>
            {
                if (e.FromZone != ZoneType.Battlefield) return false;
                if (e.ToZone != ZoneType.Graveyard) return false;
                if (!IsPermanentType(e.Card)) return false;
                return ReferenceEquals(e.Card.Controller, card.Controller);
            });

        var deathEffect = new Effect(
            $"{CardName}: controller gains {{E}} (a permanent died)",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainEnergy(EnergyPerDeath);
            });

        var deathTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: deathCondition,
            effects: new IEffect[] { deathEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(deathTrigger);

        return card;
    }

    /// <summary>
    /// CR 110.4 — permanent-type predicate. Returns true when the card
    /// is an Artifact, Creature, Enchantment, Land, or Planeswalker.
    /// Used by the death trigger's CardMovedEvent filter to reject
    /// non-permanent cards (instants / sorceries) hitting the graveyard
    /// (those go via the Stack→Graveyard path, never
    /// Battlefield→Graveyard, but the explicit type check guards
    /// against future event-shape changes). Battle support is
    /// out-of-scope at v1 — no <see cref="CardType.Battle"/> enum
    /// member ships yet.
    /// </summary>
    private static bool IsPermanentType(ICard card)
    {
        return card.HasType(CardType.Artifact)
            || card.HasType(CardType.Creature)
            || card.HasType(CardType.Enchantment)
            || card.HasType(CardType.Land)
            || card.HasType(CardType.Planeswalker);
    }
}
