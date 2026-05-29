using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Defense Grid (Urza's Legacy — Artifact {2}).
///
/// Oracle text (verified against Scryfall):
///   "Each spell costs {3} more to cast except during its controller's turn."
///
/// The base shape (name, Artifact, {2}) is materialised from the embedded
/// JSON definition (<c>defense-grid.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The cost-increase rider is
/// layered on in the factory — the JSON <c>AbilityDefinition</c> schema
/// doesn't express a turn-conditional cost closure (same posture as
/// <see cref="DampingSphereFactory"/> / <see cref="AdaptiveAutomatonFactory"/>).
///
/// ## Implementation
///
/// ### "Each spell costs {3} more to cast except during its controller's turn."
/// (CR 117.7 / CR 601.2f.)
/// Wired as a <see cref="SpellCostIncreaseAbility"/> on the card. The base
/// effect is a flat +{3} generic on every spell (predicate matches all spells
/// — "Each spell" with no type qualifier). The exemption — "except during its
/// controller's turn" — is read at cost-calculation time:
///
/// "Its controller" is the controller of the SPELL being cast (the caster).
/// A spell is exempt only when it is cast during its own controller's turn —
/// i.e. when the caster is the active player. Per the official ruling, this
/// is the spell's controller, NOT Defense Grid's controller: Defense Grid is
/// symmetric, so even its own controller's spells are taxed when cast on an
/// opponent's turn, and any player's spells are exempt on their own turn.
///
/// The exemption is evaluated via an <c>activePlayer</c> provider captured in
/// the rider's closure (<see cref="ExtraGeneric"/> returns 0 when the caster
/// is the current active player, else 3). The provider mirrors
/// <see cref="DampingSphereFactory"/>'s captured <c>TurnState</c>: the
/// shape-only <see cref="Create(Player)"/> overload passes a null provider,
/// so the rider conservatively applies the +{3} to every spell (suitable for
/// shape / dispatch tests where the turn graph isn't exercised). Production
/// wiring should call <see cref="Create(Player, Func{Player})"/> with a
/// provider that reads the live <see cref="Majik.Core.Game.TurnManager.ActivePlayer"/>.
///
/// <see cref="CostReduction.GetEffectiveCost(ICard, Player,
/// IEnumerable{Player}?)"/> scans every player's battlefield for the rider,
/// so opposing copies of Defense Grid also tax the caster.
///
/// ## Deferred
/// - LTB unregister: the <see cref="SpellCostIncreaseAbility"/> on the card
///   becomes inert when Defense Grid is off the battlefield (the
///   <see cref="CostReduction.GetEffectiveCost"/> scanner only walks
///   battlefield permanents), so the cost rider lifts automatically without
///   an explicit unregister step.
/// - Active-player provider plumbing: the <see cref="CostReduction.GetEffectiveCost"/>
///   call sites (<see cref="Majik.Core.Game.SpellCastFlow"/>,
///   <see cref="Majik.Core.Game.TurnDriver"/>,
///   <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/>) currently call
///   the two-arg overload and dispatch builds the shape-only card; threading
///   both the all-players list AND a live active-player provider into the
///   rider is the same follow-up tracked for Damping Sphere / Sphere of
///   Resistance / Thalia.
/// </summary>
[CardName("Defense Grid")]
public static class DefenseGridFactory
{
    public const string CardName = "Defense Grid";
    public const string Slug = "defense-grid";

    /// <summary>The {3} more, per CR 117.7 / CR 601.2f.</summary>
    private const int ExtraGenericPerSpell = 3;

    /// <summary>
    /// Construct Defense Grid with the correct card shape and the cost rider
    /// attached, but no active-player context. With a null provider the rider
    /// taxes EVERY spell +{3} (the conservative fallback — no turn graph to
    /// consult), suitable for shape / dispatch tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to. Production wiring should
    /// call <see cref="Create(Player, Func{Player})"/>.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, activePlayer: null);

    /// <summary>
    /// Construct Defense Grid with the cost rider attached and the
    /// "except during its controller's turn" exemption wired to a live
    /// active-player provider. The rider reads <paramref name="activePlayer"/>
    /// at each cost-calculation: when the spell's caster is the current active
    /// player the spell is exempt (+{0}); otherwise +{3} (CR 117.7 /
    /// CR 601.2f). Pass a provider over the game's
    /// <see cref="Majik.Core.Game.TurnManager.ActivePlayer"/>.
    /// </summary>
    public static Artifact Create(Player owner, Func<Player?>? activePlayer)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (Artifact {2}) from the embedded JSON definition.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // CR 117.7 / CR 601.2f — "Each spell costs {3} more to cast except
        // during its controller's turn." Flat +{3} generic on every spell.
        // "Its controller" = the spell's controller (the caster); the
        // exemption fires when the caster is the active player. A null
        // provider means "no turn context" → never exempt (always +{3}),
        // matching DampingSphereFactory's null-TurnState fallback.
        var activePlayerProvider = activePlayer;
        card.AddAbility(new SpellCostIncreaseAbility(
            predicate: _ => true,
            extraGeneric: (_, caster) =>
            {
                var active = activePlayerProvider?.Invoke();
                // Exempt only when the spell is cast during its own
                // controller's turn (caster == active player).
                if (active != null && ReferenceEquals(active, caster))
                {
                    return 0;
                }
                return ExtraGenericPerSpell;
            },
            description: "Each spell costs {3} more to cast except during its controller's turn."));

        return card;
    }
}
