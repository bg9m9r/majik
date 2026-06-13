using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reality Smasher (Oath of the Gatewatch, {4}{C}).
///
/// Creature — Eldrazi 5/5. Oracle text (Scryfall, verified):
///   "Trample, haste
///    Whenever this creature becomes the target of a spell an opponent
///    controls, counter that spell unless its controller discards a card."
///
/// ## Implemented (v1)
/// - 5/5 Creature — Eldrazi at {4}{C}.
/// - Trample (CR 702.19) + Haste (CR 702.10) as <see cref="KeywordAbility"/>
///   markers — same wiring shape as Slickshot Show-Off's Flying + Haste pair.
/// - <b>Ward—Discard a card (CR 702.21)</b>: shipped as a
///   <see cref="KeywordAbility"/>("Ward") marker so the discovery surface
///   stays uniform with Kappa Cannoneer / other Ward carriers, PLUS a real
///   <see cref="TriggeredAbility"/> over <see cref="TargetsChosenEvent"/>
///   (CR 702.21e — Ward is a triggered ability). The predicate gates on
///   (a) the targeting spell being controlled by an OPPONENT of Reality
///   Smasher's controller (CR 102.1 — "an opponent controls"), and
///   (b) Reality Smasher ITSELF being among the chosen targets
///   ("this creature becomes the target"). On resolution the bound
///   <see cref="WardEffect"/> charges a real <see cref="DiscardACardCost"/>
///   (CR 702.21c — a non-mana ward); if the opponent can't (or won't)
///   discard, the spell is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> (CR 701.5b — a
///   countered spell goes to its owner's graveyard).
///   Same TargetsChosenEvent attachment point + opponent-target predicate
///   shape as <see cref="UnsettledMarinerFactory"/> / Leyline of Combustion,
///   narrowed to "this creature" only and with a discard (rather than {1})
///   ward cost.
///
/// ## Notes
/// - <b>"Whenever this creature becomes the target of a SPELL" (not "or
///   ability")</b>: the printed oracle only covers spells, so the predicate
///   gates the targeting stack object on <see cref="Majik.Core.Spells.ISpell"/>.
/// - <b>Auto-discard</b>: the "unless its controller discards a card" choice
///   is auto-taken when the opponent has a card (pay-when-able, the rational
///   play). Agent-driven "may discard" prompting is deferred behind the same
///   queue as Ward / Mana Leak / the rest of the soft-counter family.
///
/// ## Wiring overloads + prod build path
/// - <see cref="Create(Player)"/> — the overload <see cref="NamedCardFactory"/>
///   dispatches to (the production <c>GameFacade.BuildDeckCard</c> path). The
///   ward trigger is ATTACHED to the card shape, so:
///   (1) the Class B trigger-wiring audit sees a resident
///   <see cref="ITriggeredAbility"/>, and
///   (2) <see cref="TriggerManager"/> auto-registers it the first time the card
///   crosses onto the battlefield (CR 603.6a — <c>CardMovedEvent</c> →
///   <c>SyncCardRegistration</c>), so the ward FIRES on a matching
///   <see cref="TargetsChosenEvent"/> in a real game with no explicit
///   registration call.
///   The counter itself reaches the LIVE stack at resolution off
///   <see cref="Majik.Core.Abilities.ResolutionContext.Game"/>
///   (<c>ctx.Game.Stack</c>, CR 608) — the
///   <see cref="Majik.Core.Services.StackResolver"/> hands every resolving
///   trigger a live <see cref="Majik.Core.Game.GameContext"/> — so the
///   counter/discard is NOT a no-op on this path (the prior gap: the effect
///   read a construction-time stack that is null here).
/// - <see cref="Create(Player, Majik.Core.Stack.Stack?, TriggerManager?)"/>
///   — explicit-wiring shape-test overload. The targeted-by trigger is
///   registered eagerly so a matching <see cref="TargetsChosenEvent"/> surfaces
///   as pending; the captured stack handle is the resolution fallback for the
///   legacy synchronous <see cref="Majik.Core.Abilities.IEffect.Execute"/>
///   path (no GameContext). Resolving via the async path uses the live
///   context stack identically to the prod path.
/// </summary>
[CardName("Reality Smasher")]
public static class RealitySmasherFactory
{
    public const string CardName = "Reality Smasher";
    public const string PrintedManaCost = "{4}{C}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>Printed Ward cost — non-mana (discard a card).</summary>
    public const string WardDiscardCost = "Discard a card";

