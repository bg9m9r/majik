using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spellstutter Sprite (Lorwyn, {1}{U}).
///
/// Creature — Faerie Wizard 1/1. Oracle text:
///   "Flash
///    Flying
///    When Spellstutter Sprite enters, counter target spell with mana value
///    less than or equal to the number of Faeries you control."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Faerie Wizard at {1}{U} with Flash (CR 702.8) + Flying
///   (CR 702.9) keyword markers.
/// - <b>ETB triggered ability</b> (CR 603.6a) — declares a 1..1
///   <see cref="TargetRequest"/> for "target spell with mana value less than
///   or equal to the number of Faeries you control". On resolution, the
///   chosen target (an <see cref="ISpell"/> on the stack) is validated at
///   resolution time (CR 608.2b): still on the stack, mana value sampled as
///   printed + <see cref="Card.PendingCastX"/> (CR 202.3b — same shape as
///   Spell Snare / Spell Queller), and that mv must be ≤ the count of
///   <see cref="CardSubtype.Faerie"/> creatures the Sprite's controller
///   controls at resolution time. If legal, the spell is removed from the
///   stack via <see cref="OracleSpellBinder.RemoveFromStack"/> and the
///   underlying card is moved to its owner's <see cref="ZoneType.Graveyard"/>
///   zone (CR 701.5 — countering moves a spell from the stack to its
///   owner's graveyard).
///
/// ## Faerie count includes self
/// The Sprite itself is on the battlefield at the moment its ETB trigger
/// resolves (the trigger goes on the stack AFTER ETB — CR 603.6a — and is
/// resolved while the Sprite is a Faerie creature on the battlefield). So
/// the minimum effective ceiling on a "naked" Sprite is mv 1 — Spellstutter
/// counters any 1-drop. With one other Faerie out, mv ≤ 2; etc.
///
/// ## Target gathering at choose time
/// The <see cref="TargetRequest.CandidateGatherer"/> enumerates the live
/// stack at trigger-resolve time and filters to spells whose mv ≤ controller's
/// current Faerie count (CR 601.2c — choose-time legality). The bot's
/// Counter intent ranker picks the most-expensive eligible spell.
/// </summary>
[CardName("Spellstutter Sprite")]
public static class SpellstutterSpriteFactory
{
    public const string CardName = "Spellstutter Sprite";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Spellstutter Sprite with no runtime services. The ETB
    /// triggered ability is attached but not registered with a
    /// <see cref="TriggerManager"/>, and the counter path uses raw stack
    /// manipulation. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, stack: null, triggers: null);

    /// <summary>
    /// Construct Spellstutter Sprite with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="stack">When supplied, the ETB effect removes the
    /// targeted spell from the stack via
    /// <see cref="OracleSpellBinder.RemoveFromStack"/>.</param>
    /// <param name="triggers">When supplied, the ETB triggered ability is
    /// registered so the enter-the-battlefield event lands it on the stack
    /// automatically.</param>
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
            subtypes: new[] { CardSubtype.Faerie, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 702.9 — Flying. Combat blocking restriction.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.5 (Counter).
        //   "When Spellstutter Sprite enters, counter target spell with
        //    mana value less than or equal to the number of Faeries you
        //    control."
        // Target is supplied via TriggeredAbility.SetChosenTargets — same
        // pattern as Spell Queller / Snapcaster Mage.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            "Spellstutter Sprite — counter target spell with mv ≤ Faeries you control (CR 701.5)",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not ISpell spell) return;

                // CR 608.2b — illegal-on-resolution check. The target must
                // still be on the stack at resolution time.
                var targetCard = spell.Card as Card;
                if (targetCard == null) return;
                if (targetCard.Zone != ZoneType.Stack) return;

                // CR 202.3b — mana value = printed + chosen X.
                var printed = targetCard.ManaCostValue.TotalValue;
                var x = targetCard.PendingCastX ?? 0;
                var manaValue = printed + x;

                // CR 109.5 — "you" = the ability's controller. Count
                // Faeries the Sprite's controller controls at resolution.
                // The Sprite itself is a Faerie on the battlefield while
                // its ETB resolves, so the minimum count is 1.
                var faerieCount = CountFaeriesControlled(card.Controller ?? owner);
                if (manaValue > faerieCount) return;

                // CR 701.5 — counter the spell. Remove from stack and
                // place the card in its owner's graveyard.
                if (stack != null)
                {
                    OracleSpellBinder.RemoveFromStack(stack, spell);
                }

                var targetOwner = targetCard.Owner;
                if (targetOwner != null && targetCard.Zone != ZoneType.Graveyard)
                {
                    targetOwner.Zones.Graveyard.AddCard(targetCard);
                }
                targetCard.SetZone(ZoneType.Graveyard);
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
                    Description: "target spell with mana value less than or equal to the number of Faeries you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Counter,
                    // Agent-prompt MVP: enumerate stack spells whose mv is
                    // ≤ current Faerie count (CR 601.2c — choose-time
                    // legality). Counter intent in the bot's ranker picks
                    // the most-expensive eligible spell.
                    CandidateGatherer: ctx =>
                    {
                        var faerieCount = CountFaeriesControlled(card.Controller ?? owner);
                        return ctx.Stack.GetAll()
                            .OfType<Majik.Core.Spells.ISpell>()
                            .Where(s => Majik.Core.ValueObjects.ManaCost
                                .Parse(s.Card?.ManaCost ?? "").TotalValue <= faerieCount)
                            .Cast<object>()
                            .ToList();
                    }),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Count creatures with the <see cref="CardSubtype.Faerie"/> subtype on
    /// the given player's battlefield. Used both at choose-time (the
    /// <see cref="TargetRequest.CandidateGatherer"/> filters by this) and
    /// at resolution-time (the ETB effect re-samples it for the legality
    /// gate per CR 608.2b).
    /// </summary>
    private static int CountFaeriesControlled(Player controller) =>
        controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.HasSubtype(CardSubtype.Faerie));
}
