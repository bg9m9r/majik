using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Avatar of Woe (Prophecy / reprints, {6}{B}{B}).
///
/// Creature — Avatar 6/5. Oracle text (Scryfall, verified):
///   "If there are ten or more creature cards total in all graveyards, this
///    spell costs {6} less to cast.
///    Fear (This creature can't be blocked except by artifact creatures and/or
///    black creatures.)
///    {T}: Destroy target creature. It can't be regenerated."
///
/// The base shape (name, Creature, Avatar subtype, {6}{B}{B}, 6/5, Fear) is
/// materialised from the embedded JSON definition (<c>avatar-of-woe.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="SpikeshotGoblinFactory"/>). The single activated ability is
/// layered on here.
///
/// ## Implemented (v1)
///
/// - 6/5 Creature — Avatar at printed cost {6}{B}{B}; owner / controller wired;
///   the Fear keyword stamped (from the JSON <c>keywords</c>).
///
/// - <b>{T}: Destroy target creature. It can't be regenerated. (CR 602 /
///   701.7)</b>: <see cref="ActivatedAbility"/> with a single
///   <see cref="AdditionalCost.Tap"/> on Avatar of Woe and a 1..1
///   target-creature request. Resolution moves the chosen creature to its
///   owner's graveyard with <see cref="ZoneMoveReason.DestroyNoRegeneration"/>
///   so a regeneration shield is NOT consumed (CR 701.15 suppressed by the "It
///   can't be regenerated." rider; CR 702.12b Indestructible still applies).
///   This is exactly the "{cost}: Destroy target &lt;X&gt;." oracle shape
///   <see cref="OracleActivatedAbilityBinder"/> reconstructs, so Agatha's Soul
///   Cauldron re-homes the REAL ability via <see cref="ActivatedAbility.RebindTo"/>
///   (the destroy targets the CHOSEN creature, never the exiled card, and the
///   {T} cost taps the bearer). The effect closure references ONLY the chosen
///   target (no "this creature" / source reference), so it is inherently
///   re-source-safe — marked <c>rebindSafe: true</c>.
///
/// ## Deferred (v1 gaps — see <see cref="KnownPartialImplementations"/>)
///
/// - <b>Conditional cost reduction</b>: "If there are ten or more creature cards
///   total in all graveyards, this spell costs {6} less to cast." Avatar of Woe
///   always costs the full {6}{B}{B}; the graveyard-count cost-reduction static
///   isn't applied. (CR 601.2f — cost reductions; not yet a data-driven static.)
///
/// - <b>Fear evasion</b>: the Fear keyword is stamped (inspectable / printed)
///   but the engine does not yet enforce Fear's block restriction (CR 702.36 —
///   "can't be blocked except by artifact and/or black creatures"). The marker
///   is present so this lights up automatically when Fear evasion is wired into
///   <c>BlockLegality</c>.
/// </summary>
[CardName("Avatar of Woe")]
public static class AvatarOfWoeFactory
{
    public const string CardName = "Avatar of Woe";
    public const string Slug = "avatar-of-woe";
    public const string PrintedManaCost = "{6}{B}{B}";
    public const int Power = 6;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Avatar of Woe owned and controlled by <paramref name="owner"/>.
    /// The {T}: Destroy target creature (can't be regenerated) activated ability
    /// is attached to the card. The ability is fully self-contained — no service
    /// wiring required.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Avatar
        // subtype, {6}{B}{B}, 6/5, Fear). The JSON carries no abilities — the
        // destroy ability is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}: Destroy target creature. It can't be regenerated.
        // CR 602 — activated ability with a single 1..1 target-creature
        // request. The {T} cost taps Avatar of Woe; resolution destroys the
        // chosen creature with DestroyNoRegeneration (CR 701.7 / 701.15 / 702.12b).
        //
        // RE-SOURCE-SAFE: the effect references ONLY the chosen target (read off
        // ctx.ChosenTargets), with no "this creature" / source reference — the
        // destroy verb is inherently re-homeable. Marked rebindSafe so Agatha's
        // Soul Cauldron re-homes the REAL ability via RebindTo (CR 707.2 /
        // 613.1f): the destroy targets the bearer's chosen creature and the {T}
        // cost taps the bearer, never the exiled Avatar of Woe. The shape is also
        // reconstructable by OracleActivatedAbilityBinder, so the fallback path
        // covers it too.
        // ----------------------------------------------------------------
        ActivatedAbility? ability = null;
        var destroyEffect = new Effect(
            $"{CardName}: destroy target creature (can't be regenerated)",
            ctx =>
            {
                if (ctx.ChosenTargets.Count == 0 || ctx.ChosenTargets[0].Count == 0)
                {
                    return ValueTask.CompletedTask;
                }

                // CR 608.2b — the target must still be a battlefield creature.
                if (ctx.ChosenTargets[0][0] is Permanent chosen
                    && chosen.Zone == ZoneType.Battlefield)
                {
                    // CR 701.7 / 701.15 — destroy; "can't be regenerated" =>
                    // DestroyNoRegeneration (no shield consumed). CR 702.12b
                    // Indestructible still cancels the move.
                    Fx.MoveToGraveyard(chosen, ZoneMoveReason.DestroyNoRegeneration);
                }

                return ValueTask.CompletedTask;
            });

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { destroyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            rebindSafe: true);

        card.AddAbility(ability);

        return card;
    }
}
