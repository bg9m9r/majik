using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mystic Snake (Onslaught, {1}{G}{U}{U}).
///
/// Creature — Snake 2/2. Oracle text:
///   "Flash
///    When this creature enters, counter target spell."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Snake at {1}{G}{U}{U} with Flash (CR 702.8).
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.5) — declares a 1..1
///   <see cref="TargetRequest"/> for "target spell". On resolution the chosen
///   target (an <see cref="ISpell"/> on the stack, selected via
///   <see cref="TriggeredAbility.SetChosenTargets"/>) is validated at
///   resolution time (CR 608.2b): it must still be on the stack. If legal,
///   the spell is removed from the stack via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and its card is moved to
///   its owner's <see cref="ZoneType.Graveyard"/> (CR 701.5 — countering
///   moves a spell from the stack to its owner's graveyard).
///
/// ## Relationship to the analogue
/// This mirrors <see cref="SpellstutterSpriteFactory"/> (a Flash creature whose
/// ETB counters a target spell) with the mana-value ceiling removed — Mystic
/// Snake counters ANY spell, no filter or rider.
/// </summary>
[CardName("Mystic Snake")]
public static class MysticSnakeFactory
{
    public const string CardName = "Mystic Snake";
    public const string PrintedManaCost = "{1}{G}{U}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Mystic Snake with no runtime services. The ETB triggered
    /// ability is attached but not registered with a
    /// <see cref="TriggerManager"/>, and the counter path uses raw stack
    /// manipulation. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, stack: null, triggers: null);

    /// <summary>
    /// Construct Mystic Snake with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="stack">When supplied, the ETB effect removes the targeted
    /// spell from the stack via
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
            subtypes: new[] { CardSubtype.Snake });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.5 (Counter).
        //   "When this creature enters, counter target spell."
        // Target is supplied via TriggeredAbility.SetChosenTargets — same
        // pattern as Spellstutter Sprite / Spell Queller.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            "Mystic Snake — counter target spell (CR 701.5)",
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

                // CR 701.5 — counter the spell. Remove from stack and place
                // the card in its owner's graveyard.
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
                    Description: "target spell",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Counter,
                    // Agent-prompt MVP: enumerate every spell on the live
                    // stack (CR 601.2c — choose-time legality). Mystic Snake
                    // has no mana-value filter, so all stack spells qualify.
                    // Counter intent in the bot's ranker picks the most-
                    // expensive eligible spell.
                    CandidateGatherer: ctx => ctx.Stack.GetAll()
                        .OfType<Majik.Core.Spells.ISpell>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }
}
