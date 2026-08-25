# Visual Tracking Hysteresis Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent an established local character track from being lost when its score fluctuates just below the 70% acquisition threshold.

**Architecture:** Keep acquisition and established tracking as separate thresholds in `VisualStationaryObservationSession`. Reject low-texture candidate patches before applying the robust occlusion score, then reuse the existing `SelfIdentityStabilizer` local-anchor, peak-margin, jump, and recovery behavior with a 0.68 tracking threshold for appearance profiles.

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
- Modify: `src/Maple.Host/Stationary/SelfAppearanceTemplateMatcher.cs`
- Modify: `src/Maple.Host/Stationary/VisualStationaryObservationSession.cs`
- Modify: `tests/Maple.Host.Tests/Stationary/SelfAppearanceTemplateMatcherTests.cs`

- [ ] Add a failing matcher test proving a uniform missing-character patch scores below `0.68`.
- [ ] Add a regression proving the appearance search area is independent of the green safe interior.
- [ ] Add regressions proving initial acquisition and established recovery both scan across the yellow platform and require three high-confidence frames.
- [ ] Track the candidate sample luminance range in `Score` and return invalid evidence when the range is below `16`.
- [ ] Change `CharacterTrackingScoreThreshold` from `0.70` to `0.68` while leaving `CharacterAcquisitionScoreThreshold` at `0.70`.
- [ ] Add a yellow-platform recovery pass after local loss and rebase an established track only after three high-confidence stable frames.
- [ ] Add per-pixel sparse-feature coarse matching for yellow-area passes and fully refine both spatial peaks.
- [ ] Re-run the focused stabilizer tests and verify all pass.
- [ ] Run `dotnet test MapleProduct.sln -c Release` and verify the full .NET suite passes.

### Task 3: Package and verify

**Files:**
- No source changes.

- [ ] Build the Windows x64 Release host.
- [ ] Run the frontend tests, lint, and production build.
- [ ] Publish a self-contained Windows x64 package to a new artifact directory and verify the host and broker executables are present.
- [ ] Commit and push `master`, leaving tag `1.0` unchanged.
