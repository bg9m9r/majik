using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kozilek, the Great Distortion (Oath of the
/// Gatewatch, {8}{C}{C}). Legendary Creature — Eldrazi 12/12. Oracle text
/// (verified against Scryfall):
///   "When you cast this spell, if you have fewer than seven cards in hand,
///    draw cards equal to the difference.
///    Menace
///    Discard a card with mana value X: Counter target spell with mana
///    value X."
///
/// The card's base shape (name, Legendary supertype, Eldrazi subtype,
/// {8}{C}{C}, 12/12) is materialised from the embedded JSON definition
/// (<c>kozilek-the-great-distortion.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (refill cast trigger, Menace, discard-X-counter-X) are layered on here —
/// the JSON <c>AbilityDefinition</c> schema doesn't yet express cast
/// triggers, keyword markers, or X-linked activated counters, so they live
/// in the factory (same posture as <see cref="StormscaleScionFactory"/> and
/// the other JSON-backed cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - <b>12/12 Legendary Creature — Eldrazi at {8}{C}{C}</b>. {C} parses as
///   +1 generic (CR 107.4c — no dedicated colourless bucket), so the mana
///   value is 10; the card is colourless (CR 105.2c — no coloured symbols).
/// - <b>Cast trigger — "refill to seven" (CR 603.6a / CR 603.10)</b>:
///   triggered ability over <see cref="SpellCastEvent"/> filtered to
///   <c>e.Spell.Card == card</c> (same self-cast detection pattern as
///   <see cref="UlamogTheCeaselessHungerFactory"/> /
///   <see cref="EmrakulTheAeonsTornFactory"/>), <c>activeZones = Stack</c>
///   because Kozilek is on the stack as a spell when the trigger fires. The
///   "if you have fewer than seven cards in hand" clause is an
///   intervening-if (CR 603.4) — re-checked on resolution. On resolution the
///   controller draws <c>7 - handCount</c> cards (CR 120 — one at a time;
///   empty-library halts the loop and stamps the CR 704.5b / 120.3 loss via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>, mirroring
///   <see cref="PsychicFrogFactory"/>'s loot loop).
/// - <b>Menace (CR 702.111)</b>: <see cref="KeywordAbility"/>("Menace")
///   marker — combat block-restriction reads it the same way every other
///   printed Menace creature does (mirrors
///   <see cref="SireOfSevenDeathsFactory"/>).
/// - <b>"Discard a card with mana value X: Counter target spell with mana
///   value X." activated ability (CR 602 / CR 701.5)</b>: wired via
///   <see cref="DiscardACardCost"/> (the discard is the sole activation
///   cost — no mana) + a 1..1 "target spell" <see cref="TargetRequest"/>.
///   X is the mana value of the discarded card (CR 602.5 — the activated
///   ability has no choice of X separate from the discarded card; X is
///   defined by the card paid as a cost). On resolution the counter is
///   gated by an mv-equality check (CR 608.2b — illegal-on-resolution): the
///   target spell is countered via <see cref="Fx.Counter"/> only when its
///   mana value equals the discarded card's mana value. Same counter-at-
///   resolution posture as <see cref="GlenElendraArchmageFactory"/>;
///   X-derived-from-cost mirrors <see cref="DrownInTheLochFactory"/>'s
///   mv-gated counter.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Cast trigger + Menace +
///   the discard-counter ability are attached; nothing registers with a
///   trigger bus; the counter is a no-op without a live stack. Suitable for
///   dispatcher / structural tests. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, Majik.Core.Stack.Stack?)"/>
///   — fully wired. The cast trigger registers with the bus; the activated
///   counter removes the target spell from the supplied stack.
///
/// ## Deferred (v1 gaps)
/// - <b>Discard prompt</b> on the activation cost (CR 701.16a — the player
///   chooses which card to discard, which in turn fixes X) — v1 lets the
///   agent nominate via <see cref="DiscardACardCost.Target"/>, else
///   deterministically discards the first card in hand. Same deferral queue
///   as Psychic Frog / Liliana of the Veil.
/// </summary>
[CardName("Kozilek, the Great Distortion")]
public static class KozilekTheGreatDistortionFactory
{
    public const string CardName = "Kozilek, the Great Distortion";
    public const string Slug = "kozilek-the-great-distortion";
    public const int Power = 12;
    public const int Toughness = 12;

    /// <summary>CR — "fewer than seven cards in hand" refill target.</summary>
    public const int HandSizeTarget = 7;

    /// <summary>
    /// Construct Kozilek with no live wiring. Cast trigger + Menace + the
    /// discard-counter ability are attached for shape; the trigger is NOT
    /// registered with any <see cref="TriggerManager"/> and the counter is a
    /// no-op (no live stack). Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, stack: null);

    /// <summary>
    /// Construct Kozilek with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the cast trigger registers with
    /// the bus so a <see cref="SpellCastEvent"/> for this card lands the
    /// refill ability on the stack automatically (CR 603.2).</param>
    /// <param name="stack">When supplied, the activated discard-counter
    /// ability removes the target spell from this stack via
    /// <see cref="Fx.Counter"/> (CR 701.5). When null the counter is a
    /// no-op (shape-only).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Creature, Eldrazi subtype, {8}{C}{C}, 12/12). The JSON carries no
        // abilities — the refill trigger / Menace / discard-counter are
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.6a / CR 603.10.
        //   "When you cast this spell, if you have fewer than seven cards in
        //    hand, draw cards equal to the difference."
        // Self-cast detection: filter SpellCastEvent on e.Spell.Card == card
        // (same pattern as Ulamog / Emrakul), active in the Stack zone
        // because Kozilek is on the stack as a spell at cast time. The
        // controller is captured off the live event. The "fewer than seven"
        // clause is an intervening-if (CR 603.4) — re-checked at resolution
        // by reading the live hand count.
        // ----------------------------------------------------------------
        Player? capturedController = null;

        var refillCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) =>
            {
                if (!ReferenceEquals(e.Spell.Card, card)) return false;
                capturedController = e.Spell.Controller;
                return true;
            });

        var refillEffect = new Effect(
            $"{CardName}: draw up to seven cards in hand (cast trigger)",
            () =>
            {
                var controller = capturedController ?? card.Controller ?? owner;

                // CR 603.4 — intervening-if re-checked at resolution: only
                // draw while below seven cards in hand.
                var deficit = HandSizeTarget - controller.Zones.Hand.GetCards().Count();
                if (deficit <= 0) return;

                // CR 120 — draw one card at a time. Empty-library halts the
                // loop and stamps the CR 704.5b / 120.3 loss condition
                // (mirrors PsychicFrogFactory's loot loop).
                for (var i = 0; i < deficit; i++)
                {
                    var top = controller.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        controller.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    controller.Zones.Library.RemoveCard(top);
                    controller.Zones.Hand.AddCard(top);
                }
            });

        var refillTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: refillCondition,
            effects: new IEffect[] { refillEffect },
            // Cast trigger fires while the spell is on the stack — same
            // active-zone posture as Ulamog / Emrakul.
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(refillTrigger);
        triggers?.RegisterTriggeredAbility(refillTrigger);

        // ----------------------------------------------------------------
        // Menace (CR 702.111) — marker; combat block-restriction reads it.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        // ----------------------------------------------------------------
        // Activated ability — "Discard a card with mana value X: Counter
        // target spell with mana value X." CR 602 + CR 701.5 + CR 608.2b.
        //
        // CR 602.5 — X is not a separately-chosen value; it is the mana
        // value of the card discarded to pay the cost. The DiscardACardCost
        // is the sole activation cost (no mana). At resolution X is read
        // off the discarded card (the cost's nominated/picked card, which
        // is now in the graveyard), and the target spell is countered only
        // when its mana value equals X (CR 608.2b illegal-on-resolution
        // gate — the engine's target prompt can't yet filter "mana value =
        // X" at cast time, so the equality is enforced in the resolve body,
        // mirroring DrownInTheLochFactory's mv gate).
        // ----------------------------------------------------------------
        ActivatedAbility? counterAbility = null;
        var discardCost = new DiscardACardCost();

        var counterEffect = new Effect(
            $"{CardName}: counter target spell with mana value X (X = discarded card's mana value)",
            () =>
            {
                if (counterAbility == null || stack == null) return;

                // X — the mana value of the card discarded as the cost
                // (CR 602.5). The cost's nominated card (or the v1 first-in-
                // hand pick) is the X-defining card; it has moved to the
                // graveyard by resolution.
                if (discardCost.Target is not Card discarded) return;
                var x = discarded.ManaCostValue.TotalValue;

                var chosen = counterAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not ISpell spell) return;
                if (spell.Card is not Card spellCard) return;

                // CR 608.2b — mv-equality gate at resolution.
                if (spellCard.ManaCostValue.TotalValue != x) return;

                // CR 701.5 — counter → top of owner's graveyard.
                Fx.Counter(stack, spell);
            });

        counterAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { discardCost },
            effects: new IEffect[] { counterEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target spell",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(counterAbility);

        return card;
    }
}
