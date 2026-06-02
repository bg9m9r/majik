using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Oblivion Ring (Lorwyn / reprints, {2}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "When this enchantment enters, exile another target nonland permanent.
///    When this enchantment leaves the battlefield, return the exiled card to
///    the battlefield under its owner's control."
///
/// The original "O-Ring" template that <see cref="BanishingLightFactory"/>
/// (Banishing Light), Brain Maggot, Spell Queller, and Skyclave Apparition all
/// descend from — exile a permanent while the enchantment sticks; return it if
/// the enchantment leaves. Same ETB-exile / LTB-return per-source closure used
/// by those siblings, with TWO printed-text differences from Banishing Light:
///   - Targets "ANOTHER target nonland permanent" — CR 109.5 ("another"
///     excludes the source itself), with NO "an opponent controls" clause.
///     Oblivion Ring may exile ANY nonland permanent on the battlefield except
///     itself, including its own controller's permanents.
///
/// ## Shape source
/// Card identity (name, {2}{W}, Enchantment) is loaded from
/// <c>Majik.Core/CardData/Cards/oblivion-ring.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="BorderlandRangerFactory"/>. The exile-until-leaves ETB / LTB
/// pair is attached in code below (the JSON ability schema does not express
/// the cross-trigger closure that captures the exiled card).
///
/// ## Implemented (v1)
/// - <b>Enchantment {2}{W}</b>. Owner / controller wired.
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21):
///   <list type="bullet">
///     <item>Single 1..1 "another target nonland permanent"
///       <see cref="TargetRequest"/>.</item>
///     <item>On resolve: CR 608.2b legality re-check (still on the
///       battlefield, still nonland, and — CR 109.5 — not Oblivion Ring
///       itself). If legal, exile via raw zone move routed through the
///       target's owner's zones. A reference to the exiled card AND its
///       owner is captured in a per-Oblivion-Ring closure shared with the
///       LTB ability.</item>
///   </list>
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires whenever
///   Oblivion Ring moves OUT of the battlefield (any destination — covers
///   dies + bounce + flicker, matching "leaves the battlefield" wording, same
///   posture as Banishing Light / Skyclave Apparition). On resolve: if a card
///   was exiled and is still in exile, it is returned to the battlefield under
///   its owner's control (CR 110.2 — "under its owner's control" maps
///   Controller := Owner on the way back).
///
/// ## Deferred (v1 gaps)
/// - Mirrors <see cref="BanishingLightFactory"/>'s deferrals: a single
///   Oblivion Ring exiles one card per ETB resolution (printed "target" is
///   singular); the per-instance closure captures one card. Flicker creates a
///   fresh ICard identity (CR 400.7) with an empty closure, matching real MTG.
/// </summary>
[CardName("Oblivion Ring")]
public static class OblivionRingFactory
{
    public const string CardName = "Oblivion Ring";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("oblivion-ring");

    /// <summary>
    /// Construct Oblivion Ring with both triggered abilities attached to the
    /// card shape but NOT registered with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Oblivion Ring with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, both ETB and LTB
    /// abilities are registered so the bus drives them via
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // Shared closure: ETB writes, LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.21.
        //   "When this enchantment enters, exile another target nonland
        //    permanent."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{CardName}: exile another target nonland permanent (CR 701.21)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution checks.
                if (target.Zone != ZoneType.Battlefield) return;
                // CR 109.5 — "another" excludes Oblivion Ring itself.
                if (ReferenceEquals(target, card)) return;
                // Printed "nonland permanent" — lands are not legal targets.
                if (target.HasType(CardType.Land)) return;

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
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "another target nonland permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "When this enchantment leaves the battlefield, return the exiled
        //    card to the battlefield under its owner's control."
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{CardName}: return the exiled card to the battlefield under its owner's control",
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
