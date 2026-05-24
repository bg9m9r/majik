using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mystic Sanctuary (Modern Horizons 2).
///
/// Land — Island. Oracle text:
///   "{T}: Add {U}.
///    When Mystic Sanctuary enters the battlefield, if you control three
///    or more other Islands, put target instant or sorcery card from your
///    graveyard on top of your library."
///
/// ## Implemented (v1)
/// - <b>Land with <see cref="CardSubtype.Island"/> subtype</b> — so
///   downstream "is an Island" predicates (mass-bounce, Spreading Seas,
///   Harbinger of the Seas, etc.) and the ETB trigger's own island-count
///   predicate work without special-casing.
/// - <b>{T}: Add {U}</b> — single <see cref="ManaAbility"/> for blue
///   (CR 605.1 — mana abilities do not use the stack). The
///   <see cref="OracleManaBinder"/> auto-binds the subtype-derived colour
///   only for Basic lands; Mystic Sanctuary is nonbasic, so the ability
///   is declared inline (same posture as Karakas's {T}: Add {W}).
/// - <b>ETB triggered ability (CR 603.6a) with intervening-if (CR 603.4)</b>:
///   "When Mystic Sanctuary enters, if you control three or more other
///   Islands, put target instant or sorcery card from your graveyard on
///   top of your library." Wired via <see cref="Triggers.OnEnterBattlefieldSelf"/>.
///   The intervening-if checks that the controller's battlefield contains
///   ≥3 permanent cards with <see cref="CardSubtype.Island"/> other than
///   Mystic Sanctuary itself (CR 603.4 — checked at trigger time; CR 109.2
///   — "other" excludes the source). Islands includes shock lands and any
///   permanent with the Island subtype (e.g. Hallowed Fountain after
///   Spreading Seas). A 1..1 <see cref="TargetRequest"/> declares the
///   "instant or sorcery card in your graveyard" target slot. On resolution
///   the chosen card is moved Graveyard → top of Library via
///   <see cref="IZone.InsertCardAt"/>(0) (same as Mystical Tutor / Sensei's
///   Divining Top). CR 608.2b illegal-on-resolution checks gate out cards
///   that are no longer in the graveyard or aren't instants/sorceries at
///   resolution time.
///
/// ## Lifecycle — two overloads
/// The single-arg <see cref="Create(Player)"/> overload produces the
/// correct card shape — the ETB trigger is attached for shape inspection
/// but not registered with a <see cref="TriggerManager"/>. Use the
/// <see cref="Create(Player, TriggerManager?)"/> overload (or the 4-arg
/// overload) to register the trigger for bus-driven firing.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: auto-takes the action when a target was
///   supplied; agent-driven decline deferred (same posture as Snapcaster
///   Mage / Tireless Tracker / Valakut).
/// - <b>Resolution-time intervening-if recheck</b>: CR 603.4 calls for the
///   condition to be evaluated at trigger time AND on resolution.
///   <see cref="TriggeredAbility.CanBePutOnStack"/> runs it at stack-push
///   time; a second recheck at resolution is deferred.
/// - <b>Agent target legality at choose-time</b>: <see cref="TargetRequest"/>
///   carries empty <c>LegalCandidates</c> (mirrors Snapcaster). The
///   resolution guard enforces the instant/sorcery + graveyard + owner
///   checks per CR 608.2b.
/// </summary>
public static class MysticSanctuaryFactory
{
    public const string CardName = "Mystic Sanctuary";

    /// <summary>
    /// Construct Mystic Sanctuary with no runtime service wiring.
    /// The ETB trigger is attached for shape inspection but is not
    /// registered with a <see cref="TriggerManager"/>. Suitable for
    /// dispatcher path and shape-only tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Mystic Sanctuary. When <paramref name="triggers"/> is
    /// supplied the ETB recur trigger is registered for bus-driven firing.
    /// </summary>
    public static Land Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            CardName,
            supertypes: null,
            subtypes: new[] { CardSubtype.Island });
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {U} — CR 605.1: mana ability, doesn't use the stack.
        // Declared inline because OracleManaBinder only auto-binds the
        // subtype-derived colour for Basic lands.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));

        // ----------------------------------------------------------------
        // ETB triggered ability with intervening-if (CR 603.4).
        // Fires when Mystic Sanctuary enters the battlefield. The
        // intervening-if checks that the controller controls ≥3 other
        // Islands at stack-push time (CR 603.4). On resolution, the
        // chosen instant or sorcery card from the controller's graveyard
        // is placed on top of the controller's library (index 0 via
        // IZone.InsertCardAt). CR 608.2b guards applied at resolve time.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbEffect = new Effect(
            "Mystic Sanctuary: put target instant or sorcery from graveyard on top of library",
            () =>
            {
                if (etb is null) return;
                if (etb.ChosenTargets.Count == 0) return;
                if (etb.ChosenTargets[0].Count == 0) return;
                if (etb.ChosenTargets[0][0] is not Card target) return;

                // CR 608.2b — illegal-on-resolution rechecks.
                if (target.Zone != ZoneType.Graveyard) return;
                if (target.Owner is null || !ReferenceEquals(target.Owner, owner)) return;
                if (!target.HasType(CardType.Instant) && !target.HasType(CardType.Sorcery)) return;

                owner.Zones.Graveyard.RemoveCard(target);
                owner.Zones.Library.InsertCardAt(0, target);
                target.SetZone(ZoneType.Library);
            });

        etb = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbEffect },
            interveningIf: () => CountOtherIslands(owner, land) >= 3,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return land;
    }

    /// <summary>
    /// Extended overload accepting a <paramref name="islandCountProvider"/>
    /// for testing or other callers that want to supply a custom Island-count
    /// function rather than sampling <paramref name="owner"/>'s live
    /// battlefield. The island count is the number of Islands on
    /// <paramref name="owner"/>'s battlefield excluding Mystic Sanctuary itself;
    /// the trigger fires when this count is ≥3 (CR 603.4).
    /// </summary>
    public static Land Create(
        Player owner,
        TriggerManager? triggers,
        Func<ICard, int> islandCountProvider)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(islandCountProvider);

        var land = new Land(
            CardName,
            supertypes: null,
            subtypes: new[] { CardSubtype.Island });
        land.SetOwner(owner);
        land.SetController(owner);

        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));

        TriggeredAbility? etb = null;
        var etbEffect = new Effect(
            "Mystic Sanctuary: put target instant or sorcery from graveyard on top of library",
            () =>
            {
                if (etb is null) return;
                if (etb.ChosenTargets.Count == 0) return;
                if (etb.ChosenTargets[0].Count == 0) return;
                if (etb.ChosenTargets[0][0] is not Card target) return;

                if (target.Zone != ZoneType.Graveyard) return;
                if (target.Owner is null || !ReferenceEquals(target.Owner, owner)) return;
                if (!target.HasType(CardType.Instant) && !target.HasType(CardType.Sorcery)) return;

                owner.Zones.Graveyard.RemoveCard(target);
                owner.Zones.Library.InsertCardAt(0, target);
                target.SetZone(ZoneType.Library);
            });

        etb = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbEffect },
            interveningIf: () => islandCountProvider(land) >= 3,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return land;
    }

    /// <summary>
    /// Count permanents on <paramref name="controller"/>'s battlefield that
    /// have the Island subtype, excluding <paramref name="self"/> (Mystic
    /// Sanctuary itself — CR 109.2 "other"). Includes all Islands regardless
    /// of whether they are basic or nonbasic (shock lands, Hallowed Fountain
    /// after retype effects, etc.).
    /// </summary>
    private static int CountOtherIslands(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Island));
}
