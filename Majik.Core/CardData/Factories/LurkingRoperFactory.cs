using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lurking Roper (Bloomburrow, {3}{B}).
///
/// Creature — Horror 4/5. Oracle text:
///   "Forage (You may exile three cards from your graveyard or sacrifice
///    a creature. If you do, …)
///    When this creature enters, each opponent mills three cards."
///
/// ## Implemented (v1)
/// - 4/5 Creature — Horror, mana cost {3}{B}.
/// - <b>ETB triggered ability (CR 603.6a)</b>: when Lurking Roper
///   enters the battlefield, each opponent supplied by the caller
///   mills three cards (CR 701.13 — Mill is the formal name for
///   "put the top N cards of your library into your graveyard").
///   Mill routes through the shared <see cref="MillAction.Apply"/>
///   helper. Mirrors the Creeping Chill / Omnath "each opponent"
///   resolver-closure pattern.
///
/// ## Deferred (v1 gaps)
/// - <b>Forage keyword</b> (Bloomburrow mechanic): the printed Forage
///   line is attached as a <see cref="KeywordAbility"/> marker so
///   keyword-aware code (cost reductions, tutor predicates) can see
///   it, but the actual Forage cost-and-effect plumbing is not
///   wired. Forage on Lurking Roper itself has no rider effect — the
///   printed Forage line is informational (the static ability shape
///   Bloomburrow uses on cards like Roughshod Duo). Card-shape /
///   ETB-mill behaviour is intact.
/// - <b>"Each opponent" live enumeration</b>: requires an
///   <c>opponentResolver</c> closure — same shape as the Creeping
///   Chill / Omnath pattern. Without a resolver the mill half
///   silently no-ops.
/// </summary>
[CardName("Lurking Roper")]
public static class LurkingRoperFactory
{
    public const string CardName = "Lurking Roper";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 4;
    public const int Toughness = 5;
    public const int MillAmount = 3;

    /// <summary>
    /// Construct Lurking Roper with no runtime wiring. The ETB
    /// trigger is attached structurally but no opponents are mill
    /// targets. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Lurking Roper with optional runtime wiring. The ETB mill
    /// reads "each opponent" from the live resolution context at resolution
    /// (<see cref="ContextOpponents"/>), so it is correct on the production
    /// routed build.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager to register the ETB
    /// mill trigger against. May be null — the trigger is still
    /// attached structurally.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Horror });

        card.SetOwner(owner);
        card.SetController(owner);

        // Forage keyword marker (Bloomburrow). Cost-and-effect
        // plumbing is deferred; the marker lets keyword-aware code
        // (cost reductions, tutor predicates) observe the printed
        // ability.
        card.AddAbility(new KeywordAbility("Forage", card, owner));

        // ----------------------------------------------------------------
        // CR 603.6a — ETB trigger: "When this creature enters, each
        // opponent mills three cards." (CR 701.13 — Mill.)
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: each opponent mills {MillAmount}",
            ctx =>
            {
                // "Each opponent" is read from the LIVE resolution context —
                // NOT a captured resolver, which was null on the routed prod
                // build and made the mill INERT in real games (resolver-null
                // bug class; mirrors Stormbreath #2540 / Grist #2549).
                var controller = card.Controller ?? owner;
                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    MillAction.Apply(opp, MillAmount);
                }
                return ValueTask.CompletedTask;
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
