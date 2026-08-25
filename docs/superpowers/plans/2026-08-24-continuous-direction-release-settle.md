# Continuous Direction Release Settle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent the initial-facing opposite movement segment from accumulating more in-game displacement while preserving all configured randomness.

**Architecture:** Keep the stationary movement planner and Broker timing unchanged. Add a fixed 100ms settlement component to the ordinary controller's authoritative `MoveGap`, then add the independently sampled configured gap.

**Tech Stack:** C# 12, .NET 8, xUnit

---

### Task 1: Specify the timing invariant

**Files:**
- Modify: `docs/PRODUCT_SPEC.md`
- Modify: `docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md`
- Modify: `docs/PHASE_1_ACCEPTANCE.md`
- Create: `docs/superpowers/specs/2026-08-24-continuous-direction-release-settle-design.md`

- [x] Document that ordinary `MoveGap` equals fixed `100ms` settlement plus an independent configured random gap.
- [x] State that movement order, random holds, final facing, visual mode, and Broker behavior do not change.

### Task 2: Reproduce the missing settlement delay

**Files:**
- Test: `tests/Maple.Host.Tests/Stationary/StationarySessionControllerTests.cs`

- [x] Add `Adds_direction_release_settle_to_the_random_move_gap`.
- [x] Run `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~Adds_direction_release_settle_to_the_random_move_gap`.
- [x] Verify it fails because sampled `47ms` and `63ms` gaps are published without the required fixed `100ms` component.

### Task 3: Add the settlement component

**Files:**
- Modify: `src/Maple.Host/Stationary/StationarySessionController.cs`

- [x] Add `DirectionReleaseSettleMs = 100`.
- [x] Publish and wait for `checked(DirectionReleaseSettleMs + movementPlanner.SampleGapMs(config))` before `MoveSecond`.
- [x] Re-run the focused test and verify it passes.

### Task 4: Verify and package

**Files:**
- Output: `artifacts/phase-1/win-x64-continuous-direction-settle/`

- [x] Run Maple Core, Host, InputBroker, and frontend test suites.
- [x] Build and publish Windows x64 Release artifacts.
- [x] Verify the packaged executable and ZIP contents and report the absolute executable path.
