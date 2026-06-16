# Targeting overhaul + player-as-target — design

Date: 2026-06-16
Repos: majik.core (engine + wire) and majik.portal (UI). Cross-repo, contract change, api-first deploy.

## Problem

Players cannot be chosen as targets in the UI (e.g. Lightning Bolt to the face).
Two root causes:

1. **No legal-target pool for most prompts.** "Any target"-style requests ship
   `new TargetRequest("any target", 1, 1, Array.Empty<object>())` — an EMPTY
   candidate pool, no `CandidateGatherer`. ~64% of the 611 TargetRequests across
   the factories supply no gatherer. With no pool, the portal cannot render legal
   targets; the modal fallback only lists battlefield permanents, and the board
   click-to-select (`SelectionService.mode()`) returns null on an empty pool.
2. **Players are dropped from the wire.** Even when a Player is in the candidate
   pool, `RemoteAgent.ChooseTargetsAsync` snapshots only `candidates.OfType<ICard>()`
   into `PromptPayload.Candidates`, silently discarding Player candidates. `PromptDto`
   has no player-candidate field.

Goal: the human can click a player's HUD (and any legal permanent) to choose it as a
target, with legal objects highlighted and illegal ones dimmed — reusing the
on-board click-to-select shipped in portal PR #140.

## Approach

A central `TargetCandidateService` maps a TargetRequest's free-text `Description` to a
`TargetCategory`, then enumerates the complete legal pool (creatures, players,
planeswalkers, permanents, stack spells, graveyard cards) for that category. It is
wired into the one seam where candidates are resolved (`TargetCollection.CollectAsync`)
as the fallback used only when a card supplies no gatherer (or an empty pool). Existing
bespoke gatherers (color / controller / power-toughness / mana-value filters) are
untouched. A category-derived `TargetLegalityPredicate` fallback keeps the CR 608.2b
resolution recheck honest for cards that don't set their own. Finally, players are
plumbed onto the wire (`PlayerCandidateDto` on `PromptDto`) and the portal player HUD
becomes a clickable target that plugs into the existing `SelectionService` flow.

Rejected alternatives: retrofit a gatherer onto every card (hundreds of edits, no
central truth); portal-only "click anything when pool empty" heuristic (server recheck
rejects, because `_pendingTargetCandidates` is empty).

## Verified current-state facts (anchors)

- `TargetRequest` (`Majik.Core/Players/Agents/TargetRequest.cs:51`): record with
  `Description, MinTargets, MaxTargets, LegalCandidates, Intent, CandidateGatherer,
  PrintedMinTargets, ModeIndex`. No legality predicate, no category. `ResolveCandidates(ctx)`
  (lines 85-98) unions static + gathered, dedup by reference.
- `TargetCollection.CollectAsync` (`Majik.Core/Targeting/TargetCollection.cs:49-89`):
  calls `req.ResolveCandidates(ctx)` (~line 68) then prompts the agent.
- Resolution recheck: `StackResolver` (`Majik.Core/Services/StackResolver.cs:85-101`)
  uses `Spell.TargetLegalityPredicate` (set per card; many cards leave it null).
- `RemoteAgent.ChooseTargetsAsync` (`Majik.Core.Api/RemoteAgent.cs:784-824`): drops
  non-cards via `.OfType<ICard>()` (lines 805-808). `_pendingTargetCandidates`
  retains the full pool for validation.
- Inbound: `CandidateMatchesId` (`RemoteAgent.cs:653-658`) ALREADY matches a Guid to
  `Player.Id`. No inbound change required.
- `PromptDto` (`Majik.Core.Api/Dtos/Dtos.cs:136-186`): `Candidates` is
  `CardSnapshotDto[]?` (card-only). `PlayerDto(Id,Name,Life,…)` exists at lines 23-33.
- `BuildPrompt` (`Majik.Core.Api/GameFacade.cs:1490-1504`) copies payload → DTO.
- `PromptPayload` record (`RemoteAgent.cs:1228`).
- OpenAPI drift gate covers `PromptDto` (`Majik.Server.Tests/OpenApiContractDriftTests.cs`).
- Bot reads the merged pool from the resolved request (`Majik.Bot/Heuristic/TargetPolicy.cs`),
  so a fuller pool only improves bot targeting; its empty-pool text-heuristic becomes a
  rarely-hit fallback. No bot change required, but bot tests must stay green.
- Portal: `SelectionService` (`src/app/core/match/selection.service.ts`),
  `player-hud.component.ts`, `board.component.ts` (`onBoardCardClick`, `isTargetable`,
  `isDimmed`, `isSelectedForTarget`, `boardInstanceIds` in `match.ts:~1039`),
  affordance CSS in `card-view.component.ts`. Player `id` and card `instanceId` are
  both engine Guids in one id space — no collision.

## Components

### Engine

