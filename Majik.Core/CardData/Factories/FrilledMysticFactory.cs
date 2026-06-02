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
/// Named-card factory for Frilled Mystic (Ravnica Allegiance, {G}{G}{U}{U}).
///
/// Creature — Elf Lizard Wizard 3/2. Oracle text (verified against Scryfall):
///   "Flash
///    When this creature enters, you may counter target spell."
///
/// ## Implemented (v1)
/// - 3/2 Creature — Elf Lizard Wizard at {G}{G}{U}{U}. The base shape
///   (name, Creature, Elf + Lizard + Wizard subtypes, mana cost, P/T) is
///   materialised from the embedded JSON definition
///   (<c>frilled-mystic.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="BeastWhispererFactory"/>. The JSON carries no abilities;
///   Flash + the ETB counter trigger are layered on here.
/// - CR 702.8 — Flash keyword marker (instant-speed cast).
/// - <b>ETB triggered ability</b> (CR 603.6a) — declares a 0..1
///   <see cref="TargetRequest"/> for "target spell". The "you may" rider is
///   encoded as <c>MinTargets = 0</c> ("up to one"): declining the counter
///   is choosing no target (CR 700.2 — optional target selection), mirroring
///   <see cref="TishanasTidebinderFactory"/>. On resolution the chosen target
///   (an <see cref="ISpell"/> on the stack, supplied via
///   <see cref="TriggeredAbility.SetChosenTargets"/>) is re-validated
///   (CR 608.2b): it must still be on the stack. If legal, the spell is
///   removed from the stack via <see cref="OracleSpellBinder.RemoveFromStack"/>
///   and its card moved to its owner's <see cref="ZoneType.Graveyard"/>
///   (CR 701.5 — countering moves a spell from the stack to its owner's
///   graveyard). Frilled Mystic has no filter — any spell qualifies.
///
/// ## Relationship to the analogue
/// Frilled Mystic is a near-functional reprint of Mystic Snake
/// (<see cref="MysticSnakeFactory"/>) — Flash + ETB counter target spell —
/// with a "may" rider. The counter-and-graveyard resolve path is cribbed
/// from Mystic Snake; the optional (MinTargets = 0) target shape is cribbed
/// from <see cref="TishanasTidebinderFactory"/>.
/// </summary>
[CardName("Frilled Mystic")]
public static class FrilledMysticFactory
{
    public const string CardName = "Frilled Mystic";
    public const string Slug = "frilled-mystic";
    public const string PrintedManaCost = "{G}{G}{U}{U}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Frilled Mystic with no runtime services. The ETB triggered
    /// ability is attached but not registered with a
    /// <see cref="TriggerManager"/>, and the counter path is a no-op without
    /// a stack. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, stack: null, triggers: null);

    /// <summary>
    /// Construct Frilled Mystic with optional runtime services.
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

        // Base shape from the embedded JSON definition (name, Creature,
        // Elf + Lizard + Wizard subtypes, {G}{G}{U}{U}, 3/2). The JSON
        // carries no abilities — Flash + the ETB trigger are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.5 (Counter).
        //   "When this creature enters, you may counter target spell."
        // The "you may" rider is encoded as a 0..1 target ("up to one"):
        // declining = choosing no target (CR 700.2). Target is supplied via
        // TriggeredAbility.SetChosenTargets — same pattern as Mystic Snake /
        // Spellstutter Sprite.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            "Frilled Mystic — you may counter target spell (CR 701.5)",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0)
                {
                    // "you may" — controller declined (no target chosen).
                    // Clean no-op (CR 700.2 — optional target selection).
                    return;
                }

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
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target spell",
                    // "you may" → "up to one" target (MinTargets = 0); the
                    // controller may decline by choosing no target.
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Counter,
                    // Agent-prompt MVP: enumerate every spell on the live
                    // stack (CR 601.2c — choose-time legality). Frilled Mystic
                    // has no filter, so all stack spells qualify. Counter
                    // intent in the bot's ranker picks the most-expensive
                    // eligible spell.
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
