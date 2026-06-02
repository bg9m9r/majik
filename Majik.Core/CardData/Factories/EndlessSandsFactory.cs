using System.Runtime.CompilerServices;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Endless Sands (Hour of Devastation). Land — Desert.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {C}.
///    {2}, {T}: Exile target creature you control.
///    {4}, {T}, Sacrifice this land: Return each creature card exiled with
///    this land to the battlefield under its owner's control."
///
/// A colourless {C}-producing Desert. The {T}: Add {C} base is shared with
/// <see cref="HostileDesertFactory"/>; the exile-then-return half is the
/// classic "exiled-with-this-source ledger + return" pattern, implemented
/// exactly like <see cref="BomatCourierFactory"/> (a per-card
/// <see cref="ConditionalWeakTable{TKey,TValue}"/> ledger recording the
/// "exiled with this land" relationship the game tracks, CR 400.7).
///
/// ## Abilities
/// <list type="bullet">
///   <item><b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1,
///   no stack). Materialised from the embedded JSON definition
///   (<c>endless-sands.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>, same base shape as
///   <see cref="HostileDesertFactory"/>.</item>
///
///   <item><b>{2}, {T}: Exile target creature you control.</b> — an ordinary
///   <see cref="ActivatedAbility"/> (CR 602.1, uses the stack). Costs:
///   <see cref="ManaCostCost"/> {2} + <see cref="AdditionalCost.Tap"/> (the
///   {T}). A 1..1 <see cref="TargetRequest"/> declares "target creature you
///   control". Resolution moves the chosen creature
///   Battlefield → its OWNER's Exile zone and records it in the per-land
///   ledger. CR 608.2b — an illegal / vanished target is a clean no-op.</item>
///
///   <item><b>{4}, {T}, Sacrifice this land: Return each creature card exiled
///   with this land to the battlefield under its owner's control.</b> — an
///   <see cref="ActivatedAbility"/> with costs <see cref="ManaCostCost"/> {4}
///   + <see cref="AdditionalCost.Tap"/> + a self-sacrifice. The self-sac is
///   inlined into the resolution closure (Tectonic Edge / Wasteland posture —
///   <see cref="AdditionalCost.Sacrifice"/>'s zone-move side-effect is still a
///   stub), so it is modelled as a plain second <see cref="AdditionalCost"/>
///   (a no-op-payment Sacrifice marker for the cost-shape contract) plus the
///   visible zone move in the closure. Resolution then returns every card
///   still in the ledger (and still in exile) Exile → Battlefield under its
///   OWNER's control — CR 109.5 "its owner's control", so the returning
///   creature's controller is reset to its owner. The ledger is drained as
///   cards return.</item>
/// </list>
///
/// <para>
/// <b>Wiring overloads</b>: <see cref="Create(Player)"/> attaches the full
/// ability shape with no live <see cref="ZoneService"/> — suitable for
/// dispatcher / shape tests; zone moves use the raw-zone fallback.
/// <see cref="Create(Player, ZoneService?)"/> routes the exile / return /
/// sacrifice zone moves through <see cref="ZoneService.MoveCard"/> so
/// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes (and any ETB
/// triggers on the returning creatures fire, CR 603.6a). Same two-mode
/// posture as <see cref="BomatCourierFactory"/> / <see cref="EmperorOfBonesFactory"/>.
/// </para>
/// </summary>
[CardName("Endless Sands")]
public static class EndlessSandsFactory
{
    public const string CardName = "Endless Sands";
    public const string Slug = "endless-sands";
    public const string ExileManaCost = "{2}";
    public const string ReturnManaCost = "{4}";

    /// <summary>
    /// Per-land "exiled with this land" ledger. Keyed off the Endless Sands
    /// card instance via <see cref="ConditionalWeakTable{TKey,TValue}"/> so
    /// multiple copies in the same game keep separate ledgers (mirrors
    /// <see cref="BomatCourierFactory"/>).
    /// </summary>
    private static readonly ConditionalWeakTable<Card, EndlessSandsState> _state = new();

    /// <summary>
    /// Retrieve the <see cref="EndlessSandsState"/> attached to a land
    /// instance produced by this factory. Returns null when the card was not
    /// built by this factory.
    /// </summary>
    public static EndlessSandsState? GetState(Card land)
    {
        ArgumentNullException.ThrowIfNull(land);
        return _state.TryGetValue(land, out var s) ? s : null;
    }

    /// <summary>
    /// Construct Endless Sands for the dispatcher / shape-test path: no
    /// <see cref="ZoneService"/> wired. Identity + ability shape are fully
    /// populated; zone moves use the raw-zone fallback.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, zones: null);

