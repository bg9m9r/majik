using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eldritch Evolution (Eldritch Moon, {1}{G}).
///
/// Sorcery. Oracle text:
///   "As an additional cost to cast this spell, sacrifice a creature.
///    Search your library for a creature card with mana value less than
///    or equal to the sacrificed creature's mana value plus 2, put it
///    onto the battlefield, then shuffle. Exile Eldritch Evolution."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{G}.
/// - Additional cost (CR 601.2f): <see cref="SacrificeACreatureAdditionalCost"/>
///   — sacrifices the first creature on the caster's battlefield. The cost
///   captures the sacrificed creature reference so the resolve closure can
///   read its mana value.
/// - Resolve effect: prompt the controller's agent (via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) for a creature card
///   from the library whose mana value is &lt;= sacrificed creature's mana
///   value + 2 (CR 701.19a tutor). Moves the picked creature directly to the
///   battlefield under the caster's control. When a <see cref="ZoneService"/>
///   is supplied the move routes through it so ETB triggers (CR 603.6a) and
///   <c>CardMovedEvent</c> listeners fire on the tutored permanent.
/// - Exile-self: after the tutor finishes (find-and-no-find branches alike,
///   per CR 701.19a), the card itself is moved to its owner's exile zone
///   (CR 608.2 — printed "Exile Eldritch Evolution" rider overrides the
///   default sorcery-to-graveyard destination). Routed through
///   <see cref="ZoneService"/> when available; falls back to raw zone moves
///   otherwise.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice target prompt</b>. <see cref="SacrificeACreatureAdditionalCost"/>
///   picks the first creature on the controller's battlefield deterministically
///   (same v1 behaviour as Fling / Thud / Life's Legacy bespoke templates).
///   Full agent-driven sacrifice-target prompting requires the ITarget /
///   TargetResolver pipeline (deferred — same gap noted on
///   <see cref="SacrificeAnotherCreatureCost"/>).
/// - <b>Replacing the spell's destination via the stack resolver</b>. The
///   printed "Exile Eldritch Evolution" clause is implemented inside the
///   resolve closure rather than via a per-card destination-zone override
///   on <see cref="StackResolver"/>. <see cref="StackResolver"/> still tries
///   to move the card from the stack to the graveyard after the effect runs;
///   the closure has already moved the card to exile so the post-effect
///   re-move ends up as exile → graveyard. To keep that consistent with the
///   printed wording, the closure runs the exile move LAST and tests bypass
///   the live <see cref="StackResolver"/> (executing the effects directly,
///   mirroring <see cref="SylvanScryingFactory"/> / <see cref="GreenSunsZenithPatternTemplate"/>
///   conventions). A generic "ExileSourceOnResolve" hook in
///   <see cref="SpellCastFlow"/> would let the destination override happen
///   in the right place — that hook is out of scope for this v1 named-card
///   factory.
/// </summary>
[CardName("Eldritch Evolution")]
public static class EldritchEvolutionFactory
{
    public const string CardName = "Eldritch Evolution";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>
    /// Build an Eldritch Evolution sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time spell definition is built on
    /// demand via <see cref="BuildSpellDefinition"/> so the caster
    /// reference matches the player resolving the spell and the optional
    /// <see cref="ZoneService"/> is plumbed for ETB-trigger routing.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Eldritch Evolution uses on
    /// resolution. Composes a sacrifice-a-creature additional cost with a
    /// tutor closure that respects the sacrificed creature's mana value +2
    /// cap and a final self-exile move.
    /// </summary>
    /// <param name="caster">Player resolving the spell — used to identify
    /// the library to search, the controller of the tutored permanent, and
    /// the owner of the Eldritch Evolution card for the self-exile move.</param>
    /// <param name="card">The Eldritch Evolution card instance whose
    /// resolve-time effect chain is being built. The closure captures this
    /// reference so the final self-exile move targets the specific
    /// card the spell was cast from (rather than a name lookup).</param>
    /// <param name="zoneService">Optional. When supplied, the tutored
    /// creature's library→battlefield move and the Eldritch Evolution
    /// self-exile move both route through the service so
    /// <c>CardMovedEvent</c> + ETB triggers fire (CR 603.6a). When null,
    /// raw zone manipulation is used (shape / dispatcher-test path).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ICard card,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(card);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p =>
            {
                // Resolve the sacrificed creature reference from the
                // additional-cost payment list (populated by SpellCastFlow
                // after CR 601.2f cost payment). Tests that exercise the
                // effect directly thread a ChosenSpellParams with the
                // SacrificeACreatureAdditionalCost already paid.
                Creature? sacrificed = null;
                foreach (var paid in p.AdditionalCostPaymentsOrEmpty)
                {
                    if (paid is SacrificeACreatureAdditionalCost sac && sac.Sacrificed is Creature c)
                    {
                        sacrificed = c;
                        break;
                    }
                }

                return new IEffect[]
                {
                    new Effect("Eldritch Evolution: tutor creature mv <= sac.mv + 2", () =>
                    {
                        // CR 701.19a — find / no-find both legal. No
                        // sacrificed reference (cost not paid via the
                        // expected shape) means we can't compute the
                        // cap; resolve as no-op rather than tutor for an
                        // unbounded value.
                        if (sacrificed == null) return;

                        var cap = sacrificed.ManaCostValue.TotalValue + 2;

                        var candidates = caster.Zones.Library.GetCards()
                            .Where(c => c.HasType(CardType.Creature)
                                        && Majik.Core.ValueObjects.ManaCost.Parse(c.ManaCost).TotalValue <= cap)
                            .ToList();
                        if (candidates.Count == 0) return;

                        var agent = AgentRegistry.Get(caster);
                        ICard? pick = agent != null
                            ? agent.ChooseLibraryPickAsync(
                                ctx: null,
                                candidates: candidates,
                                kindLabel: $"creature card with mana value {cap} or less")
                                .GetAwaiter().GetResult()
                            : candidates[0];

                        // CR 701.19a — agent may decline to find.
                        if (pick == null) return;

                        if (zoneService != null)
                        {
                            zoneService.MoveCard(
                                pick, ZoneType.Library, ZoneType.Battlefield, caster);
                        }
                        else
                        {
                            caster.Zones.Library.RemoveCard(pick);
                            caster.Zones.Battlefield.AddCard(pick);
                            pick.SetZone(ZoneType.Battlefield);
                            pick.SetController(caster);
                        }
                        // CR 701.20a — shuffle after a search effect.
                        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "eldritch-evolution");
                    }),
                    new Effect("Eldritch Evolution: exile self", () =>
                    {
                        // CR 608.2 — printed "Exile Eldritch Evolution"
                        // overrides the default sorcery-to-graveyard
                        // destination. Move the card to its owner's exile
                        // zone from wherever it currently is (stack on the
                        // live path; hand when tests bypass SpellCastFlow).
                        var ownerPlayer = card.Owner ?? caster;
                        var fromZone = card.Zone;
                        if (zoneService != null)
                        {
                            zoneService.MoveCard(card, fromZone, ZoneType.Exile);
                        }
                        else
                        {
                            // Owner-zone bookkeeping: remove from the
                            // current zone (best-effort across hand /
                            // stack / graveyard / library) and add to
                            // exile. ZoneManager.MoveCard handles the
                            // cross-zone bookkeeping when present; fall
                            // back to explicit add when the from-zone
                            // doesn't carry the card (e.g. the card was
                            // already off-zone by other means).
                            switch (fromZone)
                            {
                                case ZoneType.Hand:
                                    ownerPlayer.Zones.Hand.RemoveCard(card);
                                    break;
                                case ZoneType.Graveyard:
                                    ownerPlayer.Zones.Graveyard.RemoveCard(card);
                                    break;
                                case ZoneType.Library:
                                    ownerPlayer.Zones.Library.RemoveCard(card);
                                    break;
                                case ZoneType.Battlefield:
                                    ownerPlayer.Zones.Battlefield.RemoveCard(card);
                                    break;
                                case ZoneType.Stack:
                                    // Card is on the stack — no per-player
                                    // collection to remove from. The stack
                                    // pops the spell separately.
                                    break;
                                case ZoneType.Exile:
                                    // Already in exile — nothing to do.
                                    return;
                            }
                            ownerPlayer.Zones.Exile.AddCard(card);
                            card.SetZone(ZoneType.Exile);
                        }
                    }),
                };
            },
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });
    }
}
