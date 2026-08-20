# Recognition Target Filtering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with review checkpoints.

**Goal:** Filter unreliable monster and drop candidates using geometry and short-term temporal stability before they reach preview or future action policies.

**Architecture:** Keep ONNX inference unchanged at the adapter boundary. Add a pure `RecognitionTargetFilter` for per-frame geometry checks and a stateful `RecognitionTargetStabilizer` owned by the provider. Monster candidates must have plausible sprite geometry; drop candidates must be small, grounded, non-overlapping, and present in consecutive frames. No target is sent to attack logic by this change.

**Tech Stack:** .NET 8, Maple.Host recognition contracts, xUnit.

---

### Task 1: Add deterministic candidate filtering

**Files:**
- Create: `src/Maple.Host/Recognition/RecognitionTargetFilter.cs`
- Create: `tests/Maple.Host.Tests/Recognition/RecognitionTargetFilterTests.cs`

- [x] Write tests for rejecting nameplate-shaped monster boxes, rejecting oversized drops, accepting plausible candidates, and excluding candidates that overlap the self box.
- [x] Run the focused tests and verify they fail because the filter does not exist.
- [x] Implement pure geometry checks with normalized coordinate-independent thresholds.
- [x] Run the focused tests and verify they pass.

### Task 2: Add short-term target stabilization

**Files:**
- Modify: `src/Maple.Host/Recognition/RecognitionTargetFilter.cs`
- Modify: `tests/Maple.Host.Tests/Recognition/RecognitionTargetFilterTests.cs`

- [x] Test that a one-frame drop is not published, two nearby consecutive observations are published, and a moved/expired candidate is removed.
- [x] Implement nearest-center matching with a bounded frame age and no unbounded history.
- [x] Run the focused tests and verify they pass.

### Task 3: Integrate the filter into the ONNX adapter

**Files:**
- Modify: `src/Maple.WindowsHost/Preview/OnnxRecognitionProvider.cs`
- Create: `tests/Maple.Host.Tests/Recognition/OnnxRecognitionProviderFilterTests.cs` only if adapter behavior can be tested without Windows capture.

- [x] Apply monster and drop geometry filters after model decoding and before overlap suppression.
- [x] Pass the current self candidate into the filter so nameplate and self-overlap false positives are rejected.
- [x] Stabilize drops across frames and keep filtered drops diagnostic-only.
- [x] Run all Host recognition tests and verify no regression.

### Task 4: Verify and publish

**Files:**
- Modify: `docs/PHASE_2_AUTO_NAVIGATION_SCOPE.md` only if acceptance wording needs the diagnostic-only guarantee.

- [x] Run `dotnet test MapleProduct.sln --no-restore`.
- [x] Run `git diff --check`.
- [x] Publish a Windows x64 executable and verify the preview still shows FPS/HUD while filtered target counts remain safe.
- [ ] Commit the implementation and publish the resulting local executable path.
