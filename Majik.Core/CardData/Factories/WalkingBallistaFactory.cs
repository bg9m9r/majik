using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Walking Ballista (Kaladesh, {X}{X}) and its
/// functional reprints.
///
/// Walking Ballista is an Artifact Creature — Construct 0/0.
/// Oracle text:
///   "Walking Ballista enters the battlefield with X +1/+1 counters on it.
///    {4}: Put a +1/+1 counter on Walking Ballista. Activate only as a sorcery.
///    Remove a +1/+1 counter from Walking Ballista: It deals 1 damage to any target."
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/walking-ballista.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Both
/// activated abilities are now JSON: <c>{4}: put counter</c> and
/// <c>remove counter: deal 1 damage stub</c>.
///
/// ## Functional reprints served here
/// <list type="bullet">
///   <item><b>Walking Ballista</b> — {X}{X}, Kaladesh.</item>
///   <item><b>Assaultron Invader</b> — {X}{X}, Fallout (PIP). Byte-for-byte
///     functional reprint: same cost {X}{X}, same "Artifact Creature —
///     Construct" 0/0, identical oracle text (enters with X +1/+1
///     counters; {4}: put a +1/+1 counter; remove a +1/+1 counter: deal 1
///     damage to any target). ONLY the printed name differs.</item>
/// </list>
/// Both printed names are surfaced on the <see cref="NamedCardFactory"/>
/// dispatcher via the two <c>[CardName]</c> attributes below; the source
/// generator routes both names through <see cref="Create(Player, string)"/>.
/// Because the two cards share one JSON ability definition, the reprint is
/// produced by re-using the same <see cref="CardDefinition"/> with only its
/// <see cref="CardDefinition.Name"/> swapped (see <see cref="ForName"/>) —
/// no duplicated ability schema, no second JSON file. The runtime card's
/// name (and therefore every ability's description, which is derived from
/// <c>card.Name</c> at build time) follows the requested printed name.
///
/// ## Implemented
/// - <b>ETB X counters (CR 603.6a / CR 122.1g)</b>: on entering the
///   battlefield Walking Ballista places X +1/+1 counters on itself.
///   X is read from <see cref="Card.PendingCastX"/> (stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> at cast time right after
///   the caster's <c>ChooseXAsync</c>), then the stamp is consumed so a
///   later non-cast battlefield entry (blink, copy) doesn't reuse it —
///   such an entry leaves Walking Ballista as a 0/0 with zero counters
///   (the SBA pass per CR 704.5f immediately puts it in the graveyard).
///   Counter placement routes through <see cref="CountersService.Add"/>
///   when a <see cref="ReplacementBus"/> is supplied so Hardened Scales /
///   Doubling Season rewrite the amount before it commits (CR 614 /
///   CR 121.2). This is the same PendingCastX → ETB-counter mechanism as
///   <see cref="HangarbackWalkerFactory"/> (the closest analogue — {X}{X}
///   Artifact Creature — Construct 0/0) and <see cref="EndlessOneFactory"/>.
///   The card flags <see cref="Card.MarkSelfManagesEntersWithCounters"/>
///   so the generic EntersWithCountersBinder doesn't also register a
///   variable-X replacement and double the counters.
/// - <b>Sorcery-speed restriction on {4}</b>: JSON
///   <c>"sorcerySpeed": true</c> threads through
///   <c>CardDefinitionFactory</c> onto the runtime ActivatedAbility's
///   <c>IsSorcerySpeed</c> flag; ActionValidator gates the activation
///   on the controller's main phase + empty stack (CR 117.1a / 307.5).
///
/// ## Implemented (PLAN 01 Slice F)
/// - <b>Ping damage to any target</b>: the remove-counter ability emits a
///   real <c>deal_damage</c> effect (JSON), declaring a 1..1 "any target"
///   <see cref="Majik.Core.Players.Agents.TargetRequest"/>. The shared
///   <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline (driven by
///   <c>AbilityActivationFlow</c>) prompts the controller's agent, and the
///   effect routes 1 damage to the chosen target via
///   <see cref="Majik.Core.Primitives.Fx.DealDamageAny"/> at resolution
///   (CR 115.3 / 306.7 / 608.2b).
/// </summary>
[CardName("Walking Ballista")]
[CardName("Assaultron Invader")]
public static class WalkingBallistaFactory
{
    /// <summary>Canonical printed name (Kaladesh).</summary>
    public const string CardName = "Walking Ballista";

    /// <summary>Printed name for the <b>Assaultron Invader</b> reprint
    /// (Fallout / PIP). Functionally identical to Walking Ballista.</summary>
    public const string AssaultronInvaderCardName = "Assaultron Invader";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("walking-ballista");

