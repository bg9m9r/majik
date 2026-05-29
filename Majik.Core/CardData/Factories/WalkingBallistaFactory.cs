using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

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
/// ## Deferred (v1 gaps, see linked issues)
/// - <b>ETB X counters</b>: requires plumbing ChosenSpellParams.X through
///   the ZoneMoveIntent / ETB hook layer. Until that infrastructure
///   exists, Walking Ballista enters as a 0/0 with zero counters
///   (state-based actions will immediately put it in the graveyard —
///   acceptable for unit tests that pre-seed counters manually).
/// - <b>Sorcery-speed restriction on {4}</b>: JSON
///   <c>"sorcerySpeed": true</c> threads through
///   <c>CardDefinitionFactory</c> onto the runtime ActivatedAbility's
///   <c>IsSorcerySpeed</c> flag; ActionValidator gates the activation
///   on the controller's main phase + empty stack (CR 117.1a / 307.5).
/// - <b>Target prompt for ping damage</b>: emitted as
///   <c>deal_damage_stub</c> in JSON; the effect fires but does not
///   route damage to a chosen target. Full targeting requires the
///   active prompt system (ITarget / TargetResolver).
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
        (Creature)CardDefinitionFactory.Build(Definition, owner, replacements);

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

        return (Creature)CardDefinitionFactory.Build(definition, owner, replacements: null);
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
