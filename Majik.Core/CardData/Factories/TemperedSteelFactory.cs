using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tempered Steel (Scars of Mirrodin, {1}{W}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Artifact creatures you control get +2/+2."
///
/// The base shape (name, single Enchantment card type, {1}{W}{W}) is
/// materialised from the embedded JSON definition
/// (<c>tempered-steel.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="HonorOfThePureFactory"/>. The artifact-type-filtered anthem is
/// layered on here because the JSON schema doesn't express continuous static
/// effects.
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, mana cost {1}{W}{W}, owner / controller wiring.
/// - <b>Artifact-creature anthem (+2/+2)</b>: "Artifact creatures you control
///   get +2/+2." Registered as a tailored <see cref="TemperedSteelAnthemEffect"/>
///   static at Layer 7c (CR 613.7c). The existing
///   <see cref="ControllerCreatureAnthemEffect"/> gates on creature COLOUR;
///   Tempered Steel needs a card-TYPE gate (Artifact), so it reuses the
///   type-filter shape of <see cref="MasterOfEtheriumLordEffect"/> (minus the
///   "Other" self-exclusion — Tempered Steel is an Enchantment, not a
///   creature, so "you control" already excludes it). Scoped to the source's
///   controller ("you control"); opponents' artifact creatures are unaffected.
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when Tempered
///   Steel isn't on the battlefield so the bonus lifts on LTB (CR 614).
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered effect stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> gates it off when the source
///   isn't on the battlefield. Same shape as Honor of the Pure.
/// - <b>Control-change re-evaluation</b>: controller is read live from the
///   source on each evaluation, so a control change of Tempered Steel itself
///   re-scopes correctly via <c>_source.Controller</c>.
/// </summary>
[CardName("Tempered Steel")]
public static class TemperedSteelFactory
{
    public const string CardName = "Tempered Steel";
    public const string Slug = "tempered-steel";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Tempered Steel without a live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the anthem is not registered,
    /// so no creatures receive +2/+2.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Tempered Steel. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="TemperedSteelAnthemEffect"/> granting +2/+2 to ARTIFACT
    /// creatures the controller controls is registered against the layers
    /// service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// artifact-creature anthem against. May be null — no live bonus.</param>
    public static Enchantment Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Enchantment, {1}{W}{W}) from the embedded JSON def.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.7c — "Artifact creatures you control get +2/+2." Layer 7c
            // P/T modification scoped to the controller's battlefield, gated on
            // the Artifact card type (CR 301 / CR 305).
            continuousEffects.Register(new TemperedSteelAnthemEffect(card));
        }

        return card;
    }
}

/// <summary>
/// Tempered Steel's "Artifact creatures you control get +2/+2" static
/// (CR 613.7c — Layer 7c).
///
/// The existing <see cref="ControllerCreatureAnthemEffect"/> filters on a
/// creature COLOUR, which doesn't fit Tempered Steel's type-level filter
/// (Artifact card type). This mirrors <see cref="MasterOfEtheriumLordEffect"/>'s
/// type filter, but Tempered Steel is an Enchantment (not a creature), so the
/// "Other" self-exclusion is unnecessary — "you control" already can't match
/// the enchantment because the receiver is a <see cref="Creature"/>.
///
/// Filter:
///   - Target is on the battlefield (CR 613.7c — continuous effects apply only
///     to permanents).
///   - Target's controller is the source's controller (CR 109.5 — "you
///     control").
///   - Target has <see cref="CardType.Artifact"/>.
/// </summary>
public sealed class TemperedSteelAnthemEffect : ContinuousEffect
{
    private readonly Permanent _source;

    public TemperedSteelAnthemEffect(Permanent source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — the permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != ZoneType.Battlefield) return false;
        // CR 109.5 — "you control" matches the source's controller.
        if (!ReferenceEquals(creature.Controller, _source.Controller)) return false;
        // CR 301 — only Artifact creatures are buffed.
        return creature.HasType(CardType.Artifact);
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += 2;
        chars.Toughness += 2;
    }

    /// <summary>
    /// Sim-only: reconstruct an identical <see cref="TemperedSteelAnthemEffect"/>
    /// bound to <paramref name="clonedSource"/> for the search-sandbox clone.
    /// All filtering reads clonedSource.Controller live (correctly remapped).
    /// preserves: nothing scalar; source → clonedSource.
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        => new TemperedSteelAnthemEffect(clonedSource);
}
