using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Devoted Druid (Shadowmoor, {1}{G}).
///
/// Creature — Elf Druid 0/2. Oracle text:
///   "{T}: Add {G}.
///    Put a -1/-1 counter on Devoted Druid: Untap Devoted Druid."
///
/// ## Implemented (v1)
/// - 0/2 Creature — Elf Druid, mana cost {1}{G}.
/// - <b>Mana ability (CR 605.1)</b>: <c>{T}: Add {G}</c>. Built via the
///   single-pip <see cref="ManaAbility"/> ctor (implicit self-tap).
/// - <b>Untap activated ability (CR 602.1)</b>: cost = put a -1/-1
///   counter on Devoted Druid; effect = untap Devoted Druid. The cost
///   is the new <see cref="AddCounterCost"/> primitive; when a
///   <see cref="ReplacementBus"/> is supplied the placement routes
///   through <see cref="Majik.Core.Services.CountersService.Add"/> so
///   Vizier of Remedies (the famous Druid Combo enabler) can replace
///   the cost-side -1/-1 counter with no counter at all — the loop is
///   then arbitrarily long (CR 614.1 confirmed by official rulings on
///   Vizier of Remedies).
///
/// ## Druid Combo
/// With Vizier of Remedies on the battlefield, Devoted Druid's untap
/// cost places no counter (Vizier replaces -1/-1 placement on
/// controller's creatures with no counter). Combined with the
/// {T}: add {G} mana ability, Devoted Druid taps for {G}, untaps for
/// free, taps for {G} again — infinite green mana. Pair with Walking
/// Ballista for the classic instant-kill payoff.
///
/// ## Deferred (v1 gaps)
/// - <b>Summoning sickness for the activated untap</b>: a freshly-cast
///   Devoted Druid has summoning sickness and cannot pay {T} (CR 302.1),
///   but the untap activated ability has no {T} component — it is
///   currently activatable on turn-one of arrival regardless of
///   sickness. Matches the printed card: the untap ability has no tap
///   in its cost, so summoning sickness only gates the {T}: add {G}
///   mana ability (which is what the printed card does).
/// - <b>SBA death from -1/-1 counters</b>: Devoted Druid is 0/2; two
///   -1/-1 counters with a wired <see cref="ContinuousEffectsService"/>
///   reduce toughness to 0 and the SBA pass kills it (CR 704.5f).
///   Without a wired effects service the printed 0/2 surfaces unmodified
///   (same posture as Wall of Roots — counter-driven P/T reduction is
///   layered through ActiveEffects).
/// </summary>
[CardName("Devoted Druid")]
public static class DevotedDruidFactory
{
    public const string CardName = "Devoted Druid";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 0;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Devoted Druid with no replacement-bus wiring. The
    /// untap activated ability places its -1/-1 counter directly on
    /// the creature (no Vizier of Remedies interaction). Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Devoted Druid with optional replacement-bus wiring.
    /// When <paramref name="replacements"/> is supplied, the untap
    /// activated ability's -1/-1 counter cost routes through the bus
    /// so Vizier of Remedies (and any other "would put -1/-1 counter
    /// on a creature you control" replacement) can rewrite or cancel
    /// the placement (CR 614.1).
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Druid });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 605.1 — Mana ability: "{T}: Add {G}". No stack, no priority
        // pass. Default ctor includes the implicit self-tap cost.
        // ----------------------------------------------------------------
        card.AddAbility(new ManaAbility(card, owner, ManaCost.Parse("G")));

        // ----------------------------------------------------------------
        // CR 602.1 — Activated ability:
        //   "Put a -1/-1 counter on Devoted Druid: Untap Devoted Druid."
        // Cost = AddCounterCost(-1/-1, source=self), routed through the
        // replacement bus when supplied (CR 614.1 — Vizier of Remedies).
        // Effect = untap self.
        // ----------------------------------------------------------------
        var untapCost = new AddCounterCost(card, CounterType.MinusOneMinusOne, 1, replacements);

        var untapEffect = new Effect(
            $"{CardName}: untap self",
            () =>
            {
                if (card.IsTapped) card.Untap();
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { untapCost },
            effects: new IEffect[] { untapEffect }));

        return card;
    }
}