**`TargetCandidateService`** (`Majik.Core/Targeting/TargetCandidateService.cs`) — one
responsibility: given a target description + context + caster, return the legal
candidate pool. Public surface:

```csharp
public static class TargetCandidateService
{
    // Returns the legal pool for the description's category, or empty when the
    // description maps to TargetCategory.None (card gatherer wins).
    public static IReadOnlyList<object> GatherCandidates(string description, GameContext ctx, Player caster);

    // Category-derived legality predicate for the CR 608.2b recheck fallback.
    // Null when the category is None (caller keeps its own predicate / no recheck).
    public static Func<object, bool>? BuildLegalityPredicate(string description);

    internal static TargetCategory Classify(string description); // tested directly
}

internal enum TargetCategory
{
    None,
    AnyTarget,            // creature | player | planeswalker | battle
    Creature,
    Player,
    Opponent,
    CreatureOrPlayer,
    CreatureOrPlaneswalker,
    PlayerOrPlaneswalker,
    Planeswalker,
    Permanent,
    NonlandPermanent,
    Artifact,
    Enchantment,
    Land,
    Spell,               // stack
    NoncreatureSpell,
    CreatureSpell,
    GraveyardCard,
}
```

- `Classify` is substring/normalization-based on the lowercased description, ordered
  most-specific-first (so "target creature or player" beats "target creature").
- Per-category enumerators read `ctx` (battlefields of all players for permanents;
  `ctx.AllPlayers` for players; `ctx.Stack` for spells; graveyards for graveyard cards).
  Caster used only where the predicate is relative ("opponent").
- Keyword untargetability (hexproof/shroud/protection) is applied by filtering the
  enumerated pool through the existing `TargetLegality` check so the UI never offers an
  untargetable object. (Reuse `Majik.Core/Targeting/TargetLegality.cs`.)

**`TargetCollection.CollectAsync`** — after `var live = req.ResolveCandidates(ctx);`
add: `if (live.Count == 0) { live = TargetCandidateService.GatherCandidates(req.Description, ctx, caster); }`
then prompt with the (possibly newly-populated) pool via the existing `WithCandidates`
path. No behavior change when a card already supplies candidates.

**Recheck predicate** — at cast time, in `TargetCollection.CollectAsync`, when the spell
has no `TargetLegalityPredicate` and the description's category is not `None`, stamp
`spell.TargetLegalityPredicate = TargetCandidateService.BuildLegalityPredicate(description)`.
The existing `StackResolver` recheck (CR 608.2b) then corrects an over-permissive UI pick
at resolution with no change to `StackResolver` itself. Cards that set their own predicate
are untouched.

**Fidelity stance:** for exotic predicates ("creature with power 1 or less") the category
pool is coarse (all creatures); the server recheck enforces the precise rule. UI may
highlight a hair more than strictly legal; resolution is never illegal.

### Wire

- New `PlayerCandidateDto(Guid Id, string Name, int Life)` in `Dtos.cs`.
- `PromptDto` gains trailing optional `IReadOnlyList<PlayerCandidateDto>? PlayerCandidates = null`.
- `PromptPayload` gains matching optional field.
- `RemoteAgent.ChooseTargetsAsync`: alongside the card snapshots, build
  `candidates.OfType<Player>().Select(p => new PlayerCandidateDto(p.Id, p.Name, p.LifeTotal))`
  and set it on the payload (non-null only when non-empty). Keep `_pendingTargetCandidates`
  as the full pool (already correct).
- `BuildPrompt`: copy `PlayerCandidates` through.
- Inbound `ChooseTargetsCommand`: NO change (`CandidateMatchesId` handles `Player.Id`).
- Regenerate `Majik.Server.Tests/Snapshots/openapi.v1.json` (drift gate).

### Portal

- `player-hud.component.ts`: add `targetable`, `dimmed`, `selectedForTarget` boolean
  inputs; bind `[attr.data-targetable]`/`[attr.data-dimmed]`/`[attr.data-selected]` +
  `[attr.data-player-id]="player()?.id"` on the `.player-hud` root; co-locate the same
  affordance CSS used by `card-view` (jsdom can't load global SCSS — keep asserted CSS
  in component `styles[]`).
- `PromptEnvelope` (`match.types.ts`): add `playerCandidates?: { id: string; name: string; life: number }[]`.
- `match.ts` prompt subscription: thread `playerCandidates` into the envelope.
- `SelectionService`: when building a `targets` mode, merge `playerCandidates` ids into
  `candidateIds`. Players are inherently "board-locatable" (HUD always on screen), so the
  mixed-zone guard must treat player ids as locatable — extend `boardInstanceIds`
  (`match.ts`) to also include each `player.id`, and `setBoardInstanceIds` receives them.
