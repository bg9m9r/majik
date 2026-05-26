using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mulldrifter (Lorwyn / Modern Masters, {4}{U}).
///
/// Creature — Elemental 2/2. Oracle text:
///   "Flying
///    When Mulldrifter enters, draw two cards.
///    Evoke {2}{U}"
///
/// CR 702.74 — Evoke. "You may cast this spell for its evoke cost. If you
/// do, it's sacrificed when it enters the battlefield." Mulldrifter is the
/// canonical Lorwyn-cycle pure-mana evoker: hard-cast {4}{U} for a 2/2
/// flier that draws two; evoke for {2}{U} to draw two and bin it. The
/// printed evoke sacrifice trigger fires on the same ETB CardMovedEvent as
/// the draw clause — both go on the stack; APNAP order is the controller's
/// choice (CR 603.3b), but the practical outcome is identical: draw 2, then
/// Mulldrifter dies (evoke) or stays (hard cast).
///
/// ## Implemented (v1)
/// - 2/2 Elemental with mana cost <see cref="PrintedManaCost"/>.
/// - <b>Flying</b> (CR 702.9) — <see cref="KeywordAbility"/> marker so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> surfaces the
///   evasion rider for the combat validator.
/// - <b>Evoke {2}{U}</b> keyword marker via <see cref="KeywordAbility"/>
///   ("Evoke"). Pure-mana evoke alt-cost is announced at cast time via
///   <see cref="Majik.Core.Costs.EvokeAlternativeCost"/>(ManaCost.Parse("{2}{U}"))
///   — non-pitch (matches <see cref="FoundationBreakerFactory"/>). The
///   printed evoke sacrifice trigger (CR 702.74b — "when this enters, if
///   its evoke cost was paid, sacrifice it") is attached here via
///   <see cref="EvokeFactory.Build"/>.
/// - <b>ETB triggered ability</b>: when Mulldrifter enters, its controller
///   draws two cards. Routed through <see cref="Fx.DrawCards"/> so the
///   replacement bus (Dredge / Alms Collector / future "draw → reveal"
///   replacements) gets a shot per-draw and an empty library stamps the
///   SBA loss flag (CR 704.5b) on draw #1 without crashing draw #2.
///
/// ## Notes
/// Evoke + ETB-draw ordering: both triggers wait for the same ETB event,
/// so they're put on the stack together. Per CR 603.3b the controller picks
/// the order — draw-first lets the cards influence subsequent decisions
/// before the sacrifice resolves, which is the universal play in real
/// games. The v1 engine puts triggers on the stack in registration order;
/// the test suite asserts the draw-then-sac outcome irrespective of
/// per-trigger stack ordering, which is what matters for correctness.
///
/// ## Deferred (v1 gaps)
/// - <b>"Draw cards" replacement bus integration</b>: <see cref="Fx.DrawCards"/>
///   already routes per-draw through <c>Player.Replacements</c> — no
///   additional plumbing here. The trigger body is a thin wrapper.
/// </summary>
[CardName("Mulldrifter")]
public static class MulldrifterFactory
{
    public const string CardName = "Mulldrifter";
    public const string PrintedManaCost = "{4}{U}";
    public const string EvokeCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int DrawAmount = 2;

    /// <summary>
    /// Construct Mulldrifter owned and controlled by <paramref name="owner"/>.
    /// Attaches the Flying + Evoke keyword markers, the evoke-sacrifice
    /// trigger (<see cref="EvokeFactory"/>), and the printed ETB "draw two
    /// cards" trigger.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Keyword markers — CR 702.9 (Flying), CR 702.74 (Evoke). Attach
        // inline so the NamedCardFactory path matches the data-driven
        // KeywordBinder result (same shape as Foundation Breaker / the
        // MH2 incarnation cycle factories).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Evoke", card, owner));

        // ----------------------------------------------------------------
        // Printed evoke sacrifice trigger (CR 702.74b).
        //   "When this creature enters, if its evoke cost was paid,
        //    sacrifice it."
        // Pure-mana evoke ({2}{U}) — alt-cost announced at cast time via
        // Majik.Core.Costs.EvokeAlternativeCost(ManaCost.Parse("{2}{U}")).
        // OnResolved flips Creature.EvokeWasPaid; the intervening-if reads
        // that flag at queue-time (CR 603.4).
        // ----------------------------------------------------------------
        card.AddAbility(EvokeFactory.Build(card));

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.6a).
        //   "When Mulldrifter enters, draw two cards."
        // Routed through Fx.DrawCards so the replacement bus + empty-
        // library SBA flag fire per CR 121.1 + CR 704.5b. No targets.
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: draw {DrawAmount} cards",
            () => Fx.DrawCards(owner, DrawAmount));

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);

        return card;
    }
}
