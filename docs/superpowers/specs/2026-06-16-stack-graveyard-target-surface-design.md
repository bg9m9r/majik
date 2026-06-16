# Stack + graveyard click-to-target surface — design

Date: 2026-06-16
Repos: majik.core (engine + wire, stack only) and majik.portal (UI). Cross-repo, contract change, api-first deploy.

## Problem

After PRs #140 (cards), #141 (player HUD) and #3005 (central candidate pools), the human
can click battlefield permanents and player HUDs as targets. Two legal target classes
still fall back to the old modal:

- **Spells on the stack** (counterspell "target spell"). The central
  `TargetCandidateService` already gathers stack `ISpell`s into the pool, but they are
  dropped from the wire (`ChooseTargetsAsync` snapshots only `OfType<ICard>()` and
  `OfType<Player>()`), and `CandidateMatchesId` has no `ISpell` arm. The stack IS rendered
  on the board (a collapsible chip) but its items aren't clickable.
- **Cards in a graveyard** ("target card in a graveyard"). Graveyard cards are `ICard`, so
  they ALREADY ride the wire in `candidates` and inbound validation already matches them.
  The only gaps are portal-side: graveyard card ids aren't "board-locatable" (so
  `SelectionService.mode()` returns null → modal fallback), and graveyard cards render only
  inside a read-only zone-modal.

Goal: click a spell on the stack, and a card in a graveyard, to choose it as a target —
reusing the `SelectionService` + affordance flow from #140/#141.

## Verified current-state facts (anchors)

Engine:
- `Spell.Id` (`Majik.Core/Spells/Spell.cs:25`, set via `DeterministicIdScope.NewId()`) is the
  stable stack-object id, distinct from `Card.InstanceId`. `IStackObject.Id`.
- `Target.Spell(spell)` (`Majik.Core/Targeting/Target.cs:67-75`) wraps an `ISpell`.
- `CandidateMatchesId` (`Majik.Core.Api/RemoteAgent.cs:653-658`): handles `ICard`/`Player`,
  NOT `ISpell` (falls to `_ => false`). Needs one arm.