    /// <summary>
    /// CR 702.21 — Reality Smasher's printed Ward effect, bound to the
    /// supplied <paramref name="card"/>. The ward cost is the non-mana
    /// "discard a card" rider (see <see cref="WardDiscardCost"/>), modelled
    /// via <see cref="DiscardACardCost"/>; the mana portion is
    /// <see cref="ManaCost.Zero"/>. <see cref="WardEffect.Resolve"/> charges
    /// the discard when an opponent's spell targets Reality Smasher (same
    /// posture as <see cref="KappaCannoneerFactory.BuildWardEffect"/>'s mana
    /// ward).
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, new DiscardACardCost());

    /// <summary>
    /// Construct Reality Smasher with no live wiring. Trample + Haste + Ward
    /// keyword markers and the ward <see cref="TriggeredAbility"/> are attached
    /// to the card shape; the trigger is not registered (no stack / trigger
    /// manager) so the counter is a no-op. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, stack: null, triggers: null);

    /// <summary>
    /// Construct Reality Smasher with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="stack">Live stack — required for the ward to remove the
    /// targeting spell on resolution via
    /// <see cref="OracleSpellBinder.RemoveFromStack"/>. May be null for shape
    /// tests (the trigger still fires + resolves harmlessly).</param>
    /// <param name="triggers">TriggerManager — when supplied the targeted-by
    /// trigger is registered so a matching <see cref="TargetsChosenEvent"/>
    /// surfaces as pending. May be null — the trigger is still attached to the
    /// card shape.</param>
    public static Creature Create(
        Player owner,
        Majik.Core.Stack.Stack? stack,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample. CR 702.10 — Haste. CR 702.21 — Ward
        // (printed: "discard a card"). Trample/Haste are keyword markers
        // consumed by CombatValidator / CombatAbilities; the Ward marker
        // pairs with the real triggered ability wired below.
        card.AddAbility(new KeywordAbility("Trample", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        // ----------------------------------------------------------------
        // Ward—Discard a card — CR 702.21e / 109.5 / 701.5.
        //   "Whenever this creature becomes the target of a spell an opponent
        //    controls, counter that spell unless its controller discards a
        //    card."
        //
        // Fires on TargetsChosenEvent where:
        //   (a) the targeting stack object is a SPELL (printed "a spell",
        //       NOT "a spell or ability") whose controller is an OPPONENT of
        //       Reality Smasher's controller (CR 102.1).
        //   (b) Reality Smasher ITSELF is among the chosen targets ("this
        //       creature becomes the target", CR 702.21e).
        // ----------------------------------------------------------------
        IStackObject? capturedSource = null;
        var ward = BuildWardEffect(card);

        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            var controller = card.Controller ?? owner;

            // (a) "a spell an opponent controls" (CR 102.1) — the targeting
            //     stack object must be a spell, controlled by an opponent.
            if (e.StackObject is not Majik.Core.Spells.ISpell) return false;
            var sourceController = e.StackObject.Controller;
            if (sourceController == null) return false;
            if (ReferenceEquals(sourceController, controller)) return false;

            // (b) "this creature becomes the target" — Reality Smasher itself
            //     must be one of the chosen targets.
            foreach (var t in e.Targets)
            {
                if (TargetIsThisCreature(t, card))
                {
                    capturedSource = e.StackObject;
                    return true;
                }
            }

            return false;
        });

        // CR 608 — the ward resolves through the LIVE stack handed to it via
        // ResolutionContext.Game.Stack. The production build path
        // (NamedCardFactory.Create → Create(owner)) passes NO captured stack,
        // so reading a construction-time `stack` would make the counter a
        // silent no-op in real games (the WardEffect was built but never
        // reached). Prefer the live context stack; fall back to the captured
        // `stack` only for the explicit Create(owner, stack, triggers)
        // shape-test overload (which may resolve via the legacy sync path with
        // no GameContext).
        var wardEffect = new Effect(
            $"{CardName} — counter that spell unless its controller discards a card",
            ctx =>
            {
                var source = capturedSource;
                capturedSource = null;

                var liveStack = ctx.Game?.Stack ?? stack;
                if (source == null || liveStack == null)
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                // CR 608.2b — recheck the targeting spell is still on the stack
                // at resolution. If it already left, nothing to counter.
                if (!liveStack.GetAll().Contains(source))
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                var caster = source.Controller;
                if (caster == null)
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                // CR 702.21f — "unless its controller discards a card." The
                // bound WardEffect charges the DiscardACardCost when the caster
                // can (and, in v1, auto-chooses to) pay. Resolve returns true
                // when the spell should be COUNTERED (cost not paid).
                if (!ward.Resolve(caster))
                    return System.Threading.Tasks.ValueTask.CompletedTask; // discarded → not countered.

                // CR 701.5b — counter the spell. RemoveFromStack returns false
                // for an uncounterable spell (it stays put).
                if (!OracleSpellBinder.RemoveFromStack(liveStack, source))
                    return System.Threading.Tasks.ValueTask.CompletedTask;

                // CR 701.5b — a countered SPELL goes to its owner's graveyard.
                if (source is Majik.Core.Spells.ISpell spell && spell.Card is Card spellCard)
                {
                    spellCard.SetZone(ZoneType.Graveyard);
                }

                return System.Threading.Tasks.ValueTask.CompletedTask;
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { wardEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 702.21e — is <paramref name="target"/> Reality Smasher itself? True
    /// when the target is the permanent/card shape of <paramref name="card"/>.
    /// </summary>
    private static bool TargetIsThisCreature(ITarget target, Creature card)
    {
        if (target is not Target concrete) return false;

        return concrete.TargetType switch
        {
            TargetType.Permanent or TargetType.Card =>
                ReferenceEquals(concrete.TargetObject, card),
            _ => false,
        };
    }
}
