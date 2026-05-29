using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hedron Archive (Battle for Zendikar, {4}).
///
/// Artifact. Oracle text:
///   "{T}: Add {C}{C}.
///    {2}, {T}, Sacrifice this artifact: Draw two cards."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {4}, owner / controller wiring).
/// - <b>{T}: Add {C}{C}</b> — single <see cref="ManaAbility"/> (CR 605.1).
///   <see cref="ManaCost.Parse"/> folds {C}{C} into the generic bucket per
///   CR 107.4c, yielding two colourless. Same shape as Mind Stone's
///   tap-for-colourless body, doubled.
/// - <b>{2}, {T}, Sacrifice this artifact: Draw two cards</b> — a
///   sorcery-shape <see cref="ActivatedAbility"/> with three costs:
///   <see cref="ManaCostCost"/>("{2}") for the mana pip,
///   <see cref="AdditionalCost.Tap"/> on the artifact, and
///   <see cref="AdditionalCost.Sacrifice"/> on the artifact itself.
///   Resolution sacrifices the artifact (battlefield → owner's graveyard)
///   and draws two cards via <see cref="Fx.DrawCards"/>. Empty library is a
///   silent no-op for the unavailable draws (SBAs handle the loss flag via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>, CR 704.5b).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move directly so behaviour
///   is observable — same posture as Mind Stone / Pyrite Spellbomb. Remove
///   the explicit move-to-graveyard once <see cref="AdditionalCost.Pay"/>
///   performs the sacrifice itself.
/// </summary>
[CardName("Hedron Archive")]
public static class HedronArchiveFactory
{
    public const string CardName = "Hedron Archive";
    public const string PrintedManaCost = "{4}";

    /// <summary>
    /// Construct Hedron Archive owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var archive = new Artifact(CardName, PrintedManaCost);
        archive.SetOwner(owner);
        archive.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}{C}.
        // CR 605.1 — mana abilities don't use the stack. {C}{C} folds into
        // the generic bucket via ManaCost.Parse (CR 107.4c) → two colourless.
        // ----------------------------------------------------------------
        archive.AddAbility(new ManaAbility(archive, owner, ManaCost.Parse("CC")));

        // ----------------------------------------------------------------
        // {2}, {T}, Sacrifice this artifact: Draw two cards.
        // CR 602 — activated ability with three costs (mana pip, tap, sac).
        // Empty library marks the loss flag via Fx.DrawCards (CR 120.3 /
        // 704.5b) without throwing.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            "Hedron Archive: draw two cards + sac self",
            () =>
            {
                SacrificeSelf(archive, owner);
                Fx.DrawCards(owner, 2);
            });

        var drawAbility = new ActivatedAbility(
            source: archive,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(archive),
                AdditionalCost.Sacrifice(archive),
            },
            effects: new IEffect[] { drawEffect });

        archive.AddAbility(drawAbility);

        return archive;
    }

    /// <summary>
    /// Move <paramref name="archive"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if already off the battlefield.
    /// Mirrors the closure used by Mind Stone / Pyrite Spellbomb.
    /// </summary>
    private static void SacrificeSelf(Artifact archive, Player owner)
    {
        if (archive.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(archive);
        owner.Zones.Graveyard.AddCard(archive);
        archive.SetZone(ZoneType.Graveyard);
    }
}