- `ChooseTargetsAsync` (`RemoteAgent.cs:~784-834` post-#3005): snapshots cards + players;
  stack spells stay in `_pendingTargetCandidates` (full pool) but never hit the payload.
- `StateSnapshotter.SnapshotStackObject` (`StateSnapshotter.cs:275-299`) → `StackObjectDto(Id,
  Kind, ControllerId, Description)` where `Id == spell.Id`. The board state already ships the
  stack; the stack item id equals the Guid `CandidateMatchesId` would match.
- `PromptDto`/`PromptPayload` carry `Candidates` + (post-#3005) `PlayerCandidates`.
- Graveyard cards are `ICard` → already snapshotted into `Candidates`; inbound already matches
  `card.InstanceId`. NO engine change for graveyard.
- Drift gate covers `PromptDto`.

Portal:
- Stack rendered in `board.component.ts:~538-606` from `reversedStack()` (~1532-1561); each
  `StackItem { id, kind, description, controllerId?, cardName? }` (`match.types.ts:140-155`).
  No `data-stack-id`, no click, no affordance.
- Graveyard: `zone-rail.component.ts` → `zone-pile.component.ts` (count + top thumbnail);
  full cards only in `zone-modal.component.ts` (read-only grid of `<app-card-view>`),
  opened via board state at `board.component.ts:~1041-1055`.
- `SelectionService.mode()` (`selection.service.ts:56-94`) builds `candidateIds` from
  `p.candidates` (∪ `p.playerCandidates` post-#141); returns null if any candidate id isn't
  in `boardInstanceIds`.
- `boardInstanceIds` (`match.ts:~1039-1046`): battlefield + hand + (post-#141) player ids.
  No graveyard, no stack.
- Affordance: `card-view.component.ts` `data-targetable/dimmed/selected` + co-located CSS.
  `onBoardCardClick`/`onPlayerHudClick`/`isTargetable`/`isDimmed`/`isSelectedForTarget`/
  `maybeAutoSubmit` in `board.component.ts`.
- Decision shape unchanged: ids ride `targetInstanceIds` → `boardDecision` → `translateDecision`.

## Components

### Engine (stack only)

- `StackCandidateDto(System.Guid Id, string CardName, System.Guid ControllerId)` in `Dtos.cs`.
- `PromptDto` gains trailing optional `IReadOnlyList<StackCandidateDto>? StackCandidates = null`.
- `PromptPayload` gains a matching optional field.
- `RemoteAgent.ChooseTargetsAsync`: alongside card + player snapshots, build
  `candidates.OfType<ISpell>().Select(s => new StackCandidateDto(s.Id, s.Card.Name, s.Controller.Id))`
  and set it on the payload (non-null only when non-empty). Use named args. Keep
  `_pendingTargetCandidates` as the full pool.
- `CandidateMatchesId`: add arm `Majik.Core.Spells.ISpell spell => spell.Id == id,` after the
  `Player` arm.
- `BuildPrompt` (`GameFacade.cs:~1490`): copy `StackCandidates` through.
- Regenerate `Majik.Server.Tests/Snapshots/openapi.v1.json`.
- No change to `TargetCandidateService`, `TargetCollection`, `StateSnapshotter`, or the inbound
  command shape. Graveyard: NO engine change.

### Portal — shared plumbing

- `PromptEnvelope` (`match.types.ts`): add `stackCandidates?: { id: string; cardName: string; controllerId: string }[]`.
- `match.ts` prompt subscription: thread `stackCandidates` into the envelope.
- `SelectionService.mode()` for `kind === 'targets'`: `candidateIds = card ids (p.candidates,
  incl. graveyard) ∪ player ids (p.playerCandidates) ∪ stack ids (p.stackCandidates.map(s => s.id))`.
- `boardInstanceIds` (`match.ts`): add (a) every player's `graveyard.cards[].instanceId`,
  (b) every `state.stack[].id`. These make graveyard + stack candidates "locatable" so
  `mode()` activates rather than falling to the modal.

### Portal — stack surface

- Stack-item element gets `[attr.data-stack-id]="item.id"`, `[targetable]`/`[dimmed]`/
  `[selectedForTarget]` styling (reuse the affordance CSS, applied to the stack-item class),
  and `(click)="onStackItemClick(item)"`.
- `onStackItemClick(item)` mirrors the `targets` arm of `onBoardCardClick`: if
  `mode().kind === 'targets'` and `mode().candidateIds.has(item.id)` → `selection.toggle(item.id)`
  + `maybeAutoSubmit`.
- Auto-expand the stack chip when an active targets prompt carries `stackCandidates` (so the
  user can see/click the items without manually expanding). Restore prior expand state when the
  prompt clears.

### Portal — graveyard surface (reuse zone-modal in target mode)

- When an active targets prompt's `candidates` include cards that live in a graveyard
  (instanceId found in some player's `graveyard.cards`), auto-open the graveyard zone-modal in
  **target mode**.
- The zone-modal's `<app-card-view>` cells gain the affordance bindings (`targetable`/`dimmed`/
  `selectedForTarget`) and a `(click)` that calls a board handler `onGraveyardCardClick(card)`
  (same gate/toggle/auto-submit as cards).
- Candidate set may span both players' graveyards. The target-mode modal shows the union of
  graveyard cards that are candidates, grouped by owner (reuse the existing per-zone grid; render
  one section per player who has candidate cards). Non-candidate graveyard cards render dimmed.
- Cancel (the prompt is cancellable for a spell cast) closes the modal and exits target mode;
  Done (range) / auto-submit (single) commits via the existing path.

## Data flow (stack spell)

```
Cast Counterspell ("target spell")
  → TargetCandidateService gathers ctx.Stack ISpells (already)
  → ChooseTargetsAsync: StackCandidates = pool.OfType<ISpell>() → StackCandidateDto[]   [NEW]
  → PromptDto { stackCandidates:[{id, cardName, controllerId}] }
portal:
  envelope.stackCandidates → SelectionService.candidateIds ∪ stack ids
  boardInstanceIds ∪ state.stack[].id  → mode() active
  stack chip auto-expands; legal items glow
  click item → selection.toggle(item.id) → auto-submit
  → boardDecision { kind:'targets', targetInstanceIds:[spellId] } → ChooseTargetsCommand
server:
  CandidateMatchesId: spell.Id == id  [NEW arm] → ISpell chosen
  StackResolver recheck via the category/per-card predicate (unchanged)
```

Graveyard flow is identical minus the engine change: graveyard cards already arrive in
`candidates`; the portal just makes them locatable + clickable in the modal.

## Testing

Engine (xUnit):
- `RemoteAgent`/`BuildPrompt`: a targets prompt whose pool includes a stack `ISpell` ships a
  `PromptDto` with non-null `StackCandidates` (id/cardName/controllerId); the spell stays in
  `_pendingTargetCandidates`; submitting a `ChooseTargetsCommand` with `spell.Id` resolves to
  that spell. Drive a live-play assertion like the player-target wire test.
- `CandidateMatchesId` matches an `ISpell` by `spell.Id` and rejects a wrong Guid.
- Drift snapshot regenerated (contains `stackCandidates` + `StackCandidateDto`).
- Graveyard: a regression assertion that a graveyard-target prompt's candidates still ride
  `Candidates` (no behavior change).

Portal (`ng test --no-watch`, jsdom):
- `SelectionService`: a targets prompt with `stackCandidates` → `candidateIds` includes the
  stack id; with graveyard cards in `candidates` + their ids in the locatable set → mode active.
- `boardInstanceIds`: includes graveyard card ids + stack ids.
- Stack: clicking a targetable stack item toggles + auto-submits `{ kind:'targets',
  targetInstanceIds:[stackId] }`; non-candidate item click ignored; chip auto-expands under a
  stack-candidate prompt. Asserted CSS in component `styles[]`.
- Graveyard: under a graveyard-target prompt the modal auto-opens in target mode; a candidate
  card is `data-targetable` and clicking it toggles + auto-submits; non-candidate dimmed.
- Full suite + `npm run build` green.

## Scope boundary

- Exile-card targets remain modal (out of scope; no exile click surface).
- Ability targets on the stack ("target activated/triggered ability", Stifle) out of scope —
  not implemented in the engine; only `ISpell` stack candidates are shipped.
- Multi-target prompts spanning mixed surfaces work: affordance shows wherever each candidate
  lives; one Done / auto-submit commits the combined `targetInstanceIds`.

## Rollout

1. Engine PR to `bg9m9r/majik`: `StackCandidateDto` + `PromptDto`/`PromptPayload` +
   `ChooseTargetsAsync` + `CandidateMatchesId` arm + `BuildPrompt` + drift snapshot + tests.
   Auto-merge on green.
2. Deploy majik-api (manual; interrupts live matches).
3. Portal PR to `bg9m9r/majik.portal`: envelope + SelectionService + boardInstanceIds + stack
   click/affordance/auto-expand + graveyard target-mode modal + openapi regen + tests.
   Auto-merge; portal auto-deploys.

Only contract change is the additive `StackCandidates` on `PromptDto`. No new GameCommand;
inbound shape unchanged (one new `CandidateMatchesId` arm).
