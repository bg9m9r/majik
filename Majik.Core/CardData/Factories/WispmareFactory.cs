using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wispmare (Lorwyn, reprinted in Modern Horizons,
/// {2}{W}).
///
/// Creature — Elemental 1/3. Oracle text:
///   "Flying
///    When this creature enters, destroy target enchantment.
///    Evoke {W}"
///
/// Near-sibling of <see cref="FoundationBreakerFactory"/> (white evoke
/// Elemental with an ETB destroy trigger). Differences:
///   - Flying keyword marker (CR 702.9).
///   - The ETB destroy is <b>mandatory</b> and <b>enchantment-only</b>
///     (MinTargets = MaxTargets = 1) — not the "you may artifact or
///     enchantment" of Foundation Breaker.
///   - Pure-mana evoke cost {W}.
///
/// ## Implemented (v1)
/// - 1/3 Elemental, mana cost {2}{W}.
/// - Flying + Evoke keyword markers via <see cref="KeywordAbility"/> so the
///   data-driven importer surface lines up with the named factory (same
///   pattern as the MH2 incarnation cycle factories).
/// - Pure-mana evoke alt-cost wired via
///   <see cref="Majik.Core.Costs.EvokeAlternativeCost"/> at the call site
///   (mirrors classic non-pitch evokers like Mulldrifter). The evoke
///   sacrifice trigger (CR 702.74b) is attached here via
///   <see cref="EvokeFactory.Build"/>.
/// - <b>ETB triggered ability</b>: a mandatory "target enchantment"
///   <see cref="TargetRequest"/> (MinTargets = MaxTargets = 1). On
///   resolution, if the target is still a legal enchantment on the
///   battlefield, it is destroyed via
///   <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — Indestructible per
///   CR 702.12 cancels; regeneration per CR 701.15 consumed).
///
/// ## Deferred (v1 gaps)
/// - <b>Target-legality filter in ActionValidator</b>: the validator does
///   not yet restrict the ETB target pick to "enchantment" — the
///   resolution-time guard handles illegal targets (CR 608.2b). Same
///   posture as Foundation Breaker / Caustic Caterpillar.
/// </summary>
[CardName("Wispmare")]
public static class WispmareFactory
{
    public const string CardName = "Wispmare";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>Construct Wispmare owned and controlled by
    /// <paramref name="owner"/>. Attaches the Flying + Evoke keyword markers,
    /// the evoke-sacrifice trigger (<see cref="EvokeFactory"/>), and the
    /// printed ETB "destroy target enchantment" trigger.</summary>
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
        // KeywordBinder result (same shape as the MH2 incarnation cycle
        // factories).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Evoke", card, owner));

        // ----------------------------------------------------------------
        // Printed evoke sacrifice trigger (CR 702.74b).
        //   "When this creature enters, if its evoke cost was paid,
        //    sacrifice it."
        // Pure-mana evoke ({W}) — the alt-cost is announced at cast time via
        // Majik.Core.Costs.EvokeAlternativeCost(ManaCost.Parse("{W}")) and
        // OnResolved flips Creature.EvokeWasPaid which the intervening-if
        // below reads. See EvokeFactory.Build for the trigger body.
        // ----------------------------------------------------------------
        card.AddAbility(EvokeFactory.Build(card));

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.6a).
        //   "When this creature enters, destroy target enchantment."
        // Mandatory single target (MinTargets = MaxTargets = 1). Resolution
        // guards on a still-legal enchantment on the battlefield (CR 608.2b)
        // and routes through the Destroy reason so indestructible /
        // regeneration interact correctly.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: destroy target enchantment",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;

                // No legal target on resolution -> clean no-op (CR 608.2b —
                // the ability is removed from the stack with no effect).
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — resolution-time legality check.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Enchantment)) return;

                // CR 701.7 — destroy. Indestructible (CR 702.12) cancels;
                // active regeneration shield (CR 701.15) is consumed.
                Fx.MoveToGraveyard(target, ZoneMoveReason.Destroy);
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(etb);

        return card;
    }
}
