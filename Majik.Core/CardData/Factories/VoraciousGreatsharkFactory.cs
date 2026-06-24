using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Voracious Greatshark (Modern Horizons 2, {3}{U}{U}).
///
/// Creature — Shark 5/4. Oracle text (verified against Scryfall):
///   "Flash
///    When this creature enters, counter target artifact or creature spell."
///
/// ## Implemented (v1)
/// - 5/4 Creature — Shark at {3}{U}{U}. The base shape (name, Creature, Shark
///   subtype, mana cost, P/T) is materialised from the embedded JSON definition
///   (<c>voracious-greatshark.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The JSON carries no abilities;
///   Flash + the ETB counter trigger are layered on here.
/// - CR 702.8 — Flash keyword marker (instant-speed cast).
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.5) — declares a 1..1
///   <see cref="TargetRequest"/> for "target artifact or creature spell". Unlike
///   <see cref="MysticSnakeFactory"/> / <see cref="FrilledMysticFactory"/> this
///   is NOT a "you may" — the counter is mandatory (MinTargets = 1); and unlike
///   those uncounterable-anything triggers it is <b>type-filtered</b>: only
///   artifact or creature spells qualify. On resolution the chosen target (an
///   <see cref="ISpell"/> on the stack, supplied via
///   <see cref="TriggeredAbility.SetChosenTargets"/>) is re-validated
///   (CR 608.2b): it must still be on the stack AND still be an artifact or
///   creature spell. If legal, the spell is removed from the stack via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and its card moved to its
///   owner's <see cref="ZoneType.Graveyard"/> (CR 701.5 — countering moves a
///   spell from the stack to its owner's graveyard).
///
/// ## Relationship to the analogue
/// Voracious Greatshark is Mystic Snake / Frilled Mystic (Flash + ETB counter
/// target spell) with two differences: the counter is mandatory (no "may"
/// rider, so MinTargets = 1) and the legal-target filter is "artifact or
/// creature spell". The counter-and-graveyard resolve path is cribbed from
/// Mystic Snake; the artifact/creature type filter is cribbed from
/// <see cref="StrixSerenadeFactory"/> (HasType gate at choose + resolve time).
/// </summary>
[CardName("Voracious Greatshark")]
public static class VoraciousGreatsharkFactory
{
    public const string CardName = "Voracious Greatshark";
    public const string Slug = "voracious-greatshark";
    public const string PrintedManaCost = "{3}{U}{U}";
    public const int Power = 5;
    public const int Toughness = 4;

    /// <summary>
    /// CR 608.2b — the chosen target is legal for this trigger iff it is an
    /// artifact or creature spell. Used both to gather candidates at choose
    /// time (CR 601.2c) and to re-validate on resolution.
    /// </summary>
    private static bool IsArtifactOrCreatureSpell(ISpell spell)
    {
        var card = spell.Card;
        return card.HasType(CardType.Artifact)
            || card.HasType(CardType.Creature);
    }

    /// <summary>
    /// Construct Voracious Greatshark with no runtime services. The ETB
    /// triggered ability is attached but not registered with a
    /// <see cref="TriggerManager"/>, and the counter path is a no-op without a
    /// stack. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, stack: null, triggers: null);

    /// <summary>
    /// Construct Voracious Greatshark with optional runtime services.
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

        // Base shape from the embedded JSON definition (name, Creature, Shark
        // subtype, {3}{U}{U}, 5/4). The JSON carries no abilities — Flash + the
        // ETB trigger are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.5 (Counter).
        //   "When this creature enters, counter target artifact or
        //    creature spell."
        // Mandatory (MinTargets = 1, no "may" rider). Type-filtered: only
        // artifact or creature spells are legal targets. Target is supplied
        // via TriggeredAbility.SetChosenTargets — same pattern as Mystic Snake.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            "Voracious Greatshark — counter target artifact or creature spell (CR 701.5)",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not ISpell spell) return;

                // CR 608.2b — illegal-on-resolution check. The target must
                // still be on the stack AND still be an artifact or creature
                // spell at resolution time.
                var targetCard = spell.Card as Card;
                if (targetCard == null) return;
                if (targetCard.Zone != ZoneType.Stack) return;
                if (!IsArtifactOrCreatureSpell(spell)) return;

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
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or creature spell",
                    // Mandatory counter — no "may" rider (MinTargets = 1).
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Counter,
                    // Agent-prompt MVP: enumerate every artifact or creature
                    // spell on the live stack (CR 601.2c — choose-time
                    // legality). Counter intent in the bot's ranker picks the
                    // most-expensive eligible spell.
                    CandidateGatherer: ctx => ctx.Stack.GetAll()
                        .OfType<ISpell>()
                        .Where(IsArtifactOrCreatureSpell)
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }
}
