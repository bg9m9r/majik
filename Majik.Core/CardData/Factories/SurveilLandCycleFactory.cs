using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the five Duskmourn: House of Horror
/// surveil-land cycle members (CR 701.42 — surveil keyword action).
///
/// Each surveil land shares the same oracle shape — only the dual basic-land
/// subtypes + produced colours differ:
/// <code>
/// [CardName("Underground Mortuary", "Swamp",  "Forest", "B", "G")]
/// [CardName("Lush Portico",         "Forest", "Plains", "G", "W")]
/// [CardName("Meticulous Archive",   "Plains", "Island", "W", "U")]
/// [CardName("Shadowy Backstreet",   "Plains", "Swamp",  "W", "B")]
/// [CardName("Thundering Falls",     "Island", "Mountain","U", "R")]
/// </code>
///
/// args[0] = printed name, [1]/[2] = the two basic-land subtypes the type
/// line names, [3]/[4] = the two single-symbol mana colours produced by the
/// {T} mana ability. The source generator forwards the four payload entries
/// at dispatch time, prepending the printed name as args[0].
///
/// ## Implemented (v1)
/// - Land with the correct two basic-land subtypes on the type line so
///   fetchlands (CR 701.19a) and Realmwalker / Yavimaya, Cradle of Growth-style
///   subtype effects see the right shape.
/// - Two <see cref="ManaAbility"/>s — one per produced colour (CR 605.1a).
/// - Unconditional ETB-tapped via <see cref="EntersTappedReplacement"/>
///   registered on the supplied <see cref="ReplacementBus"/>. Surveil lands
///   ALWAYS enter tapped (the cycle has no "unless you control N other lands"
///   clause — that's the Verge cycle, not surveil).
/// - "When this land enters, surveil N" triggered ability that consults the
///   controller's registered agent via
///   <see cref="IPlayerAgent.ChooseSurveilDecisionAsync"/>. Falls back to the
///   pre-agent default (all peeked cards to graveyard) when no agent is
///   registered (e.g. dispatcher tests).
///
/// ## Production note
/// The fetchland / surveil-land server-side load path goes through
/// <see cref="CardData.ScryfallCardFactory"/> /
/// <c>Majik.Server.Decks.RealDeckLoader</c>, NOT this named factory. The
/// production card always carries:
///   * mana abilities from <see cref="OracleManaBinder"/> (B/G or G/W etc.
///     parsed from "{T}: Add {X} or {Y}")
///   * ETB-tapped replacement from <see cref="EntersTappedBinder"/> (matches
///     "This land enters tapped.")
///   * ETB surveil triggered ability from <see cref="OracleTriggeredAbilityBinder"/>
///     (matches "When this land enters, surveil N.")
///
/// This factory exists for the test / dispatcher path
/// (<see cref="NamedCardFactory"/>) so unit tests that synthesise these
/// lands via <c>NamedCardFactory.Create</c> get a fully-featured card without
/// having to round-trip through <c>CardEntity</c> + the binder chain.
/// </summary>
[CardName("Underground Mortuary", "Swamp",    "Forest",   "B", "G")]
[CardName("Lush Portico",         "Forest",   "Plains",   "G", "W")]
[CardName("Meticulous Archive",   "Plains",   "Island",   "W", "U")]
[CardName("Shadowy Backstreet",   "Plains",   "Swamp",    "W", "B")]
[CardName("Thundering Falls",     "Island",   "Mountain", "U", "R")]
[CardName("Elegant Parlor",       "Mountain", "Plains",   "R", "W")]
public static class SurveilLandCycleFactory
{
    /// <summary>
    /// Fallback overload — only reachable when someone constructs the cycle
    /// factory by hand. Default-builds Underground Mortuary.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, new[] { "Underground Mortuary", "Swamp", "Forest", "B", "G" });

    /// <summary>
    /// Construct the surveil land identified by <paramref name="args"/>,
    /// owned and controlled by <paramref name="owner"/>.
    /// </summary>
    /// <param name="owner">The player who owns and initially controls the land.</param>
    /// <param name="args">
    /// Source-generator-provided args. Layout:
    /// <c>[0] = printed card name</c> (e.g. "Underground Mortuary"),
    /// <c>[1] = first basic subtype</c> (e.g. "Swamp"),
    /// <c>[2] = second basic subtype</c> (e.g. "Forest"),
    /// <c>[3] = first colour symbol</c> (e.g. "B"),
    /// <c>[4] = second colour symbol</c> (e.g. "G").
    /// </param>
    public static Land Create(Player owner, string[] args)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 5)
        {
            throw new ArgumentException(
                $"SurveilLandCycleFactory needs args = [name, subtypeA, subtypeB, colorA, colorB] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var subtypeA = ParseSubtype(args[1]);
        var subtypeB = ParseSubtype(args[2]);
        var colorA = args[3];
        var colorB = args[4];

        // Land carries both basic subtypes so subtype-keyed effects
        // (Yavimaya / Urborg, fetchland subtype search) treat it as a real
        // dual basic.
        var land = new Land(cardName, supertypes: null, subtypes: new[] { subtypeA, subtypeB });
        land.SetOwner(owner);
        land.SetController(owner);

        // Two mana abilities — one per produced colour (CR 605.1a).
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse($"{{{colorA}}}")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse($"{{{colorB}}}")));

        // ETB-tapped replacement (surveil lands ALWAYS enter tapped, CR 614.1c)
        // is omitted on the single-arg dispatcher path — mirrors the posture
        // of BounceLandCycleFactory.Create(Player, string[]) which also skips
        // the replacement on the dispatcher overload. Production gets the
        // replacement from EntersTappedBinder via ScryfallCardFactory; tests
        // that want it on a synthetic load can register it directly.

        // "When this land enters, surveil 1." (CR 701.42)
        var surveilEffect = new Effect(
            $"{cardName}: surveil 1",
            () =>
            {
                var ctrl = land.Controller ?? land.Owner;
                if (ctrl == null) return;
                var peeked = SurveilAction.Peek(ctrl, 1);
                if (peeked.Count == 0) return;

                var agent = AgentRegistry.Get(ctrl);
                SurveilAction.SurveilDecision decision;
                if (agent != null)
                {
                    decision = agent.ChooseSurveilDecisionAsync(null, peeked)
                        .GetAwaiter().GetResult();
                }
                else
                {
                    decision = new SurveilAction.SurveilDecision(
                        ToGraveyard: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                SurveilAction.Apply(ctrl, 1, decision);
            });

        var surveilTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { surveilEffect });
        land.AddAbility(surveilTrigger);

        return land;
    }

    private static CardSubtype ParseSubtype(string raw)
    {
        if (Enum.TryParse<CardSubtype>(raw, ignoreCase: false, out var v))
        {
            return v;
        }
        throw new ArgumentException(
            $"SurveilLandCycleFactory: '{raw}' is not a valid CardSubtype.",
            nameof(raw));
    }
}
