using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Unsubstantiate (Eldritch Moon, <c>{1}{U}</c>).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Return target spell or creature to its owner's hand."
///
/// ## Why it gets its own factory
/// Unsubstantiate is the soft-counter / tempo-bounce that targets EITHER a
/// spell on the stack OR a creature on the battlefield with a single target.
/// It is the single-clause, non-modal cousin of
/// <see cref="VenserShaperSavantFactory"/> ("return target spell or permanent
/// to its owner's hand" — an ETB trigger) and of
/// <see cref="CrypticCommandFactory"/>'s counter + bounce modes — here folded
/// into one instant-cast <see cref="SpellDefinition"/> on the
/// <see cref="SpellCastFlow"/> path, the same shape as
/// <see cref="BoomerangFactory"/> / <see cref="VaporSnagFactory"/>. No new
/// engine mechanic is required: it reuses the existing single-target request
/// + EffectFactory plumbing and a raw zone move for the bounce.
///
/// ## Implemented (v1)
/// - Instant shape, {1}{U}, blue. Card shape comes from the embedded JSON
///   (<c>unsubstantiate.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - A single 1..1 <see cref="TargetRequest"/> whose candidate gatherer
///   enumerates every <see cref="ISpell"/> on the live stack PLUS every
///   battlefield <see cref="Creature"/> across all players (the printed text
///   has no controller filter — it can bounce friendly objects too).
/// - <b>Resolve</b> (CR 701.10 — "return ... to its owner's hand"):
///   - <i>Spell target</i>: the spell is removed from the stack and its card
///     moved to its owner's hand. Returning a spell to hand is NOT a counter
///     (CR 701.10 vs CR 701.5) — so an uncounterable spell
///     (<see cref="ISpell.CannotBeCountered"/>) is still returned; the card
///     lands in its owner's hand, never the graveyard.
///   - <i>Creature target</i>: returned to its owner's hand (same raw-zone
///     path as <see cref="BoomerangFactory"/> / Riftwing Cloudskate).
///     CR 608.2b resolution-time legality re-check: a creature that has left
///     the battlefield no-ops.
/// - <b>No / illegal target</b>: clean no-op (CR 608.2b).
///
/// ## Rules citations
/// - CR 701.10 — "Return ... to its owner's hand" (bounce, not a counter).
/// - CR 701.5 / 701.5b — countering vs. bouncing; uncounterable spells are
///   still legal bounce targets.
/// - CR 608.2b — resolution-time target legality re-check.
///
/// ## Deferred (v1 gaps)
/// - <b>ZoneService routing for the creature bounce</b>: the creature path
///   uses a raw zone move (no <see cref="Majik.Core.Services.ZoneService"/>
///   threaded through), so no <c>CardMovedEvent</c> / replacement-bus fires
///   for the bounce — same lossy posture as Venser's permanent path. The
///   printed observable contract (target ends up in its owner's hand) is
///   preserved.
/// </summary>
[CardName("Unsubstantiate")]
public static class UnsubstantiateFactory
{
    public const string CardName = "Unsubstantiate";
    public const string Slug = "unsubstantiate";

