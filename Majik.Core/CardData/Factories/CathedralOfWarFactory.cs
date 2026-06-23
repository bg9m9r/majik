using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cathedral of War (Gatecrash).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    Exalted (Whenever a creature you control attacks alone, that creature
///    gets +1/+1 until end of turn.)
///    {T}: Add {C}."
///
/// <para>
/// Combines two well-trodden shapes:
/// </para>
/// <list type="bullet">
///   <item><b>Enters-tapped {T}:Add{C} land</b> — the colourless mana ability
///     (CR 605.1) and the Land type are declared declaratively in
///     <c>Majik.Core/CardData/Cards/cathedral-of-war.json</c> and materialized
///     via <see cref="CardDefinitionFactory"/>, mirroring the JSON-driven
///     posture of <see cref="BlossomingSandsFactory"/>. The unconditional
///     "This land enters tapped" restriction (CR 614.1c) is registered as an
///     <see cref="EntersTappedReplacement"/> on the supplied
///     <see cref="ReplacementBus"/> (the production load path also matches it
///     off the oracle text via
///     <see cref="Majik.Core.CardData.EntersTappedBinder"/>).</item>
///   <item><b>Exalted (CR 702.90)</b> — wired identically to
///     <see cref="NobleHierarchFactory"/> / <see cref="IgnobleHierarchFactory"/>:
///     a <see cref="KeywordAbility"/> marker plus a
///     <see cref="TriggeredAbility"/> on <see cref="CreatureAttacksEvent"/>.
///     When exactly one creature its controller controls is attacking
///     ("attacks alone" — CR 702.90b) that creature gets +1/+1 until end of
///     turn via a <see cref="PumpUntilEndOfTurnEffect"/>.</item>
/// </list>
///
/// <para>
/// The trigger is added to the card shape with
/// <see cref="Majik.Core.Cards.Card.AddAbility"/>, so the live
/// <see cref="TriggerManager"/> auto-binds it the first time the land crosses
/// a zone boundary onto the battlefield (CR 603.6) — no explicit registration
/// is required on the production single-arg dispatch path.
/// </para>
///
/// <para>
/// ## Source-closure injection / deferred v1 gap
/// Same posture as Noble / Ignoble Hierarch: the engine does not yet expose a
/// global "currently-attacking creatures" view from inside an effect closure,
/// so the factory accepts a <c>Func&lt;IReadOnlyList&lt;Creature&gt;&gt;</c>
/// that callers / tests populate. When null (the production single-arg path)
/// the pump body short-circuits to a no-op — the trigger still fires and is
/// correctly attached to the card shape. Once a live combat-attackers provider
/// ships this closure is replaced by a direct read.
/// </para>
/// </summary>
[CardName("Cathedral of War")]
public static class CathedralOfWarFactory
{
    public const string Slug = "cathedral-of-war";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Cathedral of War owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against, and no
    /// attackers-source so the exalted pump body is a no-op). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner)
        => Create(owner, replacements: null, attackingCreaturesSource: null);

    /// <summary>Construct Cathedral of War with optional runtime services.
    /// <paramref name="replacements"/> registers the unconditional
    /// enters-tapped restriction (CR 614.1c).
    /// <paramref name="attackingCreaturesSource"/> supplies the live attacker
    /// snapshot at trigger-resolution time so the exalted "attacks alone"
    /// check can be made (CR 702.90b).</summary>
    public static Land Create(
        Player owner,
        ReplacementBus? replacements,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 614.1c — unconditional "This land enters tapped." Shape-only path
        // (no ReplacementBus) skips registration; same posture as
        // BlossomingSandsFactory. Production also matches this off the printed
        // oracle text via EntersTappedBinder.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // CR 702.90 — Exalted keyword marker so data-side tools see it.
        land.AddAbility(new KeywordAbility("Exalted", land, owner));

        // CR 702.90b — Exalted. "Whenever a creature you control attacks
        // alone, that creature gets +1/+1 until end of turn." Identical wiring
        // to Noble / Ignoble Hierarch: the trigger fires on every
        // CreatureAttacksEvent whose attacker is controlled by Cathedral of
        // War's controller; at resolution the factory reads the live attackers
        // via attackingCreaturesSource and pumps the solo attacker +1/+1 EOT.
        var exaltedEffect = new Effect(
            "Cathedral of War Exalted: +1/+1 EOT when a creature attacks alone",
            () =>
            {
                if (attackingCreaturesSource == null) return;

                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();

                // Count only creatures controlled by Cathedral of War's current
                // controller (CR 702.90b — "a creature you control attacks
                // alone" means no other controlled creatures are attacking).
                var controlledAttackers = new List<Creature>();
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    if (!ReferenceEquals(atk.Controller, land.Controller)) continue;
                    controlledAttackers.Add(atk);
                }

                // "attacks alone" — exactly 1 controlled attacker.
                if (controlledAttackers.Count != 1) return;

                var soloAttacker = controlledAttackers[0];
                if (soloAttacker.ActiveEffects == null) return;

                soloAttacker.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(soloAttacker, 1, 1));
            });

        var exaltedTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) => ReferenceEquals(e.Attacker.Controller, land.Controller)),
            effects: new IEffect[] { exaltedEffect },
            activeZones: new[] { ZoneType.Battlefield });

        // Added to the card shape — the live TriggerManager auto-binds it on
        // the first zone crossing onto the battlefield (no explicit registration
        // needed on the production single-arg dispatch path).
        land.AddAbility(exaltedTrigger);

        return land;
    }
}
