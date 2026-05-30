using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grove of the Burnwillows (Future Sight).
///
/// Land. Oracle text (Scryfall, verified):
///   "{T}: Add {C}.
///    {T}: Add {R} or {G}. Each opponent gains 1 life."
///
/// The "reverse painland": structurally identical to the Ice Age /
/// Apocalypse <see cref="PainLandCycleFactory"/> — a colourless mode plus
/// two coloured modes carrying an activation-time rider — except the
/// rider HELPS each opponent (gain 1 life) instead of hurting the
/// controller (deal 1 damage to you).
///
/// ## Implemented (v1)
/// - <b>Land identity + the {T}: Add {C} mode</b> load from the embedded
///   JSON definition
///   <c>Majik.Core/CardData/Cards/grove-of-the-burnwillows.json</c> via
///   <see cref="CardDefinitionFactory"/> — the maximally data-driven
///   portion (the {C} mode carries no rider, so it is expressible in the
///   JSON mana-ability shape).
/// - <b>{T}: Add {R}. Each opponent gains 1 life.</b> + the matching
///   <c>{G}</c> mana ability — attached in code. The printed "Add {R} or
///   {G}" modal is split into two separate <see cref="ManaAbility"/>
///   instances, the same fan-out the painland cycle and Aether Hub use:
///   the bot's source-picker iterates abilities by produced colour and
///   picks the one matching the spell it's paying for. The
///   "each opponent gains 1 life" rider rides the additional-cost overload
///   of <see cref="ManaAbility"/> (<c>additionalCostPayer</c>) — it runs
///   after the {T} tap. The rider can't live in the JSON schema (the
///   <c>ManaAbilityDefinition</c> only models <c>Produces</c> + an
///   optional mana <c>Cost</c>), so the coloured modes are wired here, in
///   code — exactly as the painland cycle wires its self-damage rider.
///
/// Each opponent is enumerated via the optional
/// <paramref name="opponentResolver"/> (the Zulaport Cutthroat / Meathook
/// convention) — single-arg <see cref="Create(Player)"/> wires no
/// resolver, so the lifegain side no-ops while the mana + tap still fire
/// (shape / dispatch tests).
///
/// CR 102.4 — "each opponent" excludes the controller; the rider skips
/// the controller even if the resolver hands back the full player list.
/// CR 119.3 — the lifegain is a discrete life-change event per opponent.
/// CR 605.1 — every mode is a mana ability and never uses the stack; the
/// lifegain rider is part of the activation, not a resolution effect.
///
/// ## Notes
/// - No life-floor / no cost gate: unlike the painland's self-damage,
///   gaining opponents life is never a "pay" cost — there is nothing to
///   gate. The coloured abilities reuse the painland's <c>!IsTapped</c>
///   activation check (the {T} tap is the only real cost).
/// </summary>
[CardName("Grove of the Burnwillows")]
public static class GroveOfTheBurnwillowsFactory
{
    public const string CardName = "Grove of the Burnwillows";
    public const int LifeGainPerOpponent = 1;

    private const string Slug = "grove-of-the-burnwillows";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Grove of the Burnwillows with no live runtime services.
    /// The coloured modes are attached but no opponent resolver is wired,
    /// so the "each opponent gains 1 life" rider is a no-op (the mana +
    /// tap still fire). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, opponentResolver: null);

    /// <summary>
    /// Construct Grove of the Burnwillows.
    /// <paramref name="opponentResolver"/> supplies the players the
    /// coloured modes grant 1 life to (typically every
    /// <c>Game.Players</c> entry; the controller is filtered out per
    /// CR 102.4). Null = the lifegain side no-ops.
    /// </summary>
    public static Land Create(Player owner, Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Land identity + the {T}: Add {C} mode come from the embedded
        // JSON definition — the data-driven slice of the card.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // {T}: Add {R}. Each opponent gains 1 life.
        // {T}: Add {G}. Each opponent gains 1 life.
        AttachOpponentGainColoredMana(land, owner, "R", opponentResolver);
        AttachOpponentGainColoredMana(land, owner, "G", opponentResolver);

        return land;
    }

    /// <summary>
    /// Attach a <c>{T}: Add &lt;color&gt;. Each opponent gains 1 life.</c>
    /// mana ability. Built via the additional-cost overload of
    /// <see cref="ManaAbility"/>: tapping pays {T}; the
    /// <c>additionalCostPayer</c> then walks the resolver-supplied
    /// opponents (CR 102.4 — controller excluded) and grants each 1 life
    /// (CR 119.3). No life-floor gate — there is nothing to pay.
    /// </summary>
    private static void AttachOpponentGainColoredMana(
        Land land, Player controller, string color, Func<IReadOnlyList<Player>>? opponentResolver)
    {
        var mana = ManaCost.Parse(color);
        land.AddAbility(new ManaAbility(
            source: land,
            controller: controller,
            manaGenerated: mana,
            canActivateCheck: () => !land.IsTapped,
            additionalCostPayer: _ =>
            {
                var opponents = opponentResolver?.Invoke();
                if (opponents == null) return;
                foreach (var opp in opponents)
                {
                    // CR 102.4 — the controller is not its own opponent.
                    if (ReferenceEquals(opp, controller)) continue;
                    opp.GainLife(LifeGainPerOpponent);
                }
            }));
    }
}
