# Visual Anchor And Recenter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent idle visual identity drift, actively return trusted characters toward the platform center, and ensure visual loss cannot indefinitely stop random attacks.

**Architecture:** Give the observation source an explicit movement-tracking window controlled by the visual session controller. Keep paired random movement only in the center band; use one random inward segment outside that band. After 15 seconds of visual unavailability, run the ordinary time-bounded movement planner inside the visual session regardless of calibration, then switch back after three trusted frames.

**Tech Stack:** C# 12, .NET 8, xUnit

---

### Task 1: Lock the appearance anchor outside movement feedback

**Files:**
- Modify: `src/Maple.Host/Stationary/VisualStationaryContracts.cs`
- Modify: `src/Maple.Host/Stationary/VisualStationaryObservationSession.cs`
- Test: `tests/Maple.Host.Tests/Stationary/VisualStationaryObservationSessionTests.cs`

- [x] Add `BeginMovementTracking(MovementDirection direction)` and `EndMovementTracking()` to the observation source.
- [x] Add a failing frame-sequence test proving candidates cannot cumulatively move the appearance search anchor while the movement window is closed.
- [x] Keep `appearanceAnchorX/Y` fixed unless movement tracking is active; close the window on every controller exit path.
- [x] Add a passing test proving an open movement window can advance the anchor and that closing it freezes the new position.

### Task 2: Use a single random inward correction outside the center band

**Files:**
- Modify: `src/Maple.Host/Stationary/VisualStationaryMovementPlanner.cs`
- Modify: `src/Maple.Host/Stationary/VisualStationarySessionController.cs`
- Test: `tests/Maple.Host.Tests/Stationary/VisualStationaryMovementPlannerTests.cs`
- Test: `tests/Maple.Host.Tests/Stationary/VisualStationarySessionControllerTests.cs`

- [x] Add failing tests for negative and positive offsets whose absolute value exceeds `GuardWidthPx` but remains inside the platform.
- [x] Return the inward direction for those states and execute exactly one randomly sampled correction segment.
- [x] Assert the next attack begins without an outward cancellation segment or `FacingRestorePending`.

### Task 3: Use ordinary random movement during visual loss

**Files:**
- Modify: `src/Maple.Host/Stationary/VisualStationarySessionController.cs`
- Test: `tests/Maple.Host.Tests/Stationary/VisualStationarySessionControllerTests.cs`

- [x] Change acquisition and tracking threshold tests from `0.72` to `0.70`.
- [x] Add a failing test where visual loss reaches 15 seconds without calibration and verify ordinary random movement executes.
- [x] Add a failing test where `FacingRestorePending` reaches 15 seconds and verify the next attack and ordinary movement begin.
- [x] Run `StationaryMovementPlanner` inside the visual session during fallback and switch back only at a full-cycle boundary after three trusted frames.
- [x] Add failing paired-gap tests for visual and ordinary fallback cycles using sampled gaps `47ms` and `63ms`.
- [x] Publish and wait for `100ms + sampledGapMs` without replacing the configured random sample.

### Task 4: Verify and package

**Files:**
- Output: `artifacts/phase-1/win-x64-visual-anchor-recenter/`

- [x] Run focused red-green tests, then `dotnet test MapleProduct.sln --no-restore`.
- [x] Run frontend tests, lint, and build.
- [x] Publish Windows x64 Release artifacts, verify ZIP entries and hashes, and report the absolute EXE path.
