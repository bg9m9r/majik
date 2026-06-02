using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Worldspine Wurm (Return to Ravnica, {8}{G}{G}{G}).
/// Creature — Wurm 15/15. Oracle text (verified against Scryfall):
///   "Trample
///    When this creature dies, create three 5/5 green Wurm creature tokens
///    with trample.
///    When Worldspine Wurm is put into a graveyard from anywhere, shuffle it
///    into its owner's library."
///
/// The card's base shape (name, Wurm subtype, {8}{G}{G}{G}, 15/15) is
/// materialised from the embedded JSON definition
/// (<c>worldspine-wurm.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (Trample, dies-makes-tokens, graveyard-shuffle-self) are layered on here —
/// the JSON <c>AbilityDefinition</c> schema doesn't express keyword markers,
/// dies triggers, or put-into-graveyard triggers, so they live in the factory
/// (same posture as <see cref="KozilekButcherOfTruthFactory"/>).
///
/// ## Implemented (v1)
/// - <b>15/15 Creature — Wurm at {8}{G}{G}{G}</b> (mana value 11, green —
///   CR 105.2c, three green pips).
/// - <b>Trample (CR 702.19)</b>: <see cref="KeywordAbility"/>("Trample")
///   marker — combat code reads via
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>, same wiring
///   shape as every other named trampler (Autochthon Wurm, etc.).
/// - <b>Dies trigger (CR 603.6c / CR 700.4)</b>: "When this creature dies,
///   create three 5/5 green Wurm creature tokens with trample." Filtered to a
///   Battlefield → Graveyard <see cref="CardMovedEvent"/> for this card
///   ("dies" = battlefield → graveyard, CR 700.4 — unlike the separate "from
///   anywhere" shuffle trigger, we gate on FromZone == Battlefield). On
///   resolution, three 5/5 green Wurm tokens with Trample are minted for the
///   controller via <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111 /
///   CR 111.4 / CR 702.19), routed through the token-doubler bus when a
///   <see cref="ZoneService"/> is supplied (CR 614 — Doubling Season /
///   Parallel Lives / Anointed Procession).
/// - <b>"Put into a graveyard from anywhere" trigger (CR 603.6c /
///   CR 603.6d)</b>: "When Worldspine Wurm is put into a graveyard from
///   anywhere, shuffle it into its owner's library." Filtered to a
///   <see cref="CardMovedEvent"/> with ToZone == Graveyard for this card; the
///   origin zone is unconstrained ("from anywhere"), so unlike the dies
///   trigger we do NOT gate on FromZone. On resolution the Wurm itself (and
///   only itself — not the rest of the graveyard) is moved Graveyard →
///   Library and the owner's library is shuffled via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20). This cribs the
///   graveyard-recycle shape from <see cref="KozilekButcherOfTruthFactory"/>,
///   but narrowed to the single card rather than the whole graveyard.
///
/// ## Active zones
/// - Dies trigger: Battlefield + Graveyard — <see cref="ZoneService"/> stamps
///   <c>card.Zone = Graveyard</c> before publishing the
///   <see cref="CardMovedEvent"/>, so the trigger must still be observable in
///   the Graveyard zone at evaluation time (same posture as Doomed Traveler /
///   Aven Fisher / Young Wolf).
/// - Shuffle trigger: every zone — "from anywhere" means the prior zone is
///   irrelevant and the card's Zone is already Graveyard at fire time, so we
///   list every zone so the active-zone guard never suppresses a legitimate
///   graveyard arrival (mirrors Kozilek).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Trample marker + both
///   triggers are attached; nothing registers with a
///   <see cref="TriggerManager"/>. Suitable for dispatcher / structural
///   tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired. Both triggers register with the bus so the corresponding
///   Battlefield → Graveyard / →Graveyard <see cref="CardMovedEvent"/>s place
///   the abilities on the stack automatically (CR 603.2); the token ETB
///   threads through the <see cref="ZoneService"/> so token doublers + ETB
///   observers see the Wurm tokens enter.
///
/// ## Rules reference
/// - CR 603.6c / CR 603.6d — triggered-ability trigger conditions; the
///   "leaves-the-battlefield" / graveyard look-back posture.
/// - CR 700.4 — "dies" means moved from the battlefield to the graveyard.
/// - CR 111 / CR 111.4 — tokens are created on the battlefield under the
///   controller's control; colour identity is stamped explicitly.
/// - CR 702.19 — Trample keyword ability.
/// - CR 701.20 — Shuffle.
/// </summary>
[CardName("Worldspine Wurm")]
public static class WorldspineWurmFactory
{
    public const string CardName = "Worldspine Wurm";
    public const string Slug = "worldspine-wurm";
    public const int Power = 15;
    public const int Toughness = 15;

    /// <summary>Number of Wurm tokens created by the dies trigger.</summary>
    public const int TokenCount = 3;

