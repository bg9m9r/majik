using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mortuary Mire (Battle for Zendikar).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you may put target creature card from your
///    graveyard on top of your library.
///    {T}: Add {B}."
///
/// <para>
/// Same shape as the black member of the "creature-recur" ETB lands, and
/// mechanically the graveyard-recursion sibling of
/// <see cref="MysticSanctuaryFactory"/> (which recurs an instant/sorcery to
/// the top of the library). Differences from Mystic Sanctuary: no land
/// subtype, an <b>unconditional</b> ETB (no intervening-if Island count),
/// a <b>"you may"</b> optional action (CR 603.5), and a <b>creature</b>-card
/// target instead of instant/sorcery.
/// </para>
///
/// <para>
/// The card body — name, Land type, and the single {T}: Add {B} mana ability
/// (CR 605.1 — mana abilities don't use the stack) — is declared declaratively
/// in <c>Majik.Core/CardData/Cards/mortuary-mire.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, mirroring the JSON-driven posture of
/// <see cref="BloodfellCavesFactory"/>. The targeted graveyard-to-library ETB
/// is attached inline (the JSON-declarative effect loader has no shorthand for
/// "put target creature card from your graveyard on top of your library"),
/// mirroring <see cref="MysticSanctuaryFactory"/>.
/// </para>
///
/// ## Lifecycle — two overloads
/// The single-arg <see cref="Create(Player)"/> overload produces the correct
/// card shape — the ETB trigger is attached for shape inspection but is not
/// registered with a <see cref="TriggerManager"/>, and the unconditional
/// enters-tapped restriction (CR 614.1c) is omitted (no
/// <see cref="ReplacementBus"/> to register against). This is the overload
/// <see cref="NamedCardFactory"/> dispatches to — same posture as
/// <see cref="BloodfellCavesFactory"/> / <see cref="MysticSanctuaryFactory"/>.
/// Use <see cref="Create(Player, TriggerManager?, ReplacementBus?)"/> to wire
/// the trigger for bus-driven firing and register the enters-tapped
/// replacement.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: auto-takes the action when a target was
///   supplied; agent-driven decline deferred (same posture as Mystic
///   Sanctuary / Snapcaster Mage).
/// - <b>Agent target legality at choose-time</b>: <see cref="TargetRequest"/>
///   carries empty <c>LegalCandidates</c> (mirrors Mystic Sanctuary). The
///   resolution guard enforces the creature + graveyard + owner checks per
///   CR 608.2b.
/// </summary>
[CardName("Mortuary Mire")]
public static class MortuaryMireFactory
{
    public const string Slug = "mortuary-mire";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Mortuary Mire owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted
    /// and the ETB trigger is unregistered). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>Construct Mortuary Mire with optional runtime wiring. When
    /// <paramref name="triggers"/> is supplied the ETB recur trigger is
    /// registered for bus-driven firing; when <paramref name="replacements"/>
    /// is supplied the unconditional enters-tapped restriction (CR 614.1c) is
    /// registered.</summary>
    public static Land Create(Player owner, TriggerManager? triggers, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same posture
        // as BloodfellCavesFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.6a). Fires when Mortuary Mire enters
        // the battlefield. Unconditional — no intervening-if. "You may put
        // target creature card from your graveyard on top of your library."
        // On resolution the chosen creature card is moved Graveyard → top of
        // Library via IZone.InsertCardAt(0) (same as Mystic Sanctuary).
        // CR 608.2b illegal-on-resolution rechecks gate out cards no longer
        // in the graveyard, not owned by the controller, or no longer a
        // creature card at resolution time.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbEffect = new Effect(
            "Mortuary Mire: put target creature card from graveyard on top of library",
            () =>
            {
                if (etb is null) return;
                if (etb.ChosenTargets.Count == 0) return;
                // "You may" (CR 603.5) — no target chosen means the optional
                // action was declined; nothing happens.
                if (etb.ChosenTargets[0].Count == 0) return;
                if (etb.ChosenTargets[0][0] is not Card target) return;

                // CR 608.2b — illegal-on-resolution rechecks.
                if (target.Zone != ZoneType.Graveyard) return;
                if (target.Owner is null || !ReferenceEquals(target.Owner, owner)) return;
                if (!target.HasType(CardType.Creature)) return;

                owner.Zones.Graveyard.RemoveCard(target);
                owner.Zones.Library.InsertCardAt(0, target);
                target.SetZone(ZoneType.Library);
            });

        etb = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature card in your graveyard",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return land;
    }
}
