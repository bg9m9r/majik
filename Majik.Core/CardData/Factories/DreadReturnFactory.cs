using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dread Return (Future Sight, {2}{B}{B}).
///
/// Sorcery. Oracle text:
///   "Return target creature card from your graveyard to the battlefield.
///    Flashback—Sacrifice three creatures."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}{B}.
/// - Resolve effect via <see cref="BuildResolveEffect"/>: target creature
///   card from the CASTER's own graveyard (CR 700.6) → caster's
///   battlefield. v1 deterministic pick = first creature card in the
///   caster's graveyard (same shape as <see cref="ReanimateFactory"/>;
///   real targeting prompt is deferred system-wide). Routes through
///   <see cref="ZoneService.MoveCard"/> when supplied so ETB triggers
///   fire (CR 603.6a).
/// - Flashback alt-cost: mana portion is <see cref="ManaCost.Zero"/>
///   (Dread Return's flashback is a non-mana cost), shipped alongside
///   <see cref="SacrificeThreeCreaturesAdditionalCost"/> as the rider
///   <see cref="SpellCastFlow"/> threads via <c>additionalCosts</c>.
///   Same packaging convention as <see cref="CabalTherapyFactory"/>.
///   The flashback cost's <c>OnResolved</c> hook exiles the card from
///   graveyard (CR 702.34b).
///
/// ## Deferred (v1 gaps)
/// - <b>Real targeting prompt</b>: "target creature card from your
///   graveyard" needs an agent-driven graveyard picker. v1 picks the
///   first creature card in the caster's graveyard deterministically.
/// - <b>Flashback-with-sacrifice as a single cost</b>: engine's
///   <see cref="IAlternativeCost"/> surface only carries the mana
///   portion, so the sacrifice rider rides as a paired additional cost.
///   Same gap as Cabal Therapy.
/// - <b>Per-creature sacrifice choice</b>: the rider deterministically
///   sacrifices the first three creatures the caster controls (no
///   agent prompt). Same deferral as
///   <see cref="SacrificeACreatureAdditionalCost"/>.
/// </summary>
[CardName("Dread Return")]
public static class DreadReturnFactory
{
    public const string CardName = "Dread Return";
    public const string PrintedManaCost = "{2}{B}{B}";

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "Return target creature card from your graveyard to the battlefield.\n" +
        "Flashback—Sacrifice three creatures.";

    /// <summary>
    /// Build a Dread Return sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.
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
    /// Build Dread Return's resolve effect — reanimate target creature
    /// card from the caster's own graveyard.
    /// </summary>
    /// <param name="caster">Spell controller. Destination battlefield +
    /// graveyard source (CR 700.6 — "your graveyard").</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers fire (CR 603.6a).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            Fx.Inline(
                $"{CardName}: reanimate target creature card from your graveyard",
                () => Resolve(caster, zoneService)),
        };
    }

    /// <summary>
    /// Shared resolve helper — picks the first creature card in the
    /// caster's graveyard, moves it to the caster's battlefield under
    /// the caster's control. CR 117.x — no legal target → no-op.
    /// </summary>
    private static void Resolve(Player caster, ZoneService? zoneService)
    {
        var pick = caster.Zones.Graveyard.GetCards()
            .OfType<Creature>()
            .FirstOrDefault();
        if (pick == null) return;

        // CR 701.20 — graveyard → battlefield under caster's control.
        Fx.ReturnFromGraveyardToBattlefield(pick, caster, zoneService);
    }

    /// <summary>
    /// Build the flashback alt-cost. Dread Return's flashback cost is
    /// the non-mana "Sacrifice three creatures" — so the alt-cost mana
    /// portion is <see cref="ManaCost.Zero"/>, and the sacrifice rider
    /// ships via <see cref="BuildFlashbackAdditionalCosts"/>. Mirrors
    /// <see cref="CabalTherapyFactory.BuildFlashbackCost"/>.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost() =>
        new FlashbackAlternativeCost(ManaCost.Zero);

    /// <summary>
    /// Build the additional-cost rider that accompanies the flashback
    /// alt-cost — "Sacrifice three creatures" (CR 601.2f / CR 702.34).
    /// Returned as a single-element list to match the shape
    /// <see cref="SpellCastFlow"/> threads through its
    /// <c>additionalCosts</c> parameter.
    /// </summary>
    public static IReadOnlyList<IAdditionalCost> BuildFlashbackAdditionalCosts() =>
        new IAdditionalCost[] { new SacrificeThreeCreaturesAdditionalCost() };
}

/// <summary>
/// "As an additional cost to cast this spell, sacrifice three creatures."
/// (Dread Return flashback rider — CR 601.2f / CR 702.34.)
///
/// v1 picks deterministically: the first three creatures the caster
/// controls, in battlefield iteration order. Atomic — either all three
/// can be paid or none are.
/// </summary>
public sealed class SacrificeThreeCreaturesAdditionalCost : IAdditionalCost
{
    public IReadOnlyList<Creature> Sacrificed { get; private set; } = Array.Empty<Creature>();

    public string Description => "sacrifice three creatures";

    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Take(3)
            .Count() == 3;
    }

    public bool Pay(Player caster)
    {
        if (caster == null) return false;

        // Snapshot the picks before mutation so the iteration is stable
        // (matches the snapshot pattern in CabalTherapy's discard sweep).
        var picks = caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Take(3)
            .ToList();
        if (picks.Count < 3) return false;

        foreach (var pick in picks)
        {
            caster.Zones.Battlefield.RemoveCard(pick);
            caster.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
        }
        Sacrificed = picks;
        return true;
    }
}
