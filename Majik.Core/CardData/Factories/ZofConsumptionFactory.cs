using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Zof Consumption // Zof Bloodbog (Zendikar Rising, {4}{B}{B}).
///
/// Sorcery. Oracle text (front, verified against Scryfall):
///   "Each opponent loses 4 life and you gain 4 life."
///
/// Back face — <see cref="ZofBloodbogFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {B}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="MalakirRebirthFactory"/> / <see cref="MalakirMireFactory"/> and
/// <see cref="AgadeemsAwakeningFactory"/> /
/// <see cref="AgadeemTheUndercryptFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>zof-consumption.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time drain body are attached in code (the JSON
/// schema models neither MDFC faces nor the each-opponent drain effect).
///
/// ## Implemented (v1)
///
/// - Sorcery identity at <c>{4}{B}{B}</c>, mono-black, owner / controller
///   wired (from JSON).
/// - <see cref="MdfcState"/> attached (front = "Zof Consumption",
///   back = "Zof Bloodbog"); starts on the front face, with a castable
///   land back-face descriptor (cast-either-face).
/// - <b>Drain body</b> exposed via <see cref="BuildResolveEffect"/>: each
///   opponent supplied by the caller loses 4 life, then the controller gains
///   4 life (CR 119.3 — "loses life" / "gains life", NOT damage; routes
///   through <see cref="Fx.LoseLife"/> / <see cref="Fx.GainLife"/> so no
///   damage-prevention / lifelink / "if a source would deal damage"
///   replacement engages). Same resolver-driven "each opponent" posture as
///   <see cref="CreepingChillFactory.BuildResolveEffect"/> — the engine has
///   no first-class "each opponent" iterator without a supplied list; the
///   controller is defensively excluded from the drain even if handed back in
///   the list.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Each opponent" live enumeration</b>: the caller threads the live
///   opponent list at cast time (from the <see cref="Majik.Core.Game.GameContext"/>),
///   same shape as Creeping Chill / Omnath / The Meathook Massacre. With no
///   opponents supplied the drain half no-ops; the "you gain 4 life" half
///   always fires.
///
/// ## References
///
/// - <see cref="CreepingChillFactory"/> — the resolver-driven "each opponent"
///   + controller-life-gain body this factory mirrors (routed through
///   <see cref="Fx.LoseLife"/> instead of damage, per the printed "loses
///   life").
/// - <see cref="MalakirRebirthFactory"/> — companion ZNR black MDFC
///   spell // land pair (JSON-loaded identity + code-attached MdfcState +
///   castable land back-face descriptor).
/// </summary>
[CardName("Zof Consumption")]
public static class ZofConsumptionFactory
{
    public const string CardName = "Zof Consumption";
    public const string BackName = "Zof Bloodbog";

    /// <summary>Life each opponent loses on resolution (CR 119.3).</summary>
    public const int LifeLoss = 4;

    /// <summary>Life the controller gains on resolution (CR 119.3).</summary>
    public const int LifeGain = 4;

    /// <summary>
    /// Construct Zof Consumption as a Sorcery (identity from JSON) with the
    /// <see cref="MdfcState"/> face tracker attached (front face, with a
    /// castable land back-face descriptor).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("zof-consumption");
        var card = (Sorcery)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker WITH a castable
        // back-face descriptor. The back face is the LAND back face played
        // with no stack; MdfcCastFlow offers the controller a face choice at
        // cast time and materializes a fresh back-face land instance when
        // chosen. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, _) => ZofBloodbogFactory.Create(landOwner));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the cast-resolve effect: each opponent in <paramref name="opponents"/>
    /// loses <see cref="LifeLoss"/> (4) life, then the <paramref name="controller"/>
    /// gains <see cref="LifeGain"/> (4) life.
    ///
    /// CR 119.3 — "loses life" / "gains life", NOT damage. Routing through
    /// <see cref="Fx.LoseLife"/> / <see cref="Fx.GainLife"/> (rather than
    /// <see cref="Fx.DealDamageAny"/>) means damage-prevention shields,
    /// lifelink, and "if a source would deal damage" replacement effects
    /// never engage — the rules distinction between a drain and a burn spell.
    ///
    /// The controller is defensively excluded from the drain even if handed
    /// back in <paramref name="opponents"/> (CR 109.5 — "each opponent" never
    /// includes the spell's controller).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player controller,
        IReadOnlyList<Player> opponents)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(opponents);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: each opponent loses {LifeLoss} life + you gain {LifeGain} life",
                () =>
                {
                    foreach (var opp in opponents)
                    {
                        // CR 109.5 — "each opponent" excludes the controller.
                        if (ReferenceEquals(opp, controller)) continue;
                        Fx.LoseLife(opp, LifeLoss);
                    }

                    Fx.GainLife(controller, LifeGain);
                }),
        };
    }
}
