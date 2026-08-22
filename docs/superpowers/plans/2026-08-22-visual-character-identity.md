# Visual Character Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make new visual stationary profiles identify the manually selected character appearance with a fixed multi-template local tracker, while keeping legacy name profiles readable and preserving all movement safety invariants.

**Architecture:** Extend the visual profile with an explicit identity kind and optional immutable character template bank. Keep legacy name matching unchanged, add a separate allocation-conscious appearance matcher/calibrator in `Maple.Host`, and route `VisualStationaryObservationSession` by profile kind. WPF owns only the two-step selection and timed frame collection; React and the Broker remain unchanged.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit, Windows Graphics Capture frames, existing broker + `keybd_event`, existing PowerShell publish pipeline.

---

### Task 1: Update Authoritative Requirements

**Files:**
- Modify: `docs/PRODUCT_SPEC.md`
- Modify: `docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md`
- Modify: `docs/PHASE_1_ACCEPTANCE.md`

- [ ] **Step 1: Specify character appearance identity**

Add the confirmed yellow/green/blue rectangle semantics, schema 2 character profile, fixed one-to-eight template bank, 1.5-second calibration, mirrored matching, local-only search, and fail-closed behavior. Explicitly state that no 15-second continuous-mode fallback exists.

- [ ] **Step 2: Add acceptance cases**

Require schema 1 compatibility, character selection dimension validation, mirrored/multi-template matching, distant-player non-promotion, 20% synthetic occlusion tolerance, three-frame reacquisition, and unchanged random/controller behavior.

- [ ] **Step 3: Verify document formatting**

Run:

```powershell
git diff --check -- docs/PRODUCT_SPEC.md docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md docs/PHASE_1_ACCEPTANCE.md
```

Expected: exit 0; line-ending notices are allowed.

### Task 2: Add Schema 2 Character Profiles With TDD

**Files:**
- Modify: `src/Maple.Host/Stationary/VisualStationaryContracts.cs`
- Modify: `tests/Maple.Host.Tests/Stationary/VisualStationaryProfileStoreTests.cs`
- Modify: `tests/Maple.Host.Tests/Stationary/VisualStationarySetupTests.cs`

- [ ] **Step 1: Write failing profile tests**

Add tests that deserialize a version 1 name profile without new JSON properties, round-trip a version 2 character profile with two templates, accept a `48x72` character source at 1366 width, and reject character patches below `24x32`, above `112x144`, with the wrong byte length, or with no texture.

Use the intended API:

```csharp
public enum VisualIdentityKind { NameTemplate, CharacterAppearance }

public sealed record VisualCharacterTemplateBank(
    FrameRect Source,
    int TemplateWidth,
    int TemplateHeight,
    byte[][] TemplatesBgra,
    int MatcherVersion);

VisualStationaryProfile profile = LegacyProfile() with
{
    SchemaVersion = VisualStationaryProfile.SchemaVersionCurrent,
    IdentityKind = VisualIdentityKind.CharacterAppearance,
    CharacterAppearance = new VisualCharacterTemplateBank(source, 48, 72, templates, 1)
};
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~VisualStationaryProfileStoreTests|FullyQualifiedName~VisualStationarySetupTests"
```

Expected: compile failure because the identity kind and character bank do not exist.

- [ ] **Step 3: Implement schema and branch validation**

Append optional properties to `VisualStationaryProfile` so old JSON remains deserializable:

```csharp
VisualIdentityKind IdentityKind = VisualIdentityKind.NameTemplate,
VisualCharacterTemplateBank? CharacterAppearance = null
```

Set `SchemaVersionCurrent = 2` and accept version 1 only as `NameTemplate`. Keep the existing name validator byte-for-byte for name profiles. Character validation scales `24x32` and `112x144` from a 1366-wide reference, requires one through eight equal-size BGRA arrays, matcher version 1, and textured pixels. Return stable `VISUAL_CHARACTER_*` codes.

- [ ] **Step 4: Run tests and verify GREEN**

Run the Task 2 command. Expected: all selected tests pass.

- [ ] **Step 5: Commit schema work**

```powershell
git add docs/PRODUCT_SPEC.md docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md docs/PHASE_1_ACCEPTANCE.md src/Maple.Host/Stationary/VisualStationaryContracts.cs tests/Maple.Host.Tests/Stationary/VisualStationaryProfileStoreTests.cs tests/Maple.Host.Tests/Stationary/VisualStationarySetupTests.cs
git commit -m "feat: define character appearance visual profiles"
```

### Task 3: Build Fixed Multi-Template Matching And Calibration With TDD

**Files:**
- Create: `src/Maple.Host/Stationary/SelfAppearanceTemplateMatcher.cs`
- Create: `src/Maple.Host/Stationary/CharacterAppearanceCalibrator.cs`
- Create: `tests/Maple.Host.Tests/Stationary/SelfAppearanceTemplateMatcherTests.cs`
- Create: `tests/Maple.Host.Tests/Stationary/CharacterAppearanceCalibratorTests.cs`
- Reuse: `src/Maple.Host/Stationary/SelfNameTemplateMatcher.cs`

