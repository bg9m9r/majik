using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Stack;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tishana's Tidebinder (Lost Caverns of Ixalan,
/// <c>{2}{U}</c>).
///
/// Creature — Merfolk Wizard 3/2. Oracle text:
///   "Flash
///    When this creature enters, counter up to one target activated or
///    triggered ability. If an ability of an artifact, creature, or
///    planeswalker is countered this way, that permanent loses all
///    abilities for as long as this creature remains on the battlefield.
///    (Mana abilities can't be targeted.)"
///
/// ## Implemented (v1)
/// - 3/2 Merfolk Wizard with Flash keyword marker (CR 702.8 — same
///   wiring as <see cref="DeceiverExarchFactory"/> /
///   <see cref="SubtletyFactory"/>).
/// - ETB <see cref="TriggeredAbility"/> (CR 603.6a) attached via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> declaring a 0..1
///   "target activated or triggered ability" <see cref="TargetRequest"/>
///   — "up to one" is encoded as <c>MinTargets = 0</c>, mirroring the
///   modal-zero shape used elsewhere when an ETB trigger may target
///   nothing.
/// - Resolve: re-checks legality (CR 608.2b) and counters the chosen
///   ability via <see cref="OracleSpellBinder.RemoveFromStack"/>
///   (CR 701.5b — countered triggered/activated abilities cease to
///   exist, no graveyard hop). Predicate accepts both
///   <see cref="ITriggeredAbility"/> and <see cref="IActivatedAbility"/>;
///   <see cref="IManaAbility"/> can't appear here because mana abilities
///   never use the stack (CR 605.1), and the "(Mana abilities can't be
///   targeted.)" reminder is satisfied structurally — there is no
///   stack-object representation of a mana ability to chose against.
/// - Opponent-ability target restriction: by oracle, Tidebinder targets
///   "an activated or triggered ability" without specifying opponent
///   control — but the printed flavour is anti-opponent. Engine matches
///   the printed text exactly (any controller's ability is a legal
///   target) so the bot / agent can self-counter a triggered ability of
///   its own if it ever wanted to. Same posture as
///   <see cref="ConsignToMemoryFactory"/>'s permissive target-controller
///   gate.
///
/// ## Deferred (v1 gaps)
/// - <b>Persistent ability-loss tied to LTB</b>: "If an ability of an
///   artifact, creature, or planeswalker is countered this way, that
///   permanent loses all abilities for as long as this creature remains
///   on the battlefield." This is a permanent-scoped ability-removing
///   continuous effect anchored to Tidebinder's lifetime on the
///   battlefield (Layer 6 — CR 613.6). The engine's existing
///   <see cref="LoseAllAbilitiesEffect"/> is creature-scoped and battlefield-
///   gated; broadening it to any permanent type (artifact / creature /
///   planeswalker) plus an LTB-anchored expiry requires a new continuous-
///   effect shape (or a generalised LTB-expiry hook on
///   <see cref="LoseAllAbilitiesEffect"/>). Same queue as Pithing Needle's
///   permanent-scoped name suppression: v1 ships the counter; the
///   ability-loss rider is documented as a follow-up so the engine has a
///   clean unit of work when the continuous-effect primitive lands.
/// - <b>Spell target</b>: the user spec hinted at countering "noncreature,
///   nonland spell an opponent controls"; that is NOT in Tidebinder's
///   printed oracle (Scryfall, LCI). The factory rejects spell targets at
///   resolution per CR 608.2b — only activated / triggered abilities are
///   legal targets.
/// - <b>Target legality at choose-time</b>: <c>LegalCandidates</c> is
///   left empty (production agent enumerates the live stack itself);
///   resolve-time recheck enforces the activated-or-triggered predicate.
/// </summary>
[CardName("Tishana's Tidebinder")]
public static class TishanasTidebinderFactory
{
    public const string CardName = "Tishana's Tidebinder";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Tishana's Tidebinder owned and controlled by
    /// <paramref name="owner"/>. The Flash keyword marker and ETB
    /// counter-an-ability trigger are attached structurally.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="stack">Live stack — required for the counter effect
    /// to remove the chosen ability. <see langword="null"/> in pure-shape
    /// tests; the counter effect becomes a no-op (the trigger still
    /// fires and resolves harmlessly).</param>
    public static Creature Create(Player owner, Majik.Core.Stack.Stack? stack = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash keyword marker.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // CR 603.6a — ETB triggered ability. 0..1 target activated or
        // triggered ability ("up to one" — MinTargets = 0).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName} — counter up to one target activated or triggered ability",
            () =>
            {
                if (etbTrigger == null) return;
                if (stack == null) return;

                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0)
                {
                    // "up to one" — controller chose no target. Clean
                    // no-op (CR 700.2 — optional target selection).
                    return;
                }

                var raw = chosen[0][0];

                // CR 608.2b — recheck legality at resolution.
                // Legal targets: activated or triggered ability still on
                // the stack. Mana abilities can't appear here (CR 605.1
                // — they don't use the stack), so the reminder text is
                // satisfied structurally.
                switch (raw)
                {
                    case ITriggeredAbility trig:
                        if (!stack.GetAll().Contains(trig)) return;
                        OracleSpellBinder.RemoveFromStack(stack, trig);
                        // CR 701.5b — countered ability ceases to exist.
                        return;

                    case IActivatedAbility act:
                        if (!stack.GetAll().Contains(act)) return;
                        OracleSpellBinder.RemoveFromStack(stack, act);
                        return;

                    // Any other shape (spell, off-stack object) is
                    // illegal per the printed predicate. Clean no-op.
                    default:
                        return;
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to one target activated or triggered ability",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
