using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Living End (Time Spiral, {2}{B}{B}{B}, mv 5).
///
/// Sorcery. Cascade. Oracle text:
///   "Each player exiles all creature cards from their graveyard, then
///    sacrifices all creatures they control, then puts all cards they
///    exiled this way onto the battlefield."
///
/// ## Implementation
///
/// Resolve effect (CR 608.2c — three sequential clauses joined by
/// "then", applied per-player simultaneously within each clause but in
/// strict clause order):
///
/// <list type="number">
///   <item>For each player, exile every creature card from that
///         player's graveyard. Cards moved this way are tracked per
///         player for the third step.</item>
///   <item>For each player, sacrifice every creature on the
///         battlefield they control (CR 701.16). Sacrificed creatures
///         go to their owners' graveyards — they are NOT picked up by
///         step 3 (the exile snapshot was taken before sacrifices).</item>
///   <item>For each player, put the cards exiled in step 1 onto the
///         battlefield under that player's control (CR 110.2 —
///         "their" battlefield). Each move is routed through
///         <see cref="Majik.Core.Services.ZoneService"/> when one is
///         supplied so <see cref="Majik.Core.Events.CardMovedEvent"/>
///         publishes and ETB triggers fire (CR 603.6a). Without a
///         ZoneService the moves still happen but no events publish
///         (matches the fallback contract of
///         <c>LibrarySpellFactory.ReanimateToBattlefieldSpell</c> and
///         <c>LibrarySpellFactory.ReturnAllFromGraveyardSpell</c>).</item>
/// </list>
///
/// ## Cascade (CR 702.85)
///
/// The printed Cascade ability is wired here on-cast: a triggered
/// ability over <see cref="SpellCastEvent"/> for this card invokes
/// <see cref="CascadeAction.Cascade"/> with sourceManaValue = 5 (the
/// printed mana value of Living End). The eligible nonland-with-MV-&lt;-5
/// card sits in exile so the caller can drive the optional free-cast
/// via <see cref="Costs.CastFromExileAlternativeCost"/> + <see cref="SpellCastFlow"/>
/// (CR 702.85a — "you may cast that spell without paying its mana
/// cost"). The trigger's active zone is <see cref="ZoneType.Stack"/>
/// — Living End needs to be on the stack when its cast event fires.
/// Mirrors <c>CrashingFootfallsFactory</c>.
/// </summary>
public static class LivingEndFactory
{
    public const string CardName = "Living End";
    public const string PrintedManaCost = "{2}{B}{B}{B}";

    /// <summary>
    /// CR 202.3 — mana value of <c>{2}{B}{B}{B}</c> = 5. Used as the
    /// cascade source-MV cap (eligible cards have MV &lt; 5).
    /// </summary>
    public const int CascadeSourceManaValue = 5;

    /// <summary>
    /// Build a Living End sorcery with no runtime services. The cascade
    /// trigger is attached to the card's ability list for shape inspection
    /// but is not registered with a TriggerManager. Suitable for
    /// dispatcher / shape-only tests.
    /// </summary>
    public static Sorcery Create(Player owner) =>
        Create(owner, triggers: null, willCast: null, onCascadeResolved: null);

