using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Auriok Champion (Fifth Dawn, {W}).
///
/// Creature — Human Cleric 1/1. Oracle text (verified against Scryfall):
///   "Protection from black and from red
///    Whenever another creature enters, you may gain 1 life."
///
/// ## Shape source
/// Card identity (name, {W}, 1/1, Creature — Human Cleric) is materialised
/// from the embedded JSON definition (<c>auriok-champion.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two protection riders and
/// the ETB-other-creature lifegain trigger are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express protection qualities
/// or this trigger shape, so they live in the factory (same posture as
/// <see cref="MirranCrusaderFactory"/>'s protection riders and
/// <see cref="SoulWardenFactory"/>'s lifegain trigger).
///
/// ## Implemented (v1)
/// - 1/1 Human Cleric (CR 205.3m) at {W}. Owner / controller wired.
/// - <b>Protection from black and from red (CR 702.16)</b> — two
///   <see cref="ProtectionAbility"/> markers attached directly to the
///   creature. Auriok Champion's protection is intrinsic and always-on, so
///   the markers live on the creature itself — the canonical shape that
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/> reads
///   (DEBT-A: can't be Damaged / Enchanted-Equipped / Blocked / Targeted by
///   anything black or red). Identical wiring shape to
///   <see cref="MirranCrusaderFactory"/>'s protection-from-black-and-green.
/// - <b>ETB-other-creature trigger (CR 603.6a / CR 119.3)</b>: any creature
///   other than Auriok Champion entering the battlefield (under any
///   controller — the printed trigger has no controller restriction)
///   triggers; on resolution Auriok Champion's controller gains 1 life.
///   Predicate gates on Battlefield destination + Creature type + "another"
///   (CR 603.1, self excluded). Mirrors <see cref="SoulWardenFactory"/>.
///
/// ## "you may" — optional clause (v1 simplification)
/// The oracle reads "you <b>may</b> gain 1 life". Gaining life is purely
/// beneficial with no downside, and the <see cref="TriggeredAbility"/> ctor
/// has no per-trigger "may decline" agent prompt surface (same posture as
/// <see cref="KorFirewalkerFactory"/> and the other "may"-lifegain triggers).
/// v1 always takes the gain — a rational agent never declines a free life
/// point, so it is behaviour-equivalent for every game state the engine
/// models. Wiring the optional prompt is deferred to the shared may-trigger
/// choice infrastructure.
/// </summary>
[CardName("Auriok Champion")]
public static class AuriokChampionFactory
{
    public const string CardName = "Auriok Champion";
    public const string Slug = "auriok-champion";
    public const int LifeGainAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Auriok Champion with no live <see cref="TriggerManager"/>
    /// wiring. The protection riders are static markers (always active); the
    /// lifegain trigger is attached to the card shape for structural /
    /// dispatch tests but not bus-registered.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Auriok Champion, registering the lifegain trigger with
    /// <paramref name="triggers"/> when supplied so a qualifying
    /// <see cref="CardMovedEvent"/> automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human + Cleric subtypes, {W}, 1/1). The JSON carries no abilities —
        // the riders below layer the printed behaviour on top.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.16 — Protection from black and from red. Qualities stored
        // normalised; the Rules.Protection / TargetLegality / CombatAbilities
        // helpers interpret them (DEBT-A). Same intrinsic-marker shape as
        // Mirran Crusader's protection-from-black-and-green.
        card.AddAbility(new ProtectionAbility("black"));
        card.AddAbility(new ProtectionAbility("red"));

        // ----------------------------------------------------------------
        // ETB-other-creature trigger — CR 603.6a / CR 119.3.
        //   "Whenever another creature enters, you may gain 1 life."
        // Condition: CardMovedEvent → Battlefield where the card is a
        // Creature (any controller) and is NOT Auriok Champion itself
        // ("another", CR 603.1). Controller resolved live so control-change
        // effects route the gain to the new controller. "may" auto-accepts
        // (see class doc). Active only on the battlefield.
        // ----------------------------------------------------------------
        var lifegainEffect = new Effect(
            $"{CardName}: controller gains {LifeGainAmount} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainLife(LifeGainAmount);
            });

        var etbOtherCreatureTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.ToZone == ZoneType.Battlefield
                && e.Card.HasType(CardType.Creature)
                && !ReferenceEquals(e.Card, card)),
            effects: new IEffect[] { lifegainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbOtherCreatureTrigger);
        triggers?.RegisterTriggeredAbility(etbOtherCreatureTrigger);

        return card;
    }
}