- [ ] **Step 1: Write failing matcher tests**

Create synthetic textured `32x40` body patches and assert:

```csharp
SelfNameMatch match = matcher.Match(frame, templates, 32, 40, localSearch);
Assert.InRange(match.BestScore, 0.88, 1.0);
Assert.Equal(expectedCenterX, match.CenterX);
```

Cover a second animation template, a horizontally mirrored patch, 20% occlusion, and a distant exact copy outside `localSearch`. Also assert a distinct local second candidate is reported for ambiguity handling.

- [ ] **Step 2: Run matcher tests and verify RED**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~SelfAppearanceTemplateMatcherTests
```

Expected: compile failure because `SelfAppearanceTemplateMatcher` is absent.

- [ ] **Step 3: Implement the appearance matcher**

Generalize the existing edge/color sample scoring into a new matcher that prebuilds samples for every stored template and its horizontal mirror, scores each candidate with the maximum template score, and excludes candidates within one third of template width when finding the second peak. Do not modify `SelfNameTemplateMatcher` behavior.

The public signature is:

```csharp
public SelfNameMatch Match(
    CapturedFrame frame,
    IReadOnlyList<byte[]> templates,
    int templateWidth,
    int templateHeight,
    FrameRect searchArea);
```

- [ ] **Step 4: Run matcher tests and verify GREEN**

Run the Task 3 matcher command. Expected: all matcher tests pass.

- [ ] **Step 5: Write failing calibrator tests**

Assert the calibrator always retains the frozen source, aligns a body moved up to six scaled pixels, discards a sample scoring `>=0.97` against an existing template, accepts a distinct animation, caps the bank at eight, rejects viewport/source mismatch, and never mutates returned arrays.

- [ ] **Step 6: Run calibrator tests and verify RED**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CharacterAppearanceCalibratorTests
```

Expected: compile failure because `CharacterAppearanceCalibrator` is absent.

- [ ] **Step 7: Implement the calibrator**

Use this boundary:

```csharp
public sealed class CharacterAppearanceCalibrator(
    CapturedFrame frozenFrame,
    FrameRect source,
    SelfAppearanceTemplateMatcher? matcher = null)
{
    public int TemplateCount { get; }
    public bool TryAdd(CapturedFrame frame);
    public VisualCharacterTemplateBank Complete();
}
```

`TryAdd` searches within six scaled pixels of the original center, crops the accepted candidate, compares it to the current fixed bank, and adds only a distinct patch. `Complete` returns cloned immutable data and matcher version 1.

- [ ] **Step 8: Run calibrator and matcher tests**

Run both Task 3 filters. Expected: all pass.

- [ ] **Step 9: Commit matching work**

```powershell
git add src/Maple.Host/Stationary/SelfAppearanceTemplateMatcher.cs src/Maple.Host/Stationary/CharacterAppearanceCalibrator.cs tests/Maple.Host.Tests/Stationary/SelfAppearanceTemplateMatcherTests.cs tests/Maple.Host.Tests/Stationary/CharacterAppearanceCalibratorTests.cs
git commit -m "feat: calibrate and match character appearance"
```

### Task 4: Route Observation Through Local Character Tracking With TDD

**Files:**
- Modify: `src/Maple.Host/Stationary/SelfIdentityStabilizer.cs`
- Modify: `src/Maple.Host/Stationary/VisualStationaryObservationSession.cs`
- Modify: `tests/Maple.Host.Tests/Stationary/SelfIdentityStabilizerTests.cs`
- Modify: `tests/Maple.Host.Tests/Stationary/VisualStationaryObservationSessionTests.cs`

- [ ] **Step 1: Write failing local-tracking tests**

Add tests proving a character profile acquires in three frames near the saved source, follows successive local moves, accepts a mirrored template, and cannot be stolen by a higher-scoring distant copy. Add failures for a `13px` per-axis jump at 1366 width, local best/second margin below `0.04`, capture fault, and three-frame local recovery.