    /// <summary>
    /// Build a Living End sorcery with optional trigger-manager wiring
    /// and "you may cast" decision predicate for cascade.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="triggers">When supplied, the cascade trigger is
    /// registered so a <see cref="SpellCastEvent"/> for this card lands
    /// on the stack automatically.</param>
    /// <param name="willCast">Forwarded to <see cref="CascadeAction.Cascade"/>
    /// — the controller's "you may" decision for the eligible card.
    /// Default = always cast.</param>
    /// <param name="onCascadeResolved">Optional callback invoked with the
    /// <see cref="CascadeAction.CascadeResult"/> when the cascade trigger
    /// resolves. Production callers use this to drive the free-cast of
    /// <see cref="CascadeAction.CascadeResult.Eligible"/> via
    /// <see cref="Costs.CastFromExileAlternativeCost"/> + <see cref="SpellCastFlow"/>
    /// (CR 702.85a). Tests use it to observe trigger firing.</param>
    public static Sorcery Create(
        Player owner,
        TriggerManager? triggers,
        Func<ICard, bool>? willCast = null,
        Action<CascadeAction.CascadeResult>? onCascadeResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.85 — Cascade. "When you cast this spell, exile cards from
        // the top of your library until you exile a nonland card whose
        // mana value is less than this spell's mana value …"
        // Trigger fires off the SpellCastEvent for THIS card.
        var cascadeCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        var cascadeEffect = new Effect(
            "Living End — Cascade (CR 702.85)",
            () =>
            {
                var result = CascadeAction.Cascade(
                    controller: owner,
                    sourceManaValue: CascadeSourceManaValue,
                    willCast: willCast);
                onCascadeResolved?.Invoke(result);
            });

        var cascadeTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: cascadeCondition,
            effects: new IEffect[] { cascadeEffect },
            // Cascade fires while the spell is on the stack (the cast
            // event is published as the spell moves to the stack), so the
            // ability needs to be active in the Stack zone — mirrors
            // CrashingFootfallsFactory.
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(cascadeTrigger);
        triggers?.RegisterTriggeredAbility(cascadeTrigger);

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Living End
    /// resolves. No targets, no modes — the three sequential
    /// per-player clauses run in order on every player in
    /// <see cref="ChosenSpellParams.AllPlayers"/>.
    /// </summary>
    /// <param name="zones">Live <see cref="Majik.Core.Services.ZoneService"/>.
    /// When supplied, each reanimate move publishes a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> so ETB triggers
    /// fire (CR 603.6a). When null, moves still happen via direct zone
    /// mutation but no events publish.</param>
    public static SpellDefinition BuildSpellDefinition(
        Majik.Core.Services.ZoneService? zones = null) => new(
        Modes: Array.Empty<string>(),
        HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p => new IEffect[]
        {
            new Effect("Living End: mass-exile-grave + sac-creatures + mass-reanimate", () =>
            {
                if (p.AllPlayers == null) return;

                // ---------------------------------------------------------
                // Step 1: each player exiles all creature cards from their
                // graveyard. We snapshot the moved cards per player so step 3
                // can return EXACTLY those — anything dumped into a graveyard
                // by step 2 (the sacrifices) is excluded by construction.
                // ---------------------------------------------------------
                var exiledByPlayer = new Dictionary<Player, List<ICard>>(p.AllPlayers.Count);
                foreach (var player in p.AllPlayers)
                {
                    var creatureCards = player.Zones.Graveyard.GetCards()
                        .Where(c => c.HasType(CardType.Creature))
                        .ToList();

                    var moved = new List<ICard>(creatureCards.Count);
                    foreach (var card in creatureCards)
                    {
                        if (zones != null)
                        {
                            zones.MoveCard(card, ZoneType.Graveyard, ZoneType.Exile);
                        }
                        else
                        {
                            player.Zones.Graveyard.RemoveCard(card);
                            player.Zones.Exile.AddCard(card);
                            card.SetZone(ZoneType.Exile);
                        }
                        moved.Add(card);
                    }
                    exiledByPlayer[player] = moved;
                }

                // ---------------------------------------------------------
                // Step 2: each player sacrifices all creatures they control.
                // Snapshot first — sacrificing during iteration would mutate
                // the collection. Each sacrificed creature goes to its
                // owner's graveyard (CR 701.16b). These are NOT picked up
                // by step 3 because step 1's snapshot was already taken.
                // ---------------------------------------------------------
                foreach (var player in p.AllPlayers)
                {
                    var creatures = player.Zones.Battlefield.GetCards()
                        .Where(c => c.HasType(CardType.Creature)
                                 && ReferenceEquals(c.Controller, player))
                        .ToList();

                    foreach (var creature in creatures)
                    {
                        var owner = creature.Owner ?? player;
                        if (zones != null)
                        {
                            zones.MoveCard(creature, ZoneType.Battlefield, ZoneType.Graveyard);
                        }
                        else
                        {
                            player.Zones.Battlefield.RemoveCard(creature);
                            owner.Zones.Graveyard.AddCard(creature);
                            creature.SetZone(ZoneType.Graveyard);
                        }
                    }
                }

                // ---------------------------------------------------------
                // Step 3: each player puts the cards they exiled in step 1
                // onto the battlefield. Reanimated permanent enters under
                // the player who exiled it (CR 110.2 — "their battlefield").
                // ZoneService routing here is what makes ETB triggers fire
                // (mirrors PR #165 / #174 wiring).
                // ---------------------------------------------------------
                foreach (var player in p.AllPlayers)
                {
                    if (!exiledByPlayer.TryGetValue(player, out var cards)) continue;
                    foreach (var card in cards)
                    {
                        // A replacement effect or earlier movement could have
                        // displaced the card; only re-enter from Exile.
                        if (card.Zone != ZoneType.Exile) continue;

                        if (zones != null)
                        {
                            zones.MoveCard(card, ZoneType.Exile, ZoneType.Battlefield, player);
                        }
                        else
                        {
                            player.Zones.Exile.RemoveCard(card);
                            player.Zones.Battlefield.AddCard(card);
                            card.SetZone(ZoneType.Battlefield);
                            card.SetController(player);
                        }
                    }
                }
            }),
        });
}
