using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Treasure Vault (The Brothers' War).
///
/// Artifact Land. Oracle text (Scryfall, verified 2026-06-02):
///   "{T}: Add {C}.
///    {X}{X}, {T}, Sacrifice this land: Create X Treasure tokens."
///
/// The "mana rock that becomes a pile of Treasures" land: it taps for {C}
/// every turn like any artifact-land mana base, then late-game cashes itself
/// out into X Treasure tokens for {X}{X}. Combines an artifact-land mana
/// shell (same {C} base as <see cref="MirrodinsCoreFactory"/> /
/// <see cref="DarksteelCitadelFactory"/>) with the existing Treasure-token
/// mint (<see cref="TokenFactory.CreateTreasure"/>, the same one
/// <see cref="StrikeItRichFactory"/> and <see cref="TirelessProvisionerFactory"/>
/// use). The X-cost activation mirrors <see cref="BlastZoneFactory"/>'s
/// {X}{X}, {T} ability — an <c>xValueProvider</c> sampled at resolution.
///
/// ## Implementation
///
/// Card identity (Artifact Land, no supertype / subtype) is loaded from
/// <c>Majik.Core/CardData/Cards/treasure-vault.json</c>
/// (<c>"types": ["Land", "Artifact"]</c>) through
/// <see cref="CardDefinitionFactory"/>: the first listed type (Land) picks
/// the runtime C# class, and the additional Artifact type is flagged so the
/// HasType-based Affinity / metalcraft / artifact-removal accounting all see
/// it (CR 301.1 / 305.1 — same posture as Tanglepool Bridge's
/// <c>["Land","Artifact"]</c> JSON).
///
/// ## {T}: Add {C} (CR 605.1)
///
/// One plain <see cref="ManaAbility"/> producing {C} (colourless) with the
/// standard tap-as-cost overload. {C} folds into <see cref="ManaCost"/>'s
/// Generic bucket, same as every other colourless producer.
///
/// ## {X}{X}, {T}, Sacrifice this land: Create X Treasure tokens (CR 602)
///
/// One <see cref="ActivatedAbility"/> whose cost is
/// <see cref="ManaCostCost"/> <c>{X}{X}</c> + <see cref="AdditionalCost.Tap"/>
/// + <see cref="AdditionalCost.Sacrifice"/>. The engine has no live
/// X-payment ledger, so the caller supplies an
/// <paramref name="treasureXValueProvider"/> sampled at resolution to decide
/// how many Treasures to mint (mirrors Blast Zone's <c>chargeXValueProvider</c>).
/// On resolution the effect:
///   (1) snapshots X (default 0 in the shape-only path — "activate for X=0",
///       a legal but useless activation that still sacrifices the land),
///   (2) sacrifices Treasure Vault to its owner's graveyard (CR 701.16 — the
///       sacrifice payment is a no-op stub at the engine level, so the effect
///       closure performs the zone move so SBAs + visible state line up,
///       same posture as <see cref="BlastZoneFactory"/>'s sweep), then
///   (3) creates X Treasure tokens under the controller via
///       <see cref="TokenFactory.CreateTreasure"/> (CR 111.10 — each a
///       colourless artifact with "{T}, Sacrifice this artifact: Add one mana
///       of any color"), threading the optional <paramref name="zoneService"/>
///       so each token's ETB CardMovedEvent fires for downstream subscribers.
/// Sacrificing first then minting matches the cost-before-effect ordering
/// (CR 602.2a — costs are paid as the ability is activated, before it
/// resolves); the Treasures don't depend on the land still being present.
///
/// No "Activate only as a sorcery" rider — the printed oracle has none, so
/// this is an instant-speed activation.
/// </summary>
[CardName("Treasure Vault")]
public static class TreasureVaultFactory
{
    public const string CardName = "Treasure Vault";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("treasure-vault");

    /// <summary>
    /// Construct Treasure Vault with no live runtime wiring. The
    /// {X}{X}, {T}, Sacrifice ability resolves with X = 0 and the minted
    /// Treasures bypass <see cref="ZoneService"/>. Suitable for shape /
    /// <see cref="NamedCardFactory"/> dispatch tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, treasureXValueProvider: null, zoneService: null);

    /// <summary>
    /// Construct Treasure Vault. When <paramref name="treasureXValueProvider"/>
    /// is supplied, the {X}{X}, {T}, Sacrifice ability mints that many Treasure
    /// tokens at resolution (callers wire this to the activation-time X value);
    /// otherwise X defaults to 0. When <paramref name="zoneService"/> is
    /// supplied each Treasure is placed onto the battlefield via ZoneService so
    /// its ETB CardMovedEvent fires.
    /// </summary>
    public static Land Create(
        Player owner,
        Func<int>? treasureXValueProvider,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}. (CR 605.1 — mana ability, no stack.) The base
        // colourless producer; standard tap-as-cost overload.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {X}{X}, {T}, Sacrifice this land: Create X Treasure tokens.
        // CR 602 — ordinary activated ability; no sorcery-speed rider.
        // Cost = {X}{X} + tap + sacrifice. The engine has no live X ledger,
        // so the caller-supplied provider determines X at resolution
        // (mirrors Blast Zone's chargeXValueProvider). Snapshot X, sacrifice
        // the land (CR 701.16 — the sacrifice cost is a no-op stub at the
        // engine level, so the effect moves the land to the graveyard so
        // visible state matches), then mint X Treasures.
        // ----------------------------------------------------------------
        var mintEffect = new Effect(
            $"{CardName}: create X Treasure tokens ({{X}}{{X}}, {{T}}, Sacrifice)",
            () =>
            {
                var controller = land.Controller ?? owner;
                var x = treasureXValueProvider?.Invoke() ?? 0;

                // Sacrifice payment (CR 701.16) — move Treasure Vault to its
                // owner's graveyard so SBAs + visible state line up. The
                // Treasures don't depend on the land staying on the
                // battlefield (CR 602.2a — the cost is paid as the ability is
                // activated, before it resolves).
                if (land.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(land);
                    owner.Zones.Graveyard.AddCard(land);
                    land.SetZone(ZoneType.Graveyard);
                }

                // CR 111.10 — each Treasure is a colourless artifact token
                // with "{T}, Sacrifice this artifact: Add one mana of any
                // color." TokenFactory.CreateTreasure handles the full spec.
                for (var i = 0; i < x; i++)
                {
                    TokenFactory.CreateTreasure(controller, zoneService);
                }
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{X}{X}"),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { mintEffect }));

        return land;
    }
}
