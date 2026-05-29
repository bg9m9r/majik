using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eldrazi Displacer (Oath of the Gatewatch,
/// {2}{C}). Creature — Eldrazi 3/3. Oracle text (verified against
/// Scryfall):
///   "Devoid (This card has no color.)
///    {2}{C}: Exile another target creature, then return it to the
///    battlefield tapped under its owner's control. ({C} represents
///    colorless mana.)"
///
/// The card's base shape (name, Creature, Eldrazi subtype, {2}{C}, 3/3)
/// is materialised from the embedded JSON definition
/// (<c>eldrazi-displacer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Devoid and the
/// exile-and-return-tapped activated ability are layered on here — the
/// JSON <c>AbilityDefinition</c> schema expresses neither keyword markers
/// nor a targeted exile/return effect (same posture as
/// <see cref="StormscaleScionFactory"/> and the other JSON-backed cards
/// whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - <b>Devoid (CR 702.114)</b> — stamps <see cref="Card.SetDevoid"/> so
///   <see cref="CardColors.GetColors"/> returns the empty set regardless
///   of mana cost, plus a <see cref="KeywordAbility"/> marker for
///   inspection. Same shape as <see cref="WrithingChrysalisFactory"/>.
/// - <b>Activated exile-and-return ability (CR 602.1 / 701.21 / 614)</b>:
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>
///   ({2}{C}) and a 1..1 "another target creature"
///   <see cref="TargetRequest"/>. The cost mirrors the World Breaker
///   graveyard-activation shape (<see cref="WorldBreakerFactory"/>) — the
///   {C} pip parses (folded into generic at cost-payment time, the
///   engine's standing colourless-mana posture, CR 107.4c). The
///   resolution body (CR 608.2b legality re-check) exiles the chosen
///   creature and immediately returns it to its <em>owner's</em>
///   battlefield TAPPED:
///   - "another" — Eldrazi Displacer cannot target itself (CR 115.5b).
///   - "under its owner's control" (CR 108.3) — the return routes through
///     <c>target.Owner</c>'s zones, NOT the controller's, so a
///     control-swapped creature (e.g. an Act of Treason target) goes back
///     to its true owner. Same owner-routing as
///     <see cref="FlickerwispFactory"/>.
///   - "tapped" — the returned permanent enters tapped (CR 614-style
///     "with" rider modelled by a post-return <see cref="Permanent.Tap"/>).
///
///   This is a single-resolution exile-then-return (NOT a delayed
///   end-step return like Flickerwisp) — exile and re-entry both happen
///   as the ability resolves, mirroring <see cref="CloudshiftFactory"/> /
///   <see cref="RestorationAngelFactory"/>'s immediate-flicker body.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + the activated ability attached;
///   zone moves use raw owner-routed manipulation. The overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?)"/> — when a
///   <see cref="ZoneService"/> is supplied, the exile + return moves route
///   through <see cref="ZoneService.MoveCard"/> so
///   <see cref="Majik.Core.Events.CardMovedEvent"/> publishes (downstream
///   ETB / LTB listeners fire on the re-entry) — same posture as
///   <see cref="RestorationAngelFactory"/> / <see cref="WorldBreakerFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>True new-object semantics (CR 400.7)</b>: the engine returns the
///   same <see cref="Permanent"/> instance — no fresh object is minted on
///   flicker, so "until end of turn" effects on the exiled creature
///   persist through the return. Shared "flicker new-object" primitive
///   deferred alongside Cloudshift / Restoration Angel / Ephemerate.
/// - <b>Token guard (CR 111.8)</b>: a token target ceases to exist in
///   exile; the resolution body guards on <c>Zone == ZoneType.Exile</c>
///   before the return so a token displace is a clean no-op.
/// </summary>
[CardName("Eldrazi Displacer")]
public static class EldraziDisplacerFactory
{
    public const string CardName = "Eldrazi Displacer";
    public const string Slug = "eldrazi-displacer";
    public const string ActivationManaCost = "{2}{C}";
    private const string DevoidKeyword = "Devoid";

    /// <summary>
    /// Construct Eldrazi Displacer with no live <see cref="ZoneService"/>.
    /// Devoid + the activated exile-and-return ability are attached; zone
    /// moves use raw owner-routed manipulation. The overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, zones: null);

    /// <summary>
    /// Construct Eldrazi Displacer with an optional <see cref="ZoneService"/>.
    /// When supplied, the exile + return moves route through
    /// <see cref="ZoneService.MoveCard"/> so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes.
    /// </summary>
    public static Creature Create(Player owner, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi subtype, {2}{C}, 3/3). The JSON carries no abilities —
        // Devoid + the activated ability are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.114 — Devoid. Stamp IsDevoid so CardColors.GetColors
        // returns the empty set regardless of the {C} pip, plus a keyword
        // marker for inspection. Same shape as Writhing Chrysalis.
        card.SetDevoid(true);
        card.AddAbility(new KeywordAbility(DevoidKeyword, card, owner));

        // ----------------------------------------------------------------
        // Activated ability — CR 602.1 / 701.21 / 614.
        //   "{2}{C}: Exile another target creature, then return it to the
        //    battlefield tapped under its owner's control."
        // ----------------------------------------------------------------
        ActivatedAbility? ability = null;
        var effect = new Effect(
            $"{CardName}: exile another target creature, return it tapped under its owner's control",
            () =>
            {
                if (ability == null) return;
                var chosen = ability.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Creature target) return;

                // "another" — cannot target Eldrazi Displacer itself
                // (CR 115.5b).
                if (ReferenceEquals(target, card)) return;
                // CR 608.2b — resolution-time legality re-check.
                if (target.Zone != ZoneType.Battlefield) return;

                ExileAndReturnTapped(target, owner, zones);
            });

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivationManaCost) },
            effects: new IEffect[] { effect },
            sorcerySpeed: false,
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "another target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // "another target creature" — every creature on any
                    // battlefield except Eldrazi Displacer itself
                    // (CR 115.5b).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => !ReferenceEquals(c, card))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(ability);

        return card;
    }

    /// <summary>
    /// CR 701.21 + CR 614 — exile <paramref name="target"/> from the
    /// battlefield, then immediately return it to its <em>owner's</em>
    /// battlefield tapped under the owner's control (CR 108.3). Routes
    /// through <see cref="ZoneService.MoveCard"/> when
    /// <paramref name="zones"/> is supplied so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> fires; raw
    /// owner-routed zone manipulation otherwise (same shape as
    /// <see cref="FlickerwispFactory"/> / <see cref="WorldBreakerFactory"/>).
    /// </summary>
    private static void ExileAndReturnTapped(Creature target, Player fallbackOwner, ZoneService? zones)
    {
        var targetOwner = target.Owner ?? fallbackOwner;

        // CR 701.21 — Exile.
        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Battlefield, ZoneType.Exile);
        }
        else
        {
            var holder = target.Controller ?? targetOwner;
            holder.Zones.Battlefield.RemoveCard(target);
            targetOwner.Zones.Exile.AddCard(target);
            target.SetZone(ZoneType.Exile);
        }

        // CR 111.8 — a token in exile has already ceased to exist; skip the
        // return cleanly so a token displace is a no-op.
        if (target.Zone != ZoneType.Exile) return;

        // CR 614 — "return it to the battlefield ... under its owner's
        // control". Owner-routed so a control-swapped creature goes back to
        // its true owner (CR 108.3).
        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Exile, ZoneType.Battlefield, targetOwner);
        }
        else
        {
            targetOwner.Zones.Exile.RemoveCard(target);
            targetOwner.Zones.Battlefield.AddCard(target);
            target.SetZone(ZoneType.Battlefield);
        }
        target.SetController(targetOwner);

        // "tapped" — the returned permanent enters tapped. Tap() throws if
        // already tapped, so guard (a freshly-returned object is untapped,
        // but the ZoneService re-entry posture leaves tap state untouched).
        if (!target.IsTapped) target.Tap();
    }
}