    /// <summary>
    /// Construct a Walking Ballista for the given owner. The returned
    /// <see cref="Creature"/> also carries
    /// <see cref="Cards.Types.CardType.Artifact"/> (multi-type — CR 301.1 /
    /// 302.1) and both activated abilities described in the class xmldoc.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Walking Ballista with optional replacement-bus wiring.
    /// When <paramref name="replacements"/> is supplied, the JSON-driven
    /// {4}-put-+1/+1-counter activated ability is routed through
    /// <see cref="Services.CountersService.Add"/> so Hardened Scales /
    /// Doubling Season replacements can rewrite the count (CR 614).
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacements) =>
        BuildWithEtbCounters(Definition, owner, replacements);

    /// <summary>
    /// Build the card for the requested printed name. Supports the
    /// canonical <c>"Walking Ballista"</c> and the functional reprint
    /// <c>"Assaultron Invader"</c> (same {X}{X} cost, same Artifact
    /// Creature — Construct 0/0, identical abilities — only the printed
    /// name differs). Any other name is rejected; the source-generated
    /// dispatcher routes only declared <c>[CardName]</c>s here.
    ///
    /// The same shared <see cref="CardDefinition"/> is re-used with its
    /// <see cref="CardDefinition.Name"/> swapped to the requested printed
    /// name — no duplicated ability schema.
    /// </summary>
    public static Creature Create(Player owner, string cardName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(cardName);

        var definition = cardName switch
        {
            CardName => Definition,
            AssaultronInvaderCardName => ForName(AssaultronInvaderCardName),
            _ => throw new ArgumentException(
                $"WalkingBallistaFactory does not serve card name '{cardName}'.",
                nameof(cardName)),
        };

        return BuildWithEtbCounters(definition, owner, replacements: null);
    }

    /// <summary>
    /// Build the JSON-driven Walking Ballista runtime card and attach the
    /// "enters with X +1/+1 counters" ETB trigger (CR 603.6a / CR 122.1g)
    /// on top. The JSON definition supplies the two activated abilities
    /// ({4}: put a counter; remove a counter: deal 1 damage); this method
    /// adds the spell-cast-X → ETB-counter behaviour that the JSON schema
    /// doesn't yet express, mirroring <see cref="HangarbackWalkerFactory"/>
    /// and <see cref="EndlessOneFactory"/>.
    ///
    /// X is read from <see cref="Card.PendingCastX"/> (stamped by
    /// SpellCastFlow after ChooseXAsync) and the stamp is consumed so
    /// re-entries (blink, copy) don't reuse it. Counter placement routes
    /// through <see cref="CountersService.Add"/> so a supplied
    /// <see cref="ReplacementBus"/> (Hardened Scales / Doubling Season)
    /// can rewrite the count (CR 614).
    /// </summary>
    private static Creature BuildWithEtbCounters(
        CardDefinition definition, Player owner, ReplacementBus? replacements)
    {
        var card = (Creature)CardDefinitionFactory.Build(definition, owner, replacements);

        // CR 614.1d — this factory wires its own "enters with X +1/+1
        // counters" via the ETB trigger below; flag it so the generic
        // EntersWithCountersBinder does NOT also register a variable-X
        // replacement and double the counters (same posture as
        // Hangarback Walker / Endless One).
        card.MarkSelfManagesEntersWithCounters();

        // ----------------------------------------------------------------
        // ETB +1/+1 counters trigger — CR 603.6a / CR 122.1g.
        //   "Walking Ballista enters the battlefield with X +1/+1 counters
        //    on it."
        // Read PendingCastX (stamped by SpellCastFlow right after
        // ChooseXAsync), apply that many +1/+1 counters via
        // CountersService.Add (so Hardened Scales / Doubling Season rewrite
        // the amount), then clear the stamp so re-entries (blink, copy)
        // don't reuse the value. PendingCastX is null for non-cast entries
        // → 0 counters → 0/0 → SBA puts it in the graveyard (CR 704.5f),
        // matching the printed behaviour. Same pattern as Hangarback Walker
        // / Endless One.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{card.Name}: enters with X +1/+1 counters (CR 122.1g)",
            () =>
            {
                var x = card.PendingCastX ?? 0;
                if (x > 0)
                {
                    CountersService.Add(card, CounterType.PlusOnePlusOne, x, replacements);
                }
                card.ClearPendingCastX();
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Return a copy of the shared Walking Ballista
    /// <see cref="CardDefinition"/> carrying the requested printed
    /// <paramref name="name"/>. Every other field — types, subtypes, cost,
    /// P/T, and the ability list — is shared by reference (the ability
    /// schema is name-agnostic; runtime ability descriptions derive from
    /// the built card's name, which follows <see cref="CardDefinition.Name"/>).
    /// </summary>
    private static CardDefinition ForName(string name) => new()
    {
        Name = name,
        Types = Definition.Types,
        Supertypes = Definition.Supertypes,
        Subtypes = Definition.Subtypes,
        ManaCost = Definition.ManaCost,
        Power = Definition.Power,
        Toughness = Definition.Toughness,
        Loyalty = Definition.Loyalty,
        Colors = Definition.Colors,
        Abilities = Definition.Abilities,
    };
}