- `board.component.ts`: add `onPlayerHudClick(player)` mirroring `onBoardCardClick` for
  the `targets` kind (gate on `mode.candidateIds.has(player.id)`, `selection.toggle`,
  `maybeAutoSubmit`). Bind HUD `targetable/dimmed/selectedForTarget` from
  `isTargetable(player.id)` / `isDimmed(player.id)` / `isSelectedForTarget(player.id)`
  and wire `(click)="onPlayerHudClick(p)"` where the HUDs render.
- No new decision shape: the player id rides in `targetInstanceIds` via the existing
  `submitSelection` → `boardDecision` → `translateDecision` path.

## Data flow

```
Cast/activate targeting prompt
  → TargetCollection.CollectAsync
      live = req.ResolveCandidates(ctx)
      if empty: live = TargetCandidateService.GatherCandidates(desc, ctx, caster)  [incl. players]
  → RemoteAgent.ChooseTargetsAsync
      Candidates       = live.OfType<ICard>()  → CardSnapshotDto[]
      PlayerCandidates = live.OfType<Player>() → PlayerCandidateDto[]   [NEW]
  → PromptDto over SignalR
portal:
  prompt$ → PromptEnvelope { candidates, playerCandidates }
  SelectionService.mode(): candidateIds = card ids ∪ player ids
  board: HUD + card-views marked targetable/dimmed/selected
  click HUD/card → selection.toggle(id) → auto-submit/Done
  → boardDecision { kind:'targets', targetInstanceIds:[…ids incl player id…] }
  → translateDecision → ChooseTargetsCommand
server:
  RemoteAgent.Submit → CandidateMatchesId(id) matches Player.Id  → target chosen
  StackResolver recheck via per-card or category-derived predicate
```

## Scope boundary

- Board-CLICK affordance: players + battlefield permanents (incl. planeswalkers).
- Stack-spell targets (counterspells) and graveyard-card targets: the central service
  gives them accurate pools so the existing MODAL renders them correctly, but they are
  NOT board-clickable in this effort (no stack/graveyard click surface yet).
- Multi-target spells: unchanged from PR #140 (targets default to min=max=1 in the
  portal selection bounds; multi-target still routes through the modal where the engine
  ships no count). Players participate wherever the prompt is board-resident.

## Testing

Engine (xUnit):
- `TargetCandidateService.Classify`: table of descriptions → expected category
  (any target, target creature, target player, target creature or player, target spell,
  target noncreature spell, target nonland permanent, target card in a graveyard, …).
- `GatherCandidates`: a built game state with creatures + planeswalkers + 2 players +
  a spell on the stack → "any target" returns both players + creatures + planeswalkers
  (+ battle if present); "target creature" returns only creatures; "target spell" returns
  the stack spell; hexproof/shroud objects excluded.
- `TargetCollection` integration: a card with an empty TargetRequest pool ("any target")
  now prompts with a non-empty pool that includes the opponent player.
- `RemoteAgent`/`BuildPrompt`: a targets prompt whose pool includes a player yields a
  `PromptDto` with non-null `PlayerCandidates` (id/name/life) AND the player still in
  `_pendingTargetCandidates`; submitting a `ChooseTargetsCommand` with the player's id
  resolves to the player (drives a live-play assertion à la
  `SacrificeAnotherCreatureLivePlayTests`).
- StackResolver recheck: a spell with no per-card predicate but category "target creature"
  whose only target became a non-creature is countered by SBA.
- Bot suites stay green (fuller pools must not break `TargetPolicy`).
- OpenAPI drift snapshot regenerated.

Portal (`ng test --no-watch`, jsdom):
- `SelectionService`: a targets prompt with `playerCandidates` → `candidateIds` includes
  the player id; mode is non-null when all card+player candidates are board-locatable.
- `player-hud.component`: `targetable`/`dimmed`/`selectedForTarget` inputs set
  `data-*` attributes; asserted CSS lives in component `styles[]`.
- `board.component`: clicking a targetable player HUD toggles selection and auto-submits a
  single fixed target as `{ kind:'targets', targetInstanceIds:[playerId] }`; clicking a
  non-candidate HUD is ignored.
- Full suite + `npm run build` green.

## Rollout

1. Engine PR to `bg9m9r/majik`: service + CollectAsync wiring + recheck fallback + wire
   (`PlayerCandidateDto`/`PromptDto`/`PromptPayload`/`ChooseTargetsAsync`/`BuildPrompt`) +
   regenerated drift snapshot + tests. Auto-merge on green.
2. Deploy majik-api (manual — auto-deploy off; interrupts live matches).
3. Portal PR to `bg9m9r/majik.portal`: HUD clickable + SelectionService/board wiring +
   `openapi.json` regen + tests. Auto-merge on green.
4. Portal auto-deploys on merge (build fetches the now-updated live api).

No new GameCommand. No change to inbound target validation. The only contract change is
the additive `PlayerCandidates` field on `PromptDto`.
