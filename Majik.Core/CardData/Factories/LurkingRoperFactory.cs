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
/// Creature — Snake Horror 4/3. Oracle text:
///   "Forage (You may exile three cards from your graveyard or sacrifice
///    a creature. If you do, …)
///    When this creature enters, each opponent mills three cards."
///
/// ## Implemented (v1)
/// - 4/3 Creature — Snake Horror, mana cost {3}{B}.
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
    public const string PrintedManaCost = "{3}{B}";
    public const int Power = 4;
    public const int Toughness = 3;
    public const int MillAmount = 3;

    /// <summary>
    /// Construct Lurking Roper with no runtime wiring. The ETB
    /// trigger is attached structurally but no opponents are mill
    /// targets. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, opponentResolver: null);

    /// <summary>
    /// Construct Lurking Roper with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager to register the ETB
    /// mill trigger against. May be null — the trigger is still
    /// attached structurally.</param>
    /// <param name="opponentResolver">Live enumerator of "each
    /// opponent" for the mill half. Without a resolver the mill
    /// silently no-ops (same posture as Creeping Chill).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Snake, CardSubtype.Horror });

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
            () =>
            {
                var opps = opponentResolver?.Invoke();
                if (opps == null) return;
                foreach (var opp in opps)
                {
                    if (ReferenceEquals(opp, owner)) continue;
                    MillAction.Apply(opp, MillAmount);
                }
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
