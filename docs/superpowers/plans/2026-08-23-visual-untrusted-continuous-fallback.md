# Visual Untrusted Position-Prediction Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Learn movement pixels per actual Broker millisecond while vision is trusted, use a dual-bounded position estimate after 15 seconds of identity loss, and reuse saved visual configuration without forcing redraw on a transient low score.

**Architecture:** `VisualStationaryObservationSession` owns continuous-untrusted timing. A pure `VisualFallbackMovementPlanner` owns robust direction-specific calibration, last-trusted pixel anchoring, uncertainty growth, and candidate filtering against both platform pixels and `maxLateralMoveMs`. `VisualStationarySessionController` remains the only input controller and switches policy only at full-cycle boundaries. Saved-profile startup accepts a temporarily untrusted identity and starts frozen instead of discarding the profile.

**Tech Stack:** .NET 8, xUnit, React 19, TypeScript, Vitest.

---

### Task 1: Continuous-untrusted timing

**Files:**
- Modify: `src/Maple.Host/Stationary/VisualStationaryContracts.cs`
- Modify: `src/Maple.Host/Stationary/VisualStationaryObservationSession.cs`
- Test: `tests/Maple.Host.Tests/Stationary/VisualStationaryObservationSessionTests.cs`

- [ ] Add a failing test proving 14,999ms is not eligible, 15,000ms is eligible, and one trusted observation resets the timer.
- [ ] Run the focused test and confirm it fails because `IsContinuouslyUntrustedFor` does not exist.
- [ ] Add the observation-source method and a testable monotonic clock; update the timer atomically when observations publish.
- [ ] Run the focused tests and confirm they pass.

### Task 2: Calibrated dual-boundary fallback planner

**Files:**
- Add: `src/Maple.Host/Stationary/VisualFallbackMovementPlanner.cs`
- Add: `tests/Maple.Host.Tests/Stationary/VisualFallbackMovementPlannerTests.cs`

- [ ] Add failing tests for direction-specific median calibration, invalid-sample rejection, pixel/time boundary filtering, uncertainty growth, inward recovery near an edge, and freeze when no candidate can be proven safe.
- [ ] Implement the pure planner with two samples per direction, eight-sample rolling medians, `20ms` release margin and position uncertainty.
- [ ] Run the focused planner tests to green, including repeated randomized cycles that never exceed either modeled boundary.

### Task 3: Single-controller strategy switching

**Files:**
- Modify: `src/Maple.Host/Stationary/VisualStationarySessionController.cs`
- Modify: `src/Maple.WindowsHost/MainWindow.VisualStationary.cs`
- Test: `tests/Maple.Host.Tests/Stationary/VisualStationarySessionControllerTests.cs`

- [ ] Add failing controller tests proving pre-threshold loss freezes, uncalibrated loss remains frozen, calibrated threshold loss uses the prediction planner, outside never falls back, and trusted recovery selects visual movement on the next full cycle.
- [ ] Run the focused tests and confirm the missing fallback behavior causes the failures.
- [ ] Feed trusted before/after X and Broker actual hold milliseconds into calibration; initialize fallback from the last trusted offset and execute the planner's pair/recovery paths with identical timing validation.
- [ ] Publish `VISUAL_FALLBACK_CONTINUOUS` on entry and `VISUAL_FALLBACK_RECOVERED` on exit; run focused tests to green.

### Task 4: Saved-profile restart without redraw

**Files:**
- Modify: `src/Maple.WindowsHost/MainWindow.VisualStationary.cs`
- Modify: `client/src/bridge/configValidation.ts`
- Test: `tests/Maple.Host.Tests/Stationary/VisualStationaryStartupPolicyTests.cs`
- Test: `client/src/bridge/configValidation.test.ts`

- [ ] Add failing tests proving a valid saved profile with a temporary `VISUAL_NAME_SCORE_LOW` starts frozen, while profile/schema/viewport failures still reject startup.
- [ ] Replace the trusted-only startup gate with an explicit startup policy and remove the second trusted-only post-Broker gate.
- [ ] Change transient identity-loss wording so redraw is not presented as mandatory.

### Task 5: Operator-visible fallback state

**Files:**
- Modify: `src/Maple.WindowsHost/MainWindow.VisualStationary.cs`
- Modify: `client/src/components/VisualSafetyStatus.tsx`
- Test: `client/src/pages/StationaryAttackPage.test.tsx`

- [ ] Add a failing UI test for the `FallbackContinuous` state label.
- [ ] Preserve fallback status while observation events continue, then clear it on recovered controller state.
- [ ] Render a warning tag labeled `持续攻击回退` and run the focused UI test to green.

### Task 6: Verification and packaging

**Files:**
- Modify: `docs/phase-1/evidence/windows-real-input.md`

- [ ] Run `dotnet test MapleProduct.sln -c Release --no-restore` and require zero failures.
- [ ] Run `npm test -- --run`, `npm run lint`, and `npm run build` in `client`; require tests/build success and no new lint errors.
- [ ] Publish with `scripts/publish-windows.ps1 -OutputRoot artifacts/phase-1/win-x64-visual-position-fallback`.
- [ ] Read every published file and ZIP entry, verify the packaged Host DLL matches the tested Release DLL, record hashes, and provide the absolute EXE path.
