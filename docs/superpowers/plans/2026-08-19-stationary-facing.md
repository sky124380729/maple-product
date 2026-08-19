# Stationary Facing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prompt for the character's current facing direction on every stationary-session start and end every movement pair facing that same direction, while isolating direction acquisition behind a future-compatible provider.

**Architecture:** React owns only the manual prompt and sends a one-time `initialFacing` start intent. `StationarySessionApplicationService` resolves that intent through `IInitialFacingProvider` after locating the target, returns the resolved direction in `SessionStartResult`, and the controller locks it for the session. `StationaryMovementPlanner` uses the opposite direction first and the resolved direction second while preserving independent durations and offset limits.

**Tech Stack:** React 19, Ant Design 6, TypeScript, .NET 8, WPF/WebView2, xUnit, Vitest.

---

### Task 1: Manual Facing Prompt

**Files:**
- Modify: `client/src/bridge/bridge.ts`
- Modify: `client/src/pages/StationaryAttackPage.tsx`
- Test: `client/src/pages/StationaryAttackPage.test.tsx`

- [ ] Add failing tests proving Start opens the prompt, cancel sends no command, and left/right send `startStationary` with the selected `initialFacing`.
- [ ] Run `npm test -- --run src/pages/StationaryAttackPage.test.tsx` and verify the new assertions fail because no prompt exists.
- [ ] Add the Ant Design modal and extend only the start bridge command with `initialFacing: 'left' | 'right'`.
- [ ] Re-run the focused test and verify it passes.

### Task 2: Replaceable Facing Provider

**Files:**
- Create: `src/Maple.Host/Windows/InitialFacingProvider.cs`
- Modify: `src/Maple.Host/Windows/WindowContracts.cs`
- Modify: `src/Maple.Host/Windows/StationarySessionApplicationService.cs`
- Test: `tests/Maple.Host.Tests/Windows/InitialFacingProviderTests.cs`
- Test: `tests/Maple.Host.Tests/Windows/StationarySessionApplicationServiceTests.cs`

- [ ] Add failing tests for manual left/right resolution, invalid input, and application-service propagation without Broker startup on invalid input.
- [ ] Run the focused Host tests and verify they fail because the provider and resolved result do not exist.
- [ ] Add `IInitialFacingProvider`, `FacingResolution`, and `ManualInitialFacingProvider`; resolve after selecting the unique target and before foreground/Broker operations.
- [ ] Re-run focused Host tests and verify they pass.

### Task 3: Facing-Preserving Movement

**Files:**
- Modify: `src/Maple.Core/Movement/StationaryMovementPlanner.cs`
- Modify: `src/Maple.Host/Stationary/StationarySessionController.cs`
- Modify: `src/Maple.WindowsHost/MainWindow.xaml.cs`
- Test: `tests/Maple.Core.Tests/Movement/StationaryMovementPlannerTests.cs`
- Test: `tests/Maple.Host.Tests/Stationary/StationarySessionControllerTests.cs`

- [ ] Add failing Core tests for `Right -> Left` when facing left, `Left -> Right` when facing right, and no order swap when the required first direction lacks budget.
- [ ] Add a failing controller test proving the direction passed at session start is used for the complete action sequence.
- [ ] Run focused Core and Host tests and verify expected failures.
- [ ] Lock facing in `StartSession`, generate the opposite direction first and facing direction second, and thread the resolved direction from MainWindow through the controller.
- [ ] Re-run focused tests and verify they pass.

### Task 4: Verification And Windows Publish

**Files:**
- Modify: `docs/phase-1/evidence/windows-real-input.md`

- [ ] Run `dotnet test MapleProduct.sln --no-restore` and verify all .NET tests pass.
- [ ] Run `npm test -- --run`, `npm run build`, and `npm run lint` in `client`; verify all checks pass except the existing non-failing bundle-size warning.
- [ ] Run `git diff --check` and inspect the final diff for unrelated changes.
- [ ] Stop only the running Maple Windows Host, run `scripts/publish-windows.ps1`, and start the newly published Host.
- [ ] Record the executable path and distinguish automated evidence from the remaining visual in-game observation.
