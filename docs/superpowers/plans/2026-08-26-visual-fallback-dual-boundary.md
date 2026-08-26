# Visual Fallback Dual-Boundary Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make calibrated visual-loss fallback generate the actual movement commands from continuously learned px/ms rates, while preserving random movement, enforcing both pixel and millisecond boundaries, and recording every calibration and fallback decision.

**Architecture:** `VisualFallbackMovementPlanner` owns rolling directional calibration, projected pixel/ms state, and calibrated fallback segment selection. `VisualStationarySessionController` chooses calibrated visual fallback or conservative uncalibrated continuous fallback and remains the only component that executes movement intent. A diagnostics sink converts calibration and fallback telemetry into the existing JSONL session log without allowing logging failures to interrupt attacks.

**Tech Stack:** .NET 8, C#, xUnit, existing Windows host/session diagnostics, existing `broker + keybd_event` input path.

---

## Task 1: Continuously Calibrate and Preserve Projection State

**Files:**
- Modify: `src/Maple.Host/Stationary/VisualFallbackMovementPlanner.cs`
- Test: `tests/Maple.Host.Tests/Stationary/VisualFallbackMovementPlannerTests.cs`

1. Add failing tests proving that each direction keeps the latest 32 valid samples, the 33rd sample evicts the oldest, and medians are recalculated from the rolling window.
2. Add failing tests proving that accepted and rejected observations return a structured result containing reason, displacement, candidate rate, sample counts, and directional medians.
3. Add failing tests proving that a trusted visual observation reanchors pixel position without resetting the controller-provided accumulated millisecond offset.
4. Change calibration storage from 8 to 32 samples and make `RecordTrustedMovement` return a result such as:

```csharp
public sealed record VisualCalibrationResult(
    bool Accepted,
    string ResultCode,
    MovementDirection Direction,
    int ActualHoldMs,
    int BeforeCenterX,
    int AfterCenterX,
    double DisplacementPx,
    double? CandidatePixelsPerMs,
    int LeftSampleCount,
    int RightSampleCount,
    double? LeftPixelsPerMs,
    double? RightPixelsPerMs);
```

5. Add a read-only projection snapshot for controller telemetry and change trusted observation/recovery APIs to accept the real accumulated millisecond offset.
6. Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~VisualFallbackMovementPlannerTests
```

## Task 2: Add Structured Calibration and Fallback Telemetry

**Files:**
- Modify: `src/Maple.Host/Diagnostics/SessionDiagnostics.cs`
- Modify: `src/Maple.Host/Stationary/VisualStationaryContracts.cs`
- Modify or create: `src/Maple.WindowsHost/Diagnostics/SessionLogVisualFallbackTelemetrySink.cs`
- Test: `tests/Maple.Host.Tests/Diagnostics/SessionDiagnosticsTests.cs`
- Test: `tests/Maple.WindowsHost.Tests/Diagnostics/SessionLogVisualFallbackTelemetrySinkTests.cs`

1. Add failing serialization tests for optional calibration and fallback fields on `SessionLogEntry`.
2. Define a small `IVisualFallbackTelemetrySink` contract and immutable telemetry records. Include session/cycle, planner kind, direction, requested/actual hold, before/after px and ms projections, uncertainty, usable half-width, accepted/rejected reason, sample counts, and directional medians.
3. Implement a null sink in Host and a Windows JSONL sink that maps telemetry onto `SessionLogEntry`.
4. Ensure sink exceptions are contained by the controller so diagnostics cannot stop attack execution.
5. Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~SessionDiagnosticsTests
dotnet test tests/Maple.WindowsHost.Tests/Maple.WindowsHost.Tests.csproj --filter FullyQualifiedName~SessionLogVisualFallbackTelemetrySinkTests
```

## Task 3: Route Actual Fallback Movement Through the Correct Planner

**Files:**
- Modify: `src/Maple.Host/Stationary/VisualStationarySessionController.cs`
- Modify: `src/Maple.WindowsHost/MainWindow.VisualStationary.cs`
- Test: `tests/Maple.Host.Tests/Stationary/VisualStationarySessionControllerTests.cs`

1. Replace the existing test that expects calibrated fallback to use the ordinary random pair with a failing test that proves actual commands come from `VisualFallbackMovementPlanner` and remain legal under both pixel and millisecond boundaries.
2. Add failing tests for:
   - uncalibrated fallback preserving random direction/holds but limiting holds to the lower half of the configured range;
   - no legal calibrated candidate freezing only movement while attack cycles continue;
   - visual recovery reanchoring the projection using current pixel and accumulated ms values;
   - requested vs actual holds and each accepted/rejected calibration sample reaching telemetry;
   - each fallback decision and completed segment reaching telemetry.
3. Split fallback execution into calibrated and uncalibrated paths. The calibrated path calls `BeginCycle`, `TryCreateFirstSegment`, `TryCreateRecoverySegment`, `CreateSecondSegment`, and `ApplyCompletedSegment` on `VisualFallbackMovementPlanner`.
4. Keep the ordinary continuous planner only for uncalibrated fallback, with an internal snapshot whose maximum hold is the midpoint of configured min/max. Preserve all existing random selection and release-settle timing.
5. When no calibrated segment is legal, publish a movement-freeze state and return to the attack loop without ending the session.
6. Wire the Windows telemetry sink into the existing controller construction without changing the broker input route.
7. Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~VisualStationarySessionControllerTests
dotnet test tests/Maple.WindowsHost.Tests/Maple.WindowsHost.Tests.csproj
```

## Task 4: Regression Verification, Package, and Release Handoff

**Files:**
- Verify: `docs/PRODUCT_SPEC.md`
- Verify: `docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md`
- Verify: `docs/PHASE_1_ACCEPTANCE.md`
- Verify: all changed source and test files

1. Run the focused planner/controller/telemetry tests again and inspect failures rather than weakening assertions.
2. Run the full test suite:

```powershell
dotnet test Maple.Product.sln -c Release
```

3. Build/package with the repository's established Windows x64 release command found in project scripts or documentation.
4. Confirm the packaged EXE exists, inspect `git diff --check`, and review the final diff against the approved specification.
5. Commit implementation and tests on `master`, push all pending commits to `origin/master`, and report the absolute latest EXE path.