    /// <summary>
    /// Construct Unsubstantiate as an Instant with owner/controller wired.
    /// The resolve <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildDefinition"/> at the cast-flow resolver wire-up site.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "return target spell or creature to its owner's hand"
    /// <see cref="SpellDefinition"/>. The single target may be either a spell
    /// on the supplied <paramref name="stack"/> or a battlefield creature.
    /// </summary>
    /// <param name="stack">Live stack — required for the spell-return branch
    /// to enumerate / remove the targeted spell. <see langword="null"/> in
    /// pure-shape tests (the spell branch then no-ops; the creature branch
    /// still works via a raw zone move).</param>
    public static SpellDefinition BuildDefinition(Majik.Core.Stack.Stack? stack = null)
    {
        var targetRequest = new TargetRequest(
            Description: "target spell or creature",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Bounce,
            // Any spell on the stack + any battlefield creature across all
            // players (no printed controller filter). The Bounce intent
            // scopes the bot ranker toward opponents' objects.
            CandidateGatherer: ctx =>
            {
                var candidates = new List<object>();
                candidates.AddRange(
                    ctx.Stack.GetAll().OfType<ISpell>().Cast<object>());
                candidates.AddRange(ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .OfType<Creature>()
                    .Cast<object>());
                return candidates;
            });

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { targetRequest },
            EffectFactory: p => new IEffect[]
            {
                new Effect(
                    $"{CardName} — return target spell or creature to its owner's hand (CR 701.10)",
                    () =>
                    {
                        if (p.Targets.Count == 0 || p.Targets[0].Count == 0) return;

                        switch (p.Targets[0][0])
                        {
                            case ISpell spell:
                                ReturnSpellToOwnersHand(stack, spell);
                                return;

                            case Creature creature:
                                ReturnCreatureToOwnersHand(creature);
                                return;

                            // Any other shape is not a legal "spell or
                            // creature" target. Clean no-op (CR 608.2b).
                            default:
                                return;
                        }
                    }),
            });
    }

    /// <summary>
    /// Return a spell on the stack to its owner's hand (CR 701.10). Unlike a
    /// counter (CR 701.5 — card to graveyard), the card lands in its owner's
    /// <see cref="ZoneType.Hand"/>. This is NOT a counter, so an uncounterable
    /// spell (<see cref="ISpell.CannotBeCountered"/>) is still returned — we
    /// remove it from the stack directly rather than via
    /// <see cref="OracleSpellBinder.RemoveFromStack"/>, which guards against
    /// countering uncounterable spells (CR 701.5b). Mirrors
    /// <see cref="VenserShaperSavantFactory"/>'s spell-return path.
    /// </summary>
    private static void ReturnSpellToOwnersHand(Majik.Core.Stack.Stack? stack, ISpell spell)
    {
        if (stack == null) return;

        // CR 608.2b — resolution-time legality re-check: the spell must still
        // be on the stack.
        if (!stack.GetAll().Contains(spell)) return;

        // Remove the spell from the stack regardless of CannotBeCountered
        // (bounce is not a counter — CR 701.10). Pop-and-rebuild idiom: pop
        // everything, drop the chosen spell, push the rest back in order.
        var keep = new List<IStackObject>();
        while (!stack.IsEmpty)
        {
            var top = stack.Pop()!;
            if (!ReferenceEquals(top, spell)) keep.Add(top);
        }
        for (var i = keep.Count - 1; i >= 0; i--)
        {
            stack.Push(keep[i]);
        }

        // CR 701.10 — move the underlying card to its owner's hand (never the
        // graveyard).
        if (spell.Card is not Card targetCard) return;
        var targetOwner = targetCard.Owner;
        if (targetOwner == null) return;

        if (targetCard.Zone != ZoneType.Hand)
        {
            targetOwner.Zones.Hand.AddCard(targetCard);
        }
        targetCard.SetZone(ZoneType.Hand);
        targetCard.SetController(targetOwner);
    }

    /// <summary>
    /// Return a creature to its owner's hand (CR 701.10). Same raw-zone-move
    /// path as Boomerang / Riftwing Cloudskate. CR 608.2b resolution-time
    /// legality re-check: a creature that has left the battlefield no-ops.
    /// </summary>
    private static void ReturnCreatureToOwnersHand(Creature creature)
    {
        // CR 608.2b — must still be on the battlefield at resolution.
        if (creature.Zone != ZoneType.Battlefield) return;

        var owner = creature.Owner;
        if (owner == null) return;

        var fromController = creature.Controller ?? owner;
        fromController.Zones.Battlefield.RemoveCard(creature);
        owner.Zones.Hand.AddCard(creature);
        creature.SetZone(ZoneType.Hand);
        creature.SetController(owner);
    }
}
