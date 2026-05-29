using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tectonic Edge (Worldwake).
///
/// Land.
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}, {T}, Sacrifice this land: Destroy target nonbasic land.
///    Activate only if an opponent controls four or more lands."
///
/// Tectonic Edge is a sibling of <see cref="WastelandFactory"/> /
/// <see cref="FieldOfRuinFactory"/> / <see cref="DemolitionFieldFactory"/>:
/// a {C}-producing utility land whose second ability sacrifices itself to
/// destroy a nonbasic land. It is implemented with the same destroy
/// primitives. The single delta is the CR 602.5b activation gate — "Activate
/// only if an opponent controls four or more lands" — which mirrors the gate
/// posture of <see cref="MagmaticChannelerFactory"/> ("four or more
/// instant/sorcery cards in your graveyard") and
/// <see cref="SeaGateWreckageFactory"/> ("no cards in hand").
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes / supertypes).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1, no
///   stack).
/// - <b>{1}, {T}, Sacrifice this land: Destroy target nonbasic land.</b> —
///   an <see cref="ActivatedAbility"/> with:
///     - <see cref="ManaCostCost"/> {1}
///     - <see cref="AdditionalCost.Tap"/>
///     - Self-sacrifice inlined in the resolution closure (Wasteland /
///       Field of Ruin / Demolition Field posture, since
///       <see cref="AdditionalCost.Sacrifice"/>'s zone-move primitive is
///       still a stub).
///   A 1..1 <see cref="TargetRequest"/> declares "target nonbasic land";
///   the resolution body gates on (a) Land type, (b) NOT Basic supertype,
///   (c) on the battlefield (CR 608.2b — illegal target → the destroy half
///   does nothing; the cost was already paid so the self-sac stands).
/// - <b>Activation gate "Activate only if an opponent controls four or more
///   lands"</b> (CR 602.5b — activation restriction). Same v1 posture as
///   Magmatic Channeler: <see cref="ActivatedAbility"/> does not yet expose
///   a generic <c>CanActivate</c> hook for non-mana activations, so the gate
///   is enforced two ways:
///     - At <em>action-enumeration time</em>: callers (bot policies / agent
///       legal-action probes) consult the public static predicate
///       <see cref="OpponentControlsFourOrMoreLands"/> BEFORE paying the
///       cost.
///     - At <em>resolve time</em>: the effect closure re-checks the gate and
///       short-circuits the destroy half cleanly when the rule was violated.
///       CR 117.x — the cost was already paid by the cost layer, so the
///       self-sacrifice still stands; only the destroy body is skipped.
///       This mirrors the Magmatic Channeler / Liliana -6 deferred-no-op
///       posture (cost paid, body skipped).
///
/// ## Deferred (v1 gaps — shared with the Magmatic Channeler / Verge family)
/// - <b><c>IActivatedAbility.CanActivate</c> hook</b>: the action-validator
///   pipeline does not yet consult an activation predicate on
///   <see cref="IActivatedAbility"/> (only the
///   <see cref="ActivatedAbility.IsSorcerySpeed"/> rider is wired). Tectonic
///   Edge's opponent-land threshold is exposed as a static predicate so
///   callers can gate enumeration; once the generic hook ships the predicate
///   is the natural single attachment site.
/// - <b>AdditionalCost.Sacrifice</b>: self-sac payment is inlined into the
///   resolution closure until the shared primitive ships a zone-move
///   side-effect.
/// - <b>Agent target legality filtering</b>: ActionValidator does not yet
///   narrow the candidate pool to nonbasic lands; the resolution-time guard
///   catches illegal picks (CR 608.2b).
/// </summary>
[CardName("Tectonic Edge")]
public static class TectonicEdgeFactory
{
    public const string CardName = "Tectonic Edge";

    /// <summary>CR 602.5b threshold — an opponent must control at least this
    /// many lands for the destroy ability to be activatable.</summary>
    public const int OpponentLandThreshold = 4;

    public static Land Create(Player owner) => Create(owner, allPlayersResolver: null);

