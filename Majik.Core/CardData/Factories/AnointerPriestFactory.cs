using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Anointer Priest (Amonkhet, {1}{W}).
///
/// Creature — Human Cleric 1/3. Oracle text:
///   "Whenever a creature token you control enters, you gain 1 life.
///    Embalm {3}{W} ({3}{W}, Exile this card from your graveyard:
///    Create a token that's a copy of it, except it's a white Zombie
///    Human Cleric with no mana cost. Embalm only as a sorcery.)"
///
/// ## Implemented (v1)
/// - 1/3 Creature — Human Cleric, mana cost {1}{W}.
/// - <b>Creature-token-ETB lifegain trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>
///   that fires when the entering card lands on the battlefield, is a
///   token (<see cref="Permanent.IsToken"/> — same probe Bridge from Below
///   uses), has the <see cref="CardType.Creature"/> type, and is controlled
///   by Anointer Priest's controller. Effect: caster gains 1 life via
///   <see cref="Player.GainLife"/> (CR 119). Mirrors
///   <see cref="SythisHarvestsHandFactory"/>'s constellation shape, swapping
///   the type predicate from "enchantment" to "creature token".
/// - <b>Embalm {3}{W}</b>: <see cref="KeywordAbility"/>("Embalm") marker
///   only — the alt-cost graveyard-cast that produces a white Zombie Human
///   Cleric token copy (CR 702.130) has no engine-wide primitive yet
///   (no `EmbalmAlternativeCost`, no copy-as-token-with-overrides path),
///   so the keyword scan surface is preserved without the runtime gate.
///   Same posture as <see cref="RagavanNimblePilfererFactory"/>'s deferred
///   Dash and <see cref="LotusBloomFactory"/>'s Suspend stub-for-keyword.
///
/// ## Deferred (v1 gaps)
/// - <b>Embalm alternative cost + copy-as-token</b> (CR 702.130) — needs an
///   `EmbalmAlternativeCost` (graveyard-cast + sorcery-speed + exile-on-pay)
///   plus a copy-into-token primitive that overrides `Colors → {W}`,
///   `Supertypes → {}`, `Subtypes += Zombie`, and `ManaCost → empty`.
///   No card in the corpus has needed this path before; surface it when a
///   second Embalm card lands.
/// - <b>Triggered-vs-replacement interaction with Anointed Procession</b>
///   — Procession is itself unshipped (no token-doubler primitive). When
///   Procession ships, this trigger fires once per token created by the
///   replacement (CR 603.3a — events trigger per token), so the doubled
///   count yields doubled lifegain naturally.
/// </summary>
[CardName("Anointer Priest")]
public static class AnointerPriestFactory
{
    public const string CardName = "Anointer Priest";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Anointer Priest with no live trigger-manager wiring.
    /// The lifegain trigger is attached to the card for shape observability
    /// but is not registered with the bus. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Anointer Priest with an optional <see cref="TriggerManager"/>.
    /// When supplied, the creature-token-ETB lifegain trigger is registered
    /// so the bus surfaces it as pending on a matching
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Lifegain trigger — CR 603.1.
        //   "Whenever a creature token you control enters, you gain 1
        //    life."
        // Predicate gates on:
        //   - entering the battlefield (ToZone),
        //   - the moving card being a token (Permanent.IsToken — same
        //     probe Bridge from Below uses; non-Permanent cards never
        //     return true here),
        //   - the moving card carrying the Creature type (CR 302.1),
        //   - the moving card's Controller equalling Anointer Priest's
        //     controller (CR 603.1 "you").
        // The current Card.Controller is the post-move controller, which
        // is the correct reading per CR 603.6d (ETB triggers see the
        // post-ETB game state).
        // ----------------------------------------------------------------
        var lifegainCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card is Permanent perm
            && perm.IsToken
            && e.Card.HasType(CardType.Creature)
            && ReferenceEquals(e.Card.Controller, owner));

        var lifegainEffect = new Effect(
            $"{CardName}: gain 1 life (creature token you control entered)",
            () =>
            {
                // CR 119 — life-total gain. The controller is the
                // resolving-time controller (CR 603.6d), which for a static
                // creature ability equals the card's current Controller.
                var controller = card.Controller ?? owner;
                controller.GainLife(1);
            });

        var lifegainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: lifegainCondition,
            effects: new IEffect[] { lifegainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(lifegainTrigger);
        triggers?.RegisterTriggeredAbility(lifegainTrigger);

        // ----------------------------------------------------------------
        // Embalm {3}{W} — keyword marker (CR 702.130). Runtime alt-cost
        // graveyard-cast + copy-as-token is deferred; the marker keeps
        // the keyword-scan surface uniform. Mirrors Dash on Ragavan and
        // Suspend on Lotus Bloom: the keyword is visible to scanners
        // even though the alt-cost machinery is not yet plumbed.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Embalm", card, owner));

        return card;
    }
}