    /// <summary>
    /// Construct Endless Sands.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the exile / return / sacrifice zone
    /// moves route through <see cref="ZoneService.MoveCard"/> so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes. Raw zone
    /// manipulation otherwise.</param>
    public static Land Create(Player owner, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // Desert subtype, {T}: Add {C} mana ability).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        var state = new EndlessSandsState();
        _state.AddOrUpdate(land, state);

        // ----------------------------------------------------------------
        // {2}, {T}: Exile target creature you control.
        // CR 602.1 — ordinary activated ability (uses the stack). Resolution
        // moves the chosen creature to its owner's exile zone and records it
        // in the per-land ledger (CR 400.7).
        // ----------------------------------------------------------------
        ActivatedAbility? exileAbility = null;
        var exileEffect = new Effect(
            $"{CardName}: exile target creature you control",
            () =>
            {
                if (exileAbility == null) return;

                // CR 608.2b — illegal / vanished target is a clean no-op.
                if (exileAbility.ChosenTargets.Count == 0) return;
                if (exileAbility.ChosenTargets[0].Count == 0) return;

                var chosen = exileAbility.ChosenTargets[0][0];
                if (chosen is not Creature creature) return;
                if (!creature.HasType(CardType.Creature)) return;
                if (creature.Zone != ZoneType.Battlefield) return;

                ExileCreature(creature, zones);
                state.AddExiledWith(creature);
            });

        exileAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ExileManaCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { exileEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection),
            });

        land.AddAbility(exileAbility);

        // ----------------------------------------------------------------
        // {4}, {T}, Sacrifice this land: Return each creature card exiled
        // with this land to the battlefield under its owner's control.
        // CR 602.1 — ordinary activated ability. The self-sacrifice is
        // inlined in the closure (Tectonic Edge / Wasteland posture) because
        // AdditionalCost.Sacrifice's zone-move side-effect is still a stub;
        // the Sacrifice marker is kept on the cost list so the cost SHAPE is
        // correct, and the visible zone move runs in the closure.
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return each creature card exiled with this land under its owner's control",
            () =>
            {
                // Self-sacrifice — part of the already-paid cost; runs first
                // and unconditionally (CR 117.x — cost was declared at
                // activation; the visible zone-move catches up here).
                SacrificeToOwnersGraveyard(land, zones);

                ReturnExiledCreatures(state, zones);
            });

        var returnAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ReturnManaCost),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { returnEffect });

        land.AddAbility(returnAbility);

        return land;
    }

    // ------------------------------------------------------------------------
    // Resolution helpers
    // ------------------------------------------------------------------------

    /// <summary>
    /// Move a creature Battlefield → its OWNER's Exile zone (CR 110.6 — a
    /// card moves to its owner's exile zone). Routes through
    /// <see cref="ZoneService"/> when wired, raw-zone otherwise.
    /// </summary>
    private static void ExileCreature(Creature creature, ZoneService? zones)
    {
        var cardOwner = creature.Owner;
        if (cardOwner == null) return;

        if (zones != null)
        {
            zones.MoveCard(creature, ZoneType.Battlefield, ZoneType.Exile, cardOwner);
        }
        else
        {
            var holder = creature.Controller ?? cardOwner;
            holder.Zones.Battlefield.RemoveCard(creature);
            cardOwner.Zones.Exile.AddCard(creature);
            creature.SetZone(ZoneType.Exile);
        }
    }

    /// <summary>
    /// Resolve the return ability: put every creature still in the ledger
    /// (and still in exile) onto the battlefield under its OWNER's control
    /// (CR 109.5 — "its owner's control"). The ledger is drained as cards
    /// return; cards that have since left exile (the "exiled with" link ends
    /// on a zone change) are skipped.
    /// </summary>
    private static void ReturnExiledCreatures(EndlessSandsState state, ZoneService? zones)
    {
        // Snapshot — the ledger is mutated as we drain it.
        foreach (var exiled in state.ExiledWith.ToList())
        {
            state.RemoveExiledWith(exiled);

            if (exiled.Zone != ZoneType.Exile) continue;
            var cardOwner = exiled.Owner;
            if (cardOwner == null) continue;

            if (zones != null)
            {
                // "Under its owner's control" — move under the owner.
                zones.MoveCard(exiled, ZoneType.Exile, ZoneType.Battlefield, cardOwner);
            }
            else
            {
                cardOwner.Zones.Exile.RemoveCard(exiled);
                cardOwner.Zones.Battlefield.AddCard(exiled);
                exiled.SetZone(ZoneType.Battlefield);
            }

            // CR 109.5 — controller is reset to owner regardless of which
            // path moved the card.
            if (exiled is Card concrete) concrete.SetController(cardOwner);
        }
    }

    /// <summary>
    /// Self-sacrifice Endless Sands → its owner's graveyard. Part of the
    /// already-declared activation cost (Tectonic Edge / Wasteland posture).
    /// </summary>
    private static void SacrificeToOwnersGraveyard(Land self, ZoneService? zones)
    {
        var ownerOfSelf = self.Owner;
        if (ownerOfSelf == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        if (zones != null)
        {
            zones.MoveCard(self, ZoneType.Battlefield, ZoneType.Graveyard, ownerOfSelf);
        }
        else
        {
            var holder = self.Controller ?? ownerOfSelf;
            holder.Zones.Battlefield.RemoveCard(self);
            ownerOfSelf.Zones.Graveyard.AddCard(self);
            self.SetZone(ZoneType.Graveyard);
        }
    }
}

/// <summary>
/// Per-land "exiled with this land" ledger. Tracks the order of exile so the
/// return is deterministic. Mirrors <see cref="BomatCourierState"/>.
/// </summary>
public sealed class EndlessSandsState
{
    private readonly List<ICard> _exiledWith = new();

    /// <summary>All cards currently exiled with this Endless Sands, in
    /// insertion order.</summary>
    public IReadOnlyList<ICard> ExiledWith => _exiledWith;

    /// <summary>Record <paramref name="card"/> as exiled with this land.
    /// Idempotent.</summary>
    public void AddExiledWith(ICard card)
    {
        if (card == null) return;
        if (_exiledWith.Contains(card)) return;
        _exiledWith.Add(card);
    }

    /// <summary>Remove <paramref name="card"/> from the ledger. Returns true
    /// if the card was in the ledger.</summary>
    public bool RemoveExiledWith(ICard card)
    {
        if (card == null) return false;
        return _exiledWith.Remove(card);
    }
}