    /// <summary>
    /// Construct Tectonic Edge.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="allPlayersResolver">Late-bound enumerator of all players
    /// in the game. Used by the resolve-time activation-gate guard to count
    /// each opponent's lands (CR 602.5b). May be null — in that case the gate
    /// cannot be evaluated and the destroy half is suppressed at resolution
    /// (fail-closed; the self-sacrifice still stands). Mirrors Field of
    /// Ruin's <c>allPlayersResolver</c> posture.</param>
    public static Land Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("{C}")));

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice this land: Destroy target nonbasic land.
        // Activate only if an opponent controls four or more lands.
        // ----------------------------------------------------------------
        ActivatedAbility? destroyAbility = null;
        var destroyEffect = new Effect(
            $"{CardName}: destroy target nonbasic land (opponent controls 4+ lands)",
            () =>
            {
                if (destroyAbility == null) return;

                // Self-sacrifice — Wasteland / Field of Ruin posture
                // (AdditionalCost.Sacrifice is a stub today; the cost was
                // declared at activation, the visible zone-move catches up
                // here ahead of the destroy step). The sac is part of the
                // already-paid cost, so it runs regardless of the gate.
                SacrificeToOwnersGraveyard(land);

                // CR 602.5b activation gate — defensive re-check at resolve
                // time. The gate should already have been enforced at
                // activation time by the bot policy / action validator via
                // OpponentControlsFourOrMoreLands, but until
                // IActivatedAbility.CanActivate ships an authoritative hook
                // this is the safety net. Fail-closed: if no player list is
                // wired we cannot prove an opponent has four lands, so the
                // destroy half is skipped (the cost was paid; the body is a
                // no-op — Magmatic Channeler posture).
                var controller = land.Controller ?? owner;
                if (!OpponentControlsFourOrMoreLands(controller, allPlayersResolver))
                {
                    return;
                }

                // Destroy half — gate the chosen target (CR 608.2b — illegal
                // target → the destroy does nothing for that target).
                if (destroyAbility.ChosenTargets.Count == 0) return;
                if (destroyAbility.ChosenTargets[0].Count == 0) return;

                var chosen = destroyAbility.ChosenTargets[0][0];
                if (chosen is not ICard target) return;
                if (!target.HasType(CardType.Land)) return;
                if (target.HasSupertype(CardSupertype.Basic)) return;
                if (target.Owner == null) return;
                if (target.Zone != ZoneType.Battlefield) return;

                DestroyToOwnersGraveyard(target);
            });

        destroyAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { destroyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonbasic land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        land.AddAbility(destroyAbility);

        return land;
    }

    /// <summary>
    /// CR 602.5b activation gate — "Activate only if an opponent controls
    /// four or more lands."
    ///
    /// Public so action-enumeration callers (bot policies, agent legal-action
    /// probes) can gate the activation BEFORE the cost layer fires, and so
    /// the resolve-time guard can re-check it. Returns <c>true</c> iff at
    /// least one player other than <paramref name="controller"/> controls
    /// <see cref="OpponentLandThreshold"/> or more lands (CR 305 — a land is
    /// any permanent with the land card type; basics count). The
    /// controller's own lands never count toward the gate.
    /// </summary>
    /// <param name="controller">The activating player (Tectonic Edge's
    /// controller). Their own lands are excluded.</param>
    /// <param name="allPlayersResolver">Late-bound enumerator of all players.
    /// When null the gate cannot be evaluated and returns <c>false</c>
    /// (fail-closed).</param>
    public static bool OpponentControlsFourOrMoreLands(
        Player controller,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var players = allPlayersResolver?.Invoke();
        if (players == null) return false;

        foreach (var p in players)
        {
            if (ReferenceEquals(p, controller)) continue;
            if (p?.Zones?.Battlefield == null) continue;

            var landCount = p.Zones.Battlefield.GetCards()
                .Count(c => c.HasType(CardType.Land));
            if (landCount >= OpponentLandThreshold) return true;
        }

        return false;
    }

    private static void SacrificeToOwnersGraveyard(Land self)
    {
        var ownerOfSelf = self.Owner;
        if (ownerOfSelf == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        var holder = self.Controller ?? ownerOfSelf;
        holder.Zones.Battlefield.RemoveCard(self);
        ownerOfSelf.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }

    private static void DestroyToOwnersGraveyard(ICard card)
    {
        var ownerOfCard = card.Owner;
        if (ownerOfCard == null) return;

        var holder = card.Controller ?? ownerOfCard;
        holder.Zones.Battlefield.RemoveCard(card);
        ownerOfCard.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