- [ ] **Step 2: Run observation tests and verify RED**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SelfIdentityStabilizerTests|FullyQualifiedName~VisualStationaryObservationSessionTests"
```

Expected: character cases fail because the session still invokes the name matcher and broad name search.

- [ ] **Step 3: Add optional tracking peak margin**

Extend `SelfIdentityStabilizer` with `minimumTrackingPeakMargin = 0`. Character sessions construct it with acquisition `0.88`, tracking `0.82`, initial margin `0.06`, tracking margin `0.04`, three frames, and scaled 12px jump. Existing name construction retains its current values and behavior.

- [ ] **Step 4: Route character profiles locally**

In `VisualStationaryObservationSession`, retain the last accepted character center beginning at the configured source center. For character profiles, build a search rectangle that permits template origins within scaled 12px on each axis, invoke `SelfAppearanceTemplateMatcher`, and keep the center anchor across untrusted frames. Never use the platform-wide name search for character identity. Continue publishing through the existing direction-specific authorization lock.

- [ ] **Step 5: Run observation tests and verify GREEN**

Run the Task 4 command. Expected: all name and character tests pass.

- [ ] **Step 6: Commit observation work**

```powershell
git add src/Maple.Host/Stationary/SelfIdentityStabilizer.cs src/Maple.Host/Stationary/VisualStationaryObservationSession.cs tests/Maple.Host.Tests/Stationary/SelfIdentityStabilizerTests.cs tests/Maple.Host.Tests/Stationary/VisualStationaryObservationSessionTests.cs
git commit -m "feat: track selected character locally"
```

### Task 5: Change Native Setup To Character Calibration

**Files:**
- Modify: `src/Maple.WindowsHost/Preview/VisualStationarySetupController.cs`
- Modify: `src/Maple.WindowsHost/Preview/PreviewWindowHost.cs`
- Modify: `tests/Maple.Host.Tests/Stationary/VisualStationarySetupTests.cs`

- [ ] **Step 1: Add setup-state tests for pure validation/calibration**

Extend Host setup tests to prove character rectangles use scaled character limits rather than `VISUAL_NAME_TEMPLATE_TOO_TALL`, reverse drags still map correctly, and failed calibration data cannot validate or overwrite a prior valid profile.

- [ ] **Step 2: Implement the two-step character workflow**

Change status and tooltip text to `1/2 框选平台安全范围` and `2/2 框选人物头部和上半身（不含名字、宠物和特效）`. Preserve yellow platform, green derived core, and blue identity colors.

After the character mouse-up:

```csharp
var calibrator = new CharacterAppearanceCalibrator(frozenFrame, selected.Value);
for (int sample = 0; sample < 10; sample++)
{
    await Task.Delay(150, calibrationCancellation.Token);
    if (latestFrame() is { } frame) calibrator.TryAdd(frame);
    setStatus($"人物外观采集中：{calibrator.TemplateCount}/8");
}
VisualCharacterTemplateBank bank = calibrator.Complete();
```

Build a schema 2 `CharacterAppearance` profile and save it atomically. Cancellation and validation failure retain `CurrentProfile`; validation failure returns to step 2 without losing the platform selection.

- [ ] **Step 3: Build WindowsHost**

Run:

```powershell
dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release --no-restore
```

Expected: 0 warnings and 0 errors.

- [ ] **Step 4: Run all visual setup/profile tests**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~VisualStationarySetupTests|FullyQualifiedName~VisualStationaryProfileStoreTests|FullyQualifiedName~CharacterAppearance"
```

Expected: all pass.

- [ ] **Step 5: Commit setup work**

```powershell
git add src/Maple.WindowsHost/Preview/VisualStationarySetupController.cs src/Maple.WindowsHost/Preview/PreviewWindowHost.cs tests/Maple.Host.Tests/Stationary/VisualStationarySetupTests.cs
git commit -m "feat: configure visual identity from character appearance"
```

### Task 6: Regression, Review, And Windows Package

**Files:**
- Modify: `docs/phase-1/evidence/visual-safe-stationary-mode.md`
- Replace generated output: `artifacts/phase-1/win-x64-visual-character-identity/`

- [ ] **Step 1: Run focused visual tests**

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~VisualStationary|FullyQualifiedName~SelfIdentity|FullyQualifiedName~SelfAppearance|FullyQualifiedName~CharacterAppearance"
```

Expected: zero failures.

- [ ] **Step 2: Run complete verification**

```powershell
dotnet build MapleProduct.sln -c Release --no-restore
dotnet test MapleProduct.sln -c Release --no-build --no-restore
npm --prefix client test -- --run
npm --prefix client run lint
npm --prefix client run build
git diff --check
```

Expected: .NET and frontend tests pass; build has zero errors; only documented pre-existing lint/chunk warnings may remain.

- [ ] **Step 3: Request independent code review**

Review profile compatibility, fixed-template immutability, local-only search, ambiguity handling, direction-token revocation, cancellation during calibration, and proof that the original continuous controller is untouched. Resolve every Critical/Important finding before packaging.

- [ ] **Step 4: Publish Windows x64**

Validate the resolved output path is under the repository, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-windows.ps1 -Configuration Release -OutputRoot artifacts\phase-1\win-x64-visual-character-identity
```

- [ ] **Step 5: Validate and record package identity**

Read every ZIP entry, require `Maple.WindowsHost.exe`, `Maple.InputBroker.exe`, `Maple.Host.dll`, and `client/index.html`, compare packaged `Maple.Host.dll` with the fresh Release hash, and record SHA-256 for the EXE, Host DLL, and ZIP in the evidence file.

- [ ] **Step 6: Report the latest executable**

Give the user the absolute clickable path to:

```text
C:\Users\Levi\Desktop\maple-product\maple-product\artifacts\phase-1\win-x64-visual-character-identity\MapleProduct\Maple.WindowsHost.exe
```

Remind the user to close old processes before testing and state that real-game identity behavior remains a Windows acceptance item.
