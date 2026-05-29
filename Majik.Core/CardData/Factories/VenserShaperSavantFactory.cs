using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Venser, Shaper Savant (Future Sight,
/// <c>{2}{U}{U}</c>).
///
/// Legendary Creature — Human Wizard 2/2. Oracle text (verified against
/// Scryfall):
///   "Flash (You may cast this spell any time you could cast an instant.)
///    When Venser enters, return target spell or permanent to its owner's
///    hand."
///
/// The base shape (name, Legendary supertype, Human Wizard, {2}{U}{U}, 2/2)
/// is materialised from the embedded JSON definition
/// (<c>venser-shaper-savant.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Flash keyword marker and
/// the ETB bounce trigger are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers or a
/// "return target spell or permanent to its owner's hand" effect, so they
/// live in the factory (same posture as <see cref="StormscaleScionFactory"/>
/// and the other JSON-backed cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - 2/2 Legendary Human Wizard with <b>Flash</b> (CR 702.8) keyword marker
///   — same wiring as <see cref="TishanasTidebinderFactory"/> /
///   <see cref="SpellstutterSpriteFactory"/>.
/// - <b>ETB triggered ability</b> (CR 603.6a) via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>, declaring a single 1..1
///   "target spell or permanent" <see cref="TargetRequest"/>. The candidate
///   gatherer enumerates both every <see cref="ISpell"/> on the live stack
///   and every battlefield <see cref="Permanent"/> across all players (the
///   printed text has no controller filter — Venser can bounce friendly
///   objects too, like <see cref="CrypticCommandFactory"/>'s bounce mode and
///   Riftwing Cloudskate).
/// - <b>Resolve</b> (CR 701.10 — "return ... to its owner's hand"):
///   - <i>Spell target</i>: the spell is removed from the stack and its
///     card moved to its owner's hand. Returning a spell to hand is NOT
///     countering it (CR 701.10 vs CR 701.5) — so unlike
///     <see cref="OracleSpellBinder.RemoveFromStack"/> (which refuses an
///     uncounterable spell per CR 701.5b), the bounce here removes the spell
///     regardless of <see cref="ISpell.CannotBeCountered"/>: an uncounterable
///     spell can still be returned to hand. The card lands in its owner's
///     hand, never the graveyard.
///   - <i>Permanent target</i>: the permanent is returned to its owner's
///     hand (same path as Riftwing Cloudskate / Boomerang). CR 608.2b
///     resolution-time legality re-check: a permanent that has left the
///     battlefield no-ops.
/// - <b>No target</b> (or illegal-on-resolution target): clean no-op.
///
/// ## Deferred (v1 gaps)
/// - <b>ZoneService routing for the permanent bounce</b>: the permanent path
///   uses a raw zone move (no <see cref="Majik.Core.Services.ZoneService"/>
///   overload threaded through), so no <c>CardMovedEvent</c> /
///   replacement-bus fires for the bounce — same lossy posture as the
///   Spellstutter Sprite counter path. Acceptable for the printed observable
///   contract (target object ends up in its owner's hand). When a live
///   ZoneService is threaded through the wiring overload this becomes the
///   wiring point.
/// - <b>Triggers used for live wiring</b>: the wiring overload accepts a
///   <see cref="TriggerManager"/> so the ETB lands on the stack
///   automatically; the shape overload leaves it unregistered.
/// </summary>
[CardName("Venser, Shaper Savant")]
public static class VenserShaperSavantFactory
{
    public const string CardName = "Venser, Shaper Savant";
    public const string Slug = "venser-shaper-savant";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Venser with no live runtime services. The ETB trigger is
    /// attached for shape inspection; with a null stack the spell-return
    /// branch no-ops and the permanent branch uses a raw zone move. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, stack: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Venser.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="stack">Live stack — required for the spell-return
    /// branch to remove the targeted spell. <see langword="null"/> in
    /// pure-shape tests (the spell branch then no-ops; the permanent branch
    /// still works via a raw zone move).</param>
    /// <param name="triggers">When supplied, the ETB
    /// <see cref="TriggeredAbility"/> is registered so the enter-the-
    /// battlefield event lands it on the stack automatically.</param>
    public static Creature Create(
        Player owner,
        Majik.Core.Stack.Stack? stack,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Human Wizard, {2}{U}{U}, 2/2). The JSON carries no abilities —
        // Flash + the ETB bounce are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.8 — Flash keyword marker. Lets Venser be cast at instant
        // speed (the ActionValidator reads the marker).
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.10.
        //   "When Venser enters, return target spell or permanent to its
        //    owner's hand."
        // Single 1..1 target that may be EITHER a spell on the stack OR a
        // permanent on the battlefield (any controller — no printed
        // controller filter). Resolution-time legality re-check per
        // CR 608.2b.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName} — return target spell or permanent to its owner's hand (CR 701.10)",
            () =>
            {
                if (etb == null) return;

                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];

                switch (raw)
                {
                    case ISpell spell:
                        ReturnSpellToOwnersHand(stack, spell);
                        return;

                    case Permanent permanent:
                        ReturnPermanentToOwnersHand(permanent);
                        return;

                    // Any other shape (e.g. an activated/triggered ability
                    // on the stack) is not a legal "spell or permanent"
                    // target. Clean no-op.
                    default:
                        return;
                }
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target spell or permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // Any spell on the stack + any battlefield permanent
                    // across all players. The Bounce intent scopes the bot
                    // ranker toward opponents' objects.
                    CandidateGatherer: ctx =>
                    {
                        var candidates = new List<object>();
                        candidates.AddRange(
                            ctx.Stack.GetAll().OfType<ISpell>().Cast<object>());
                        candidates.AddRange(ctx.AllPlayers
                            .SelectMany(p => p.Zones.Battlefield.GetCards())
                            .OfType<Permanent>()
                            .Cast<object>());
                        return candidates;
                    }),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Return a spell on the stack to its owner's hand (CR 701.10). Unlike a
    /// counter (CR 701.5 — card to graveyard), the card lands in its owner's
    /// <see cref="ZoneType.Hand"/>. Crucially this is NOT a counter, so an
    /// uncounterable spell (<see cref="ISpell.CannotBeCountered"/>) is still
    /// returned — we remove it from the stack directly rather than via
    /// <see cref="OracleSpellBinder.RemoveFromStack"/>, which guards against
    /// countering uncounterable spells (CR 701.5b).
    /// </summary>
    private static void ReturnSpellToOwnersHand(Majik.Core.Stack.Stack? stack, ISpell spell)
    {
        if (stack == null) return;

        // CR 608.2b — resolution-time legality re-check: the spell must
        // still be on the stack.
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

        // CR 701.10 — move the underlying card to its owner's hand (never
        // the graveyard).
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
    /// Return a permanent to its owner's hand (CR 701.10). Same raw-zone-move
    /// path as Riftwing Cloudskate's shape fallback. CR 608.2b resolution-
    /// time legality re-check: a permanent that has left the battlefield
    /// no-ops.
    /// </summary>
    private static void ReturnPermanentToOwnersHand(Permanent permanent)
    {
        // CR 608.2b — must still be on the battlefield at resolution.
        if (permanent.Zone != ZoneType.Battlefield) return;

        var owner = permanent.Owner;
        if (owner == null) return;

        var fromController = permanent.Controller ?? owner;
        fromController.Zones.Battlefield.RemoveCard(permanent);
        owner.Zones.Hand.AddCard(permanent);
        permanent.SetZone(ZoneType.Hand);
        permanent.SetController(owner);
    }
}
