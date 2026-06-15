using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Deep-Cavern Bat (The Lost Caverns of Ixalan,
/// {1}{B}). Creature — Bat 1/1. Oracle text (verified against Scryfall):
///   "Flying, lifelink
///    When this creature enters, look at target opponent's hand. You may
///    exile a nonland card from it until this creature leaves the
///    battlefield."
///
/// A flying/lifelink-bodied member of the "exile-a-card-from-opponent's-hand
/// on ETB / return-on-LTB" family — the same shape as
/// <see cref="BrainMaggotFactory"/> (Brain Maggot), Tidehollow Sculler, and
/// Mesmeric Fiend. The only mechanical difference from Brain Maggot is the
/// keyword shell (Flying + Lifelink instead of Enchantment-Creature) and that
/// the printed wording is "look at … hand. You may exile" (private peek +
/// optional exile) rather than "reveals their hand and you choose" — the same
/// hidden-information / caster-choice deferral applies to both (see below).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Bat at {1}{B}. Identity (types / subtypes / mana cost /
///   P-T) loaded from the embedded JSON definition
///   <c>deep-cavern-bat.json</c> via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — the JSON-backed convention
///   shared with <see cref="BloodthirstyAdversaryFactory"/>.
/// - <b>Flying (CR 702.9)</b> + <b>Lifelink (CR 702.15)</b>:
///   <see cref="KeywordAbility"/> markers; combat helpers in
///   <see cref="Majik.Core.Combat.CombatAbilities"/> read them directly (same
///   shape as <see cref="VaultSkirgeFactory"/> / Nighthawk Scavenger).
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21):
///   <list type="bullet">
///     <item>Single 1..1 "target opponent" <see cref="TargetRequest"/>. The
///       candidate gatherer (opponents only) is resolved live at
///       trigger-placement; the resolution reads
///       <see cref="TriggeredAbility.ChosenTargets"/> (CR 109.5 / CR 608.2b
///       — opponents only).</item>
///     <item>On resolve: the controller "looks at" the target opponent's
///       hand (CR 701.18 — the engine's hand state is already observable to
///       agents; the private peek is a UI concern surfaced via the outer
///       event bus). v1 then exiles the first nonland card in that hand
///       deterministically (mirrors <see cref="BrainMaggotFactory"/>'s pick;
///       caster-choice prompt deferred).</item>
///     <item>The chosen card is exiled (CR 701.21) — moved Hand → Exile via
///       the card's owner (the target opponent). A reference to the exiled
///       card is captured in a per-Bat closure shared with the LTB ability so
///       the return half can read it.</item>
///   </list>
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires whenever
///   Deep-Cavern Bat moves OUT of the battlefield (any destination — dies +
///   bounce + flicker + exile, same as Brain Maggot / Skyclave Apparition).
///   On resolve: if a card was exiled and is still in exile, it is returned
///   to its owner's hand (Exile → Hand). If no card was exiled (the target
///   had only lands, or an empty hand), the LTB no-ops cleanly.
///
/// ## Deferred (v1 gaps — shared with Brain Maggot)
/// - <b>Caster's-choice prompt</b>: CR 701.18 / CR 701.21 — "You may exile a
///   nonland card". v1 always exiles the first nonland card deterministically
///   rather than prompting the controller to pick (or to decline). Same
///   posture as <see cref="BrainMaggotFactory"/> / <see cref="GriefFactory"/>.
/// - <b>Private "look at" event</b>: a dedicated reveal/peek event for UI
///   fan-out is not synthesised by the factory shell path; the target's hand
///   state is already inspectable when a live event bus is wired at the game
///   level.
/// </summary>
[CardName("Deep-Cavern Bat")]
public static class DeepCavernBatFactory
{
    public const string CardName = "Deep-Cavern Bat";
    public const string Slug = "deep-cavern-bat";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Deep-Cavern Bat: a 1/1 Creature — Bat with Flying + Lifelink
    /// keyword markers, plus the ETB-exile and LTB-return triggered abilities
    /// (both attached to <c>card.Abilities</c>, so the production
    /// <see cref="TriggerManager"/> auto-registers them when the Bat enters —
    /// no captured services required). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.9 / CR 702.15 — Flying + Lifelink markers. Combat-side reads
        // via CombatAbilities; the marker keeps the keyword-scan surface
        // uniform (Vault Skirge / Nighthawk Scavenger shape).
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // Shared closure: ETB writes (the exiled card + its owner), LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.18 / CR 701.21.
        //   "When this creature enters, look at target opponent's hand. You
        //    may exile a nonland card from it until this creature leaves the
        //    battlefield."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: look at target opponent's hand; exile a nonland card until this leaves",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Player targetOpponent) return;

                // CR 109.5 — "target opponent" must be a player other than
                // the source's controller at resolution time.
                if (ReferenceEquals(targetOpponent, card.Controller ?? owner)) return;

                // CR 701.18 — "look at … hand" is a private peek. The engine's
                // hand state is already observable to agents; UI surfaces the
                // private look via the outer event bus.

                // v1 deterministic pick — first nonland card in the target's
                // hand. Agent-driven caster-choice ("You may exile") deferred
                // (same posture as Brain Maggot / Grief).
                var pick = targetOpponent.Zones.Hand.GetCards()
                    .FirstOrDefault(c => !c.HasType(CardType.Land));

                if (pick == null) return; // empty / land-only hand → no exile.

                // CR 701.21 — exile from hand. Routed through the target's own
                // zones (the card's owner is the target).
                targetOpponent.Zones.Hand.RemoveCard(pick);
                targetOpponent.Zones.Exile.AddCard(pick);
                pick.SetZone(ZoneType.Exile);

                exiled = pick;
                exiledOwner = targetOpponent;
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
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "… until this creature leaves the battlefield."
        // Fires whenever the Bat moves OUT of the battlefield (any
        // destination — dies + bounce + flicker + exile).
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{CardName}: return the exiled card to its owner's hand",
            () =>
            {
                if (exiled == null || exiledOwner == null) return;
                // CR 400.7 — if the exiled card has since left exile, skip.
                if (exiled.Zone != ZoneType.Exile) return;

                exiledOwner.Zones.Exile.RemoveCard(exiled);
                exiledOwner.Zones.Hand.AddCard(exiled);
                exiled.SetZone(ZoneType.Hand);
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last existed on
            // the battlefield (same "looks back" semantics as Brain Maggot).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);

        return card;
    }
}
