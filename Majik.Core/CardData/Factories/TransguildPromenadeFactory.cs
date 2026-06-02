using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Transguild Promenade (Ravnica: City of Guilds).
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, sacrifice it unless you pay {1}.
///    {T}: Add one mana of any color."
///
/// <para>
/// Functionally identical to Rupture Spire — an any-colour land with an
/// enters-tapped restriction and an ETB "pay {1} or sacrifice" tax. Built
/// entirely from existing engine primitives, mirroring three cited analogues:
/// </para>
/// <list type="bullet">
/// <item><see cref="CrumblingVestigeFactory"/> — the unconditional
///   <b>enters-tapped</b> replacement (CR 614.1c) via
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>, plus the
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> ETB-self trigger shape
///   (CR 603.6e). The shape-only path (null bus) skips the registration; the
///   production load path also matches the plain "This land enters tapped."
///   clause through <see cref="Majik.Core.CardData.EntersTappedBinder"/>.</item>
/// <item><see cref="StasisFactory"/> — the <b>"sacrifice it unless you pay"</b>
///   pay-or-sacrifice tail (CR 603.1). At resolution the controller attempts
///   <see cref="Player.PayMana"/> with {1}; on failure the land is sacrificed
///   (Battlefield → Graveyard, CR 701.17). v1 "may pay" collapses to
///   pay-if-able — same posture as Stasis / Mana Vault / the pact cycle (no
///   <c>ChooseYesNoAsync</c> agent prompt yet).</item>
/// <item><see cref="ForbiddenOrchardFactory"/> — the <b>"add one mana of any
///   color"</b> ability, bound as five <see cref="ManaAbility"/> instances
///   (one per WUBRG, CR 605.1 — mana abilities don't use the stack). No {C}
///   mode and — unlike Mana Confluence / City of Brass — no pay-life / pain
///   cost: {T} alone.</item>
/// </list>
///
/// <para>
/// The Land shell (name, Land type, owner/controller) is declared
/// declaratively in <c>Majik.Core/CardData/Cards/transguild-promenade.json</c>
/// and materialized via <see cref="CardDefinitionFactory"/> — the same posture
/// as <see cref="ForbiddenOrchardFactory"/>. The any-colour mana fan-out, the
/// ETB pay-or-sacrifice trigger, and the enters-tapped restriction are
/// attached on top in C# because the data-only schema has no shape for them.
/// </para>
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for "pay {1}?"</b>: there is no "do you want to pay {1}?"
///   prompt yet — same gap as Stasis / Mana Vault / the pact cycle. v1
///   auto-pays if the controller's pool holds {1}; otherwise the sacrifice
///   tail fires. Deferred until <see cref="IPlayerAgent"/> grows a
///   ChooseYesNoAsync surface.
/// </summary>
[CardName("Transguild Promenade")]
public static class TransguildPromenadeFactory
{
    public const string CardName = "Transguild Promenade";
    public const string Slug = "transguild-promenade";
    public const string SacrificeTax = "1";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Transguild Promenade owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// the ETB trigger is attached but not registered with a
    /// <see cref="TriggerManager"/>). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Transguild Promenade with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB "sacrifice unless you
    /// pay {1}" trigger is registered so the bus surfaces it as pending when
    /// the land enters (CR 603.6e).</param>
    /// <param name="replacements">When supplied, the unconditional
    /// enters-tapped restriction (CR 614.1c) is registered against it.</param>
    public static Land Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // "This land enters tapped." — CR 614.1c. Unconditional. Shape-only
        // path (no ReplacementBus) skips registration; same posture as
        // CrumblingVestigeFactory. The production load path also matches the
        // clause via EntersTappedBinder off the oracle text.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // "When this land enters, sacrifice it unless you pay {1}." —
        // CR 603.6e (enters-the-battlefield-self trigger) + CR 603.1
        // (pay-or-sacrifice). At resolution the controller attempts to pay
        // {1}; on failure the land is sacrificed (Battlefield → Graveyard,
        // CR 701.17). v1 "may pay" auto-pays if able — same posture as Stasis.
        // ----------------------------------------------------------------
        var etbTaxEffect = new Effect(
            $"{CardName}: when this enters, sacrifice it unless you pay {{1}}",
            () =>
            {
                if (land.Zone != ZoneType.Battlefield) return;

                var controller = land.Controller ?? owner;
                var cost = ManaCost.Parse(SacrificeTax);

                if (!controller.PayMana(cost))
                {
                    // Sacrifice — Battlefield → Graveyard (CR 701.17). Raw
                    // zone move, same shape as StasisFactory's sacrifice tail.
                    controller.Zones.Battlefield.RemoveCard(land);
                    controller.Zones.Graveyard.AddCard(land);
                    land.SetZone(ZoneType.Graveyard);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbTaxEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // "{T}: Add one mana of any color." — CR 605.1 mana ability (no
        // stack). Five ManaAbility instances (one per WUBRG), same any-colour
        // fan-out as Forbidden Orchard / Mana Confluence. No {C} mode, no
        // pay-life cost: {T} alone.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: ManaCost.Parse(color)));
        }

        return land;
    }
}
