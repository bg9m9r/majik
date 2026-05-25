using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Desperate Ritual (Champions of Kamigawa,
/// {1}{R}).
///
/// Instant — Arcane. Oracle text:
///   "Add {R}{R}{R}.
///    Splice onto Arcane {1}{R} (As you cast an Arcane spell, you may
///    reveal this card from your hand and pay its splice cost. If you
///    do, add this card's effects to that spell.)"
///
/// ## Implemented (v1)
/// - Card identity: Instant with mana cost {1}{R}. (The printed subtype
///   "Arcane" is dropped in v1 — the engine has no <c>Arcane</c>
///   <see cref="Cards.Types.CardSubtype"/> member and no Splice
///   activation surface in <see cref="Majik.Core.Services.SpellCastFlow"/>
///   either, so the subtype carries no observable behaviour. Adding the
///   subtype is gated on the Splice primitive landing, same posture as
///   the Splice marker below.)
/// - Resolve effect: add three red mana to the controller's mana pool
///   (CR 605.1 / CR 106.4) via
///   <see cref="Player.AddManaToPool(ManaCost)"/>. Identical net-mana
///   profile to <see cref="PyreticRitualFactory"/>.
/// - Splice onto Arcane {1}{R} (CR 702.46): wired as a documented
///   <see cref="KeywordAbility"/> marker only — the Splice primitive
///   (alt-cost shape that copies the rider effect onto a target Arcane
///   spell as it's cast) does NOT exist in the engine yet. The marker
///   keeps the card-text auditable so a future Splice pass can scan for
///   "Splice onto Arcane" without rewriting factories. Documented as a
///   deferred mechanic in
///   <c>Majik.Core/CardData/MechanicDeps/MechanicPrimitive.cs</c>
///   ("splice-arcane").
///
/// ## Deferred (v1 gaps)
/// - <b>Splice activation</b>: the printed Splice cost is observable on
///   <see cref="SpliceCostText"/> for bot probes, but
///   <see cref="Majik.Core.Services.SpellCastFlow"/> does NOT consult it
///   when an Arcane spell is cast. Implementing Splice requires (a) the
///   Arcane subtype, (b) a cast-time "reveal + pay splice cost" prompt,
///   and (c) the ability to inject the spliced card's effects into the
///   resolving spell. Until those land, Desperate Ritual behaves as a
///   plain {R}{R}{R} ritual when cast normally.
///
/// Storm pillar — pairs with <see cref="PyreticRitualFactory"/> /
/// <see cref="DarkRitualFactory"/> / <see cref="CabalRitualFactory"/> to
/// fuel the Belcher / Past in Flames combo loops.
/// </summary>
[CardName("Desperate Ritual")]
public static class DesperateRitualFactory
{
    public const string CardName = "Desperate Ritual";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>
    /// Output: add three red mana.
    /// </summary>
    public const string ManaProduced = "RRR";

    /// <summary>
    /// Printed Splice cost: {1}{R}. Surfaced for bot probes / future
    /// Splice primitive integration. CR 702.46.
    /// </summary>
    public const string SpliceCostText = "{1}{R}";

    /// <summary>CardDef DSL — card shape only. <see cref="BuildResolveEffect"/>
    /// supplies the resolve-time {R}{R}{R} mana production. Splice onto
    /// Arcane is attached structurally as a <see cref="KeywordAbility"/>
    /// marker by <see cref="Create"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Instant)CardDefRuntime.Build(Define(), owner);

        // CR 702.46 — Splice onto Arcane {1}{R}. Marker only; the Splice
        // primitive (alt-cost + effect-merge surface in SpellCastFlow) is
        // not wired in v1. Future implementation can scan keyword markers
        // for "Splice onto Arcane" + read the printed cost off the factory
        // const. Same observational-only posture as Warp on
        // PinnacleEmissaryFactory.
        card.AddAbility(new KeywordAbility(
            keyword: "Splice onto Arcane",
            source: card,
            controller: owner));

        return card;
    }

    /// <summary>
    /// Build Desperate Ritual's resolve effect. On resolution, add three
    /// red mana to <paramref name="controller"/>'s mana pool. The Splice
    /// rider is observational metadata only — see class docs.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return new IEffect[]
        {
            new Effect("Desperate Ritual: add {R}{R}{R}.", () =>
            {
                controller.AddManaToPool(ManaCost.Parse(ManaProduced));
            }),
        };
    }
}
