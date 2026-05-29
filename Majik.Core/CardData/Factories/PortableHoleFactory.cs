using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Portable Hole (Adventures in the Forgotten Realms,
/// {W}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "When this artifact enters, exile target nonland permanent an opponent
///    controls with mana value 2 or less until this artifact leaves the
///    battlefield."
///
/// The same exile-on-ETB / return-on-LTB "Oblivion Ring" template as
/// <see cref="BanishingLightFactory"/> (and Conclave Tribunal / Skyclave
/// Apparition), built on the same per-source closure that captures the exiled
/// card between the two triggered abilities. Portable Hole narrows the target
/// to an opponent's nonland permanent with mana value 2 or less (CR 202.3) and
/// is a {W} Artifact rather than an enchantment.
///
/// ## Implemented (v1)
/// - <b>Artifact {W}</b>. Owner / controller wired.
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21):
///   <list type="bullet">
///     <item>Single 1..1 "target nonland permanent an opponent controls with
///       mana value 2 or less" <see cref="TargetRequest"/>.</item>
///     <item>On resolve: CR 608.2b legality re-check (still on the battlefield,
///       still nonland, controlled by an opponent of the Portable Hole
///       controller, mana value ≤ 2). If legal, exile via raw zone move. A
///       reference to the exiled card AND its owner is captured in a
///       per-Portable-Hole closure shared with the LTB ability.</item>
///   </list>
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires whenever
///   Portable Hole moves OUT of the battlefield (any destination — covers
///   dies + bounce + flicker, matching "leaves the battlefield" wording, same
///   posture as Banishing Light / Skyclave Apparition). On resolve: if a card
///   was exiled and is still in exile, it is returned to the battlefield under
///   its owner's control (CR 110.2 — Controller := Owner on the way back).
///
/// ## Deferred (v1 gaps)
/// - <b>Legality prompt</b>: CR 601.2c target selection is supplied by the
///   caller via <see cref="TriggeredAbility.SetChosenTargets"/>; the agent
///   prompt is driven by the shared targeting subsystem on the production
///   resolution path.
/// </summary>
[CardName("Portable Hole")]
public static class PortableHoleFactory
{
    public const string CardName = "Portable Hole";
    public const string PrintedManaCost = "{W}";

    /// <summary>The printed "mana value 2 or less" cap (CR 202.3).</summary>
    public const int MaxTargetManaValue = 2;

    /// <summary>
    /// Construct Portable Hole with no runtime services. Both triggered
    /// abilities are attached to the card shape; neither is registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Portable Hole with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both ETB and LTB abilities are
    /// registered so the bus drives them via <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Artifact Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        // Shared closure: ETB writes, LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.21.
        //   "When this artifact enters, exile target nonland permanent an
        //    opponent controls with mana value 2 or less until this artifact
        //    leaves the battlefield."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{card.Name}: exile target nonland permanent an opponent controls with mv ≤ 2 (CR 701.21)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution checks.
                if (target.Zone != ZoneType.Battlefield) return;
                if (target.HasType(CardType.Land)) return;
                // CR 109.5 — must be an opponent's permanent at resolution.
                if (ReferenceEquals(target.Controller, card.Controller ?? owner)) return;
                // CR 202.3 — printed "mana value 2 or less" cap.
                if (target.ManaCostValue.TotalValue > MaxTargetManaValue) return;

                // CR 701.21 — exile (Battlefield → Exile). Routed through the
                // target's owner's zones — same posture as Banishing Light /
                // Skyclave Apparition.
                var targetOwner = target.Owner;
                if (targetOwner != null)
                {
                    targetOwner.Zones.Battlefield.RemoveCard(target);
                    targetOwner.Zones.Exile.AddCard(target);
                }
                target.SetZone(ZoneType.Exile);

                exiled = target;
                exiledOwner = targetOwner;
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonland permanent an opponent controls with mana value 2 or less",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "until this artifact leaves the battlefield" — when this leaves,
        //   return the exiled card to the battlefield under its owner's
        //   control.
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{card.Name}: return the exiled card to the battlefield under its owner's control",
            () =>
            {
                if (exiled == null || exiledOwner == null) return;
                // CR 400.7 — if the exiled card has since left exile, skip.
                if (exiled.Zone != ZoneType.Exile) return;

                exiledOwner.Zones.Exile.RemoveCard(exiled);
                exiledOwner.Zones.Battlefield.AddCard(exiled);
                exiled.SetZone(ZoneType.Battlefield);
                // CR 110.2 — "under its owner's control" maps Controller :=
                // Owner on the way back.
                if (exiled is Card returned) returned.ChangeController(exiledOwner);
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last existed on
            // the battlefield (same "looks back" semantics as Banishing Light).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return card;
    }
}
