# Visual Tracking Hysteresis Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent an established local character track from being lost when its score fluctuates just below the 70% acquisition threshold.

**Architecture:** Keep acquisition and established tracking as separate thresholds in `VisualStationaryObservationSession`. Reuse the existing `SelfIdentityStabilizer` local-anchor, peak-margin, jump, and recovery behavior; only supply a 0.68 tracking threshold for appearance profiles.

**Tech Stack:** .NET 8, C#, xUnit

---

### Task 1: Add the regression

**Files:**
- Modify: `tests/Maple.Host.Tests/Stationary/SelfIdentityStabilizerTests.cs`

- [ ] Add a test that establishes an appearance track at `0.92`, then submits a unique local `0.692` candidate and expects `Trusted`.
- [ ] Update the existing threshold-boundary test to expect `0.68` to remain trusted and `0.67` to revoke trust.
- [ ] Run `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj -c Release --filter "FullyQualifiedName~SelfIdentityStabilizerTests"` and verify the new `0.692` assertion fails under the current `0.70` tracking threshold.

### Task 2: Implement the threshold split

**Files:**
- Modify: `src/Maple.Host/Stationary/VisualStationaryObservationSession.cs`

- [ ] Change `CharacterTrackingScoreThreshold` from `0.70` to `0.68` while leaving `CharacterAcquisitionScoreThreshold` at `0.70`.
- [ ] Re-run the focused stabilizer tests and verify all pass.
- [ ] Run `dotnet test MapleProduct.sln -c Release` and verify the full .NET suite passes.

### Task 3: Package and verify

**Files:**
- No source changes.

- [ ] Build the Windows x64 Release host.
- [ ] Run the frontend tests, lint, and production build.
- [ ] Publish a self-contained Windows x64 package to a new artifact directory and verify the host and broker executables are present.
- [ ] Commit and push `master`, leaving tag `1.0` unchanged.