    /// <summary>Printed power/toughness of each Wurm token.</summary>
    public const int TokenPower = 5;
    public const int TokenToughness = 5;

    /// <summary>
    /// Construct Worldspine Wurm with no live wiring. The Trample marker and
    /// both triggers are attached for shape; nothing registers with any
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / structural
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Worldspine Wurm with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, both the dies trigger and the
    /// graveyard-shuffle trigger register with the bus so the corresponding
    /// <see cref="CardMovedEvent"/>s automatically place the abilities on the
    /// stack (CR 603.2).</param>
    /// <param name="zoneService">When supplied, the Wurm-token ETB routes
    /// through the <see cref="ZoneService"/> so a <see cref="CardMovedEvent"/>
    /// fires (ETB observers like Soul Warden see the tokens enter) and token
    /// doublers can rewrite the count (CR 614).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Wurm
        // subtype, {8}{G}{G}{G}, 15/15). The JSON carries no abilities — the
        // Trample marker + dies trigger + graveyard-shuffle are layered on
        // below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.19 — Trample marker. CombatAbilities.HasTrample reads the
        // marker; the marker also keeps the keyword scan surface uniform.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c / CR 700.4.
        //   "When this creature dies, create three 5/5 green Wurm creature
        //    tokens with trample."
        // "Dies" = Battlefield → Graveyard (CR 700.4), so we gate the
        // CardMovedEvent on FromZone == Battlefield && ToZone == Graveyard.
        // Active in Battlefield + Graveyard because ZoneService stamps
        // card.Zone = Graveyard before publishing the event (same posture as
        // Doomed Traveler / Young Wolf).
        // ----------------------------------------------------------------
        var diesCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield
                      && e.ToZone == ZoneType.Graveyard);

        var diesEffect = new Effect(
            $"{CardName} dies: create three 5/5 green Wurm tokens with trample",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 111 / CR 111.4 / CR 702.19 — three 5/5 green Wurm tokens
                // with Trample. Routed through the token-doubler bus (CR 614)
                // when a ZoneService is supplied; falls back to a plain N-mint
                // loop otherwise.
                var spec = new TokenFactory.TokenSpec(
                    Name: "Wurm",
                    Power: TokenPower,
                    Toughness: TokenToughness,
                    Subtypes: new[] { CardSubtype.Wurm },
                    Keywords: new[] { "Trample" },
                    Colors: new[] { ManaColor.Green });

                TokenFactory.CreateOnBattlefield(
                    spec, controller, TokenCount, zoneService, zoneService?.Replacements);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: diesCondition,
            effects: new IEffect[] { diesEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        // ----------------------------------------------------------------
        // "Put into a graveyard from anywhere" trigger — CR 603.6c / 603.6d.
        //   "When Worldspine Wurm is put into a graveyard from anywhere,
        //    shuffle it into its owner's library."
        // CardMovedEvent filtered to ToZone == Graveyard for this card; the
        // origin zone is unconstrained ("from anywhere"), so unlike the dies
        // trigger we do NOT gate on FromZone. ActiveZones spans every zone
        // because the card's Zone has already been stamped to Graveyard by
        // ZoneService before the event publishes (CR 603.6d — look-back).
        // Unlike Kozilek (which recycles the whole graveyard), this shuffles
        // only the Wurm itself into the OWNER's library.
        // ----------------------------------------------------------------
        var shuffleCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.ToZone == ZoneType.Graveyard);

        var shuffleEffect = new Effect(
            $"{CardName}: shuffle it into its owner's library",
            () =>
            {
                // CR 701.20 — move the Wurm itself (and only itself) from the
                // graveyard to the owner's library, then shuffle. Guard the
                // remove so a stray fire (card already elsewhere) is a no-op.
                if (owner.Zones.Graveyard.GetCards().Contains(card))
                {
                    owner.Zones.Graveyard.RemoveCard(card);
                }
                owner.Zones.Library.AddCard(card);
                card.SetZone(ZoneType.Library);

                // Shared shuffle hook — Fisher-Yates with the registered
                // GameRandom + a LibraryShuffledEvent publish (CR 701.20).
                LibraryShuffle.ShuffleLibrary(owner, $"{CardName} shuffle into library");
            });

        var shuffleTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: shuffleCondition,
            effects: new IEffect[] { shuffleEffect },
            // "from anywhere" — list every zone so the active-zone guard never
            // suppresses a legitimate graveyard arrival regardless of origin.
            activeZones: new[]
            {
                ZoneType.Library, ZoneType.Hand, ZoneType.Battlefield,
                ZoneType.Graveyard, ZoneType.Exile, ZoneType.Stack,
                ZoneType.Command,
            });

        card.AddAbility(shuffleTrigger);
        triggers?.RegisterTriggeredAbility(shuffleTrigger);

        return card;
    }
}
