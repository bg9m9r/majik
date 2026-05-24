using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wrath of the Skies (Modern Horizons 3, {X}{W}{W}).
///
/// Sorcery. Oracle text:
///   "You may pay {E}{E}{E}{E} rather than pay this spell's mana cost.
///    Destroy each nonland permanent with mana value X or less."
///
/// ## Why a named factory
///
/// Two things compose here that the existing templates don't yet bind
/// together:
///
/// 1. An X-keyed all-battlefield sweep across nonland permanents
///    (same scan shape as <see cref="EngineeredExplosivesFactory"/>'s
///    "destroy each nonland permanent with mv = charge count" line, but
///    keyed on the cast-time X instead of a counter count).
/// 2. A printed energy alternative cost (CR 118.9 + CR 106.13) — "Pay
///    {E}{E}{E}{E} rather than pay this spell's mana cost". This is the
///    first card to wire <see cref="EnergyAlternativeCost"/>; the cost
///    surface is generic so future cards (anything that prints
///    "rather than pay … pay {E}…") can reuse it.
///
/// ## Implemented (v1)
///
/// - Sorcery shape, printed cost <c>{X}{W}{W}</c>.
/// - <see cref="BuildResolveEffect(Player, IReadOnlyList{Player}, int)"/>:
///   for every supplied player, snapshot the battlefield, filter to cards
///   that are NOT a <see cref="CardType.Land"/> AND whose
///   <see cref="Card.ManaCostValue"/>.<see cref="ManaCost.TotalValue"/>
///   is <c>≤ X</c>, dedupe via <see cref="HashSet{T}"/>, and route each
///   through <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7 —
///   destroyed permanents go to their owner's graveyard).
/// - <see cref="BuildSpellDefinition(Player, IReadOnlyList{Player})"/>
///   declares no <c>TargetRequest</c>s (it's a sweep, not targeted) and
///   wires <see cref="SpellDefinition.HasVariableX"/> = true so the
///   engine prompts for X at cast time. The resolve closure reads
///   <c>ChosenSpellParams.X</c> as the mana-value ceiling.
/// - Single-arg dispatcher path produces the card shape only — the
///   resolve effect / spell definition are built on demand (same posture
///   as <see cref="WrathOfGodFactory"/> / <see cref="AngerOfTheGodsFactory"/>).
///
/// ## Energy alternative cost — printed-cost / X interaction
///
/// Per CR 107.3b: when an alternative cost replaces a spell's mana cost
/// and the alternative cost does not specify a value for X, X is treated
/// as 0. Wrath of the Skies' printed energy alt-cost ("You may pay
/// {E}{E}{E}{E} rather than pay this spell's mana cost") does not specify
/// a value for X, so casting Wrath of the Skies via
/// <see cref="EnergyAlternativeCost"/> resolves with X = 0 — only
/// mv-0 nonland permanents are destroyed (i.e. tokens with no printed
/// mana cost — CR 110.5a / 107.3b). This is the strict CR reading and is
/// what the test suite exercises. Callers wiring the cast flow should
/// honour the same rule: when <see cref="EnergyAlternativeCost"/> is the
/// chosen alt-cost, skip the agent's <c>ChooseXAsync</c> prompt (or
/// supply X = 0).
///
/// ## v1 simplifications
///
/// - <b>Indestructible bypass</b>: same gap as
///   <see cref="WrathOfGodFactory"/> /
///   <see cref="EngineeredExplosivesFactory"/> —
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> doesn't yet consult
///   CR 702.12 indestructible.
/// - <b>Cast-flow integration of the agent's X-prompt skip when the
///   energy alt-cost is used</b>: <see cref="Majik.Core.Game.SpellCastFlow"/>
///   today always prompts for X when <see cref="SpellDefinition.HasVariableX"/>
///   is true. Tests bypass this by calling
///   <see cref="BuildResolveEffect(Player, IReadOnlyList{Player}, int)"/>
///   directly with X = 0. Once the cast flow grows an "alt-cost replaces
///   X" hook (CR 107.3b) the prompt-skip can move out of factory xmldoc
///   into engine code.
/// </summary>
public static class WrathOfTheSkiesFactory
{
    public const string CardName = "Wrath of the Skies";
    public const string PrintedManaCost = "{X}{W}{W}";

    /// <summary>The printed energy alternative cost — 4 energy.</summary>
    public const int EnergyAltCostAmount = 4;

    /// <summary>
    /// Build a Wrath of the Skies sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve
    /// effect via <see cref="BuildResolveEffect"/> and the spell
    /// definition via <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the printed energy alternative cost — 4 energy
    /// (<see cref="EnergyAltCostAmount"/>). Mirrors
    /// <see cref="ChordOfCallingFactory.BuildAlternativeCost"/> /
    /// <see cref="ForceOfWillFactory"/>'s alt-cost factory shape.
    /// </summary>
    public static EnergyAlternativeCost BuildAlternativeCost() =>
        new(EnergyAltCostAmount);

    /// <summary>
    /// Build Wrath of the Skies' resolve effect — destroy each nonland
    /// permanent on every supplied player's battlefield whose mana value
    /// is <c>≤ X</c>. Each victim is routed to its owner's graveyard via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7).
    /// HashSet-deduped so a permanent that somehow appears on multiple
    /// supplied battlefields (e.g. caller passed the same player twice)
    /// is only destroyed once.
    /// </summary>
    /// <param name="caster">Spell controller — used only as a default
    /// owner when a discovered victim has no <see cref="Card.Owner"/> set
    /// (shape-only tests with untyped controllers). Mirrors
    /// <see cref="PerniciousDeedFactory"/>'s <c>victimOwner ?? p</c>
    /// fallback.</param>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>.</param>
    /// <param name="x">The mana-value ceiling. Permanents with
    /// <c>ManaCostValue.TotalValue &lt;= x</c> are destroyed.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        IReadOnlyList<Player> allPlayers,
        int x)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: destroy each nonland permanent with mv ≤ {x}.",
                () =>
                {
                    // HashSet-dedupe across all supplied battlefields —
                    // protects against caller passing the same player
                    // twice (or future shared-battlefield game modes).
                    var victims = new HashSet<Card>(ReferenceEqualityComparer.Instance);

                    foreach (var pl in allPlayers)
                    {
                        // Snapshot — MoveToGraveyard mutates the source
                        // battlefield in place. Cast through OfType<Card>
                        // for ManaCostValue access (same pattern
                        // Pernicious Deed / Engineered Explosives use).
                        foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Card>().ToList())
                        {
                            if (c.HasType(CardType.Land)) continue;
                            if (c.ManaCostValue.TotalValue > x) continue;
                            victims.Add(c);
                        }
                    }

                    foreach (var v in victims)
                    {
                        OracleSpellBinder.MoveToGraveyard(v);
                    }
                }),
        };
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Wrath of the Skies uses on
    /// resolution. <see cref="SpellDefinition.HasVariableX"/> is true so
    /// the engine prompts for X at cast time; the resolve-time effect
    /// reads <c>ChosenSpellParams.X</c> as the sweep ceiling.
    ///
    /// No <see cref="TargetRequest"/>s — the printed effect is an
    /// untargeted sweep (CR 701.7 + CR 109.5 — "each [thing]").
    /// </summary>
    /// <param name="caster">Spell controller — passed through to
    /// <see cref="BuildResolveEffect"/>.</param>
    /// <param name="allPlayers">All players whose battlefields the sweep
    /// scans. Typically <c>Game.Players</c>.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p =>
            {
                var x = p.X ?? 0;
                return BuildResolveEffect(caster, allPlayers, x);
            });
    }
}
