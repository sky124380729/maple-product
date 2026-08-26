# Visual Profile Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users reconfigure only the platform while reusing a persistent character template bank, and show the saved character templates with a separate update action.

**Architecture:** Keep the existing schema 2 profile file as the atomic persistence unit, but add a pure profile editor that preserves the character bank when replacing the platform. Split the native setup controller into platform and character entry points, then render a compact template preview row outside the captured game image.

**Tech Stack:** .NET 8, C#, WPF, xUnit, React, TypeScript, Vitest

---

### Task 1: Character template metadata and atomic platform editing

**Files:**
- Create: `src/Maple.Host/Stationary/VisualStationaryProfileEditor.cs`
- Create: `tests/Maple.Host.Tests/Stationary/VisualStationaryProfileEditorTests.cs`
- Modify: `src/Maple.Host/Stationary/VisualStationaryContracts.cs`
- Modify: `src/Maple.Host/Stationary/CharacterAppearanceCalibrator.cs`
- Modify: `tests/Maple.Host.Tests/Stationary/VisualStationaryProfileStoreTests.cs`

- [ ] **Step 1: Write failing profile editor and compatibility tests**

Add tests that call:

```csharp
VisualProfileEditResult result = VisualStationaryProfileEditor.ReplacePlatform(
    profile,
    new FrameRect(180, 260, 700, 120),
    1366,
    768,
    DateTimeOffset.Parse("2026-08-26T01:00:00Z"));

Assert.True(result.Success);
Assert.Same(profile.CharacterAppearance, result.Profile!.CharacterAppearance);
Assert.Equal(profile.CharacterAppearance!.CapturedAtUtc, result.Profile.CharacterAppearance!.CapturedAtUtc);
Assert.Equal(new FrameRect(180, 260, 700, 120), result.Profile.Platform);
```

Also assert that a legacy name profile returns `VISUAL_CHARACTER_TEMPLATE_NOT_CONFIGURED`, a viewport mismatch returns `VISUAL_VIEWPORT_MISMATCH`, and JSON without `capturedAtUtc` loads with a null bank timestamp.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~VisualStationaryProfileEditorTests|FullyQualifiedName~VisualStationaryProfileStoreTests"
```

Expected: compilation fails because `CapturedAtUtc`, `VisualProfileEditResult`, and `VisualStationaryProfileEditor` do not exist.

- [ ] **Step 3: Add the minimal metadata and editor**

Extend the template bank without breaking old schema 2 JSON:

```csharp
public sealed record VisualCharacterTemplateBank(
    FrameRect Source,
    int TemplateWidth,
    int TemplateHeight,
    byte[][] TemplatesBgra,
    int MatcherVersion,
    DateTimeOffset? CapturedAtUtc = null);
```

Add `VisualProfileEditResult` and `VisualStationaryProfileEditor.ReplacePlatform`. It must require a valid character bank and matching viewport, construct the candidate with `profile with { Platform = platform, UpdatedAtUtc = updatedAtUtc }`, run `VisualStationaryProfileValidator.Validate`, and return the candidate only when valid. Change `CharacterAppearanceCalibrator.Complete` to accept an optional capture timestamp and place it in the bank.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the command from Step 2. Expected: all selected tests pass.

- [ ] **Step 5: Commit the profile editing unit**

```powershell
git add src/Maple.Host/Stationary/VisualStationaryProfileEditor.cs src/Maple.Host/Stationary/VisualStationaryContracts.cs src/Maple.Host/Stationary/CharacterAppearanceCalibrator.cs tests/Maple.Host.Tests/Stationary/VisualStationaryProfileEditorTests.cs tests/Maple.Host.Tests/Stationary/VisualStationaryProfileStoreTests.cs
git commit -m "feat: reuse character templates across platforms"
```

### Task 2: Split native platform and character setup flows

**Files:**
- Modify: `src/Maple.WindowsHost/Preview/VisualStationarySetupController.cs`
- Modify: `src/Maple.WindowsHost/Preview/PreviewWindowHost.cs`

- [ ] **Step 1: Route platform completion through the tested editor**

Replace `Begin()` with explicit `BeginPlatformSetup()` and `BeginCharacterSetup()` entry points. `BeginPlatformSetup()` freezes the latest frame and enters `SetupStep.Platform`. After a valid yellow rectangle, call `VisualStationaryProfileEditor.ReplacePlatform`; if it succeeds, save and publish immediately. Only `VISUAL_CHARACTER_TEMPLATE_NOT_CONFIGURED` continues to `SetupStep.Character`.

- [ ] **Step 2: Preserve the platform during a character-only update**

`BeginCharacterSetup()` must reuse `CurrentProfile.Platform`, freeze the latest frame, and enter `SetupStep.Character`. If no platform exists, it starts the first-time platform flow. On successful calibration, create schema 2 with the preserved platform and call:

```csharp
VisualCharacterTemplateBank bank = calibrator.Complete(DateTimeOffset.UtcNow);
```

The profile is assigned and published only after validation and `store.SaveAsync` succeed. Cancel and all exceptions leave `CurrentProfile` unchanged.

- [ ] **Step 3: Add separate native actions**

In `PreviewWindowHost`, rename the setup action to `配置平台`, add `更新人物模板`, and route clicks to the two controller entry points. Both actions use the existing `CanClearVisualProfile` mutation gate. Keep `BeginVisualSetup()` as the main-window platform entry.

- [ ] **Step 4: Build WindowsHost**

Run:

```powershell
dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release -r win-x64
```

Expected: build succeeds with zero errors.

- [ ] **Step 5: Commit the split workflow**

```powershell
git add src/Maple.WindowsHost/Preview/VisualStationarySetupController.cs src/Maple.WindowsHost/Preview/PreviewWindowHost.cs
git commit -m "feat: split platform and character setup"
```

### Task 3: Saved character template preview

**Files:**
- Create: `src/Maple.Host/Stationary/VisualCharacterTemplatePreview.cs`
- Create: `tests/Maple.Host.Tests/Stationary/VisualCharacterTemplatePreviewTests.cs`
- Modify: `src/Maple.WindowsHost/Preview/PreviewWindowHost.cs`

- [ ] **Step 1: Write failing preview model tests**

Test that `VisualCharacterTemplatePreview.Create(profile)` returns one item per real template, preserves width, height and pixels, uses `bank.CapturedAtUtc` when present, falls back to `profile.UpdatedAtUtc` for an old schema 2 bank, and returns null for a name profile.

- [ ] **Step 2: Run preview tests and verify RED**

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~VisualCharacterTemplatePreviewTests
```

Expected: compilation fails because the preview model does not exist.

- [ ] **Step 3: Implement the preview model and native thumbnail row**

Create immutable preview records containing `Width`, `Height`, `ReadOnlyMemory<byte> BgraPixels`, and `CapturedAtUtc`. In `PreviewWindowHost`, add a collapsed row outside `imageLayer`; when a character profile loads or saves, populate it with up to eight WPF `Image` controls backed by `WriteableBitmap`, plus `人物模板 N 张` and the local capture time. Clear or name profiles collapse the row. Thumbnails use fixed maximum dimensions and `Stretch.Uniform` so they cannot resize the main preview layout.

- [ ] **Step 4: Run preview tests and WindowsHost build**

Run the test from Step 2 and the build from Task 2 Step 4. Expected: both pass.

- [ ] **Step 5: Commit the preview**

```powershell
git add src/Maple.Host/Stationary/VisualCharacterTemplatePreview.cs tests/Maple.Host.Tests/Stationary/VisualCharacterTemplatePreviewTests.cs src/Maple.WindowsHost/Preview/PreviewWindowHost.cs
git commit -m "feat: preview saved character templates"
```

### Task 4: Main-window wording and full verification

**Files:**
- Modify: `client/src/pages/StationaryAttackPage.tsx`
- Modify: `client/src/pages/StationaryAttackPage.test.tsx`

- [ ] **Step 1: Write the failing wording test**

Change the existing setup test to find `配置平台安全区`, click it, and still assert only `{ command: 'openVisualStationarySetup' }` is sent. Assert the old `配置视觉安全区` label is absent.

- [ ] **Step 2: Run the focused frontend test and verify RED**

```powershell
npm test -- --run src/pages/StationaryAttackPage.test.tsx
```

Run from `client`. Expected: failure because the old label is still rendered.

- [ ] **Step 3: Update compact wording**

Rename the main action and tooltip to `配置平台安全区`. Keep the clear action and clarify its confirmation text as `清空后将删除平台和已采集人物模板。` No new bridge command or template pixels are added to React.

- [ ] **Step 4: Run all verification**

```powershell
dotnet test MapleProduct.sln --no-restore
cd client
npm test -- --run
npm run lint
npm run build
```

Expected: all .NET and frontend tests pass, lint has no new errors, and the production frontend builds.

- [ ] **Step 5: Commit, publish, and push**

```powershell
git add client/src/pages/StationaryAttackPage.tsx client/src/pages/StationaryAttackPage.test.tsx docs/superpowers/plans/2026-08-26-visual-profile-lifecycle.md
git commit -m "feat: clarify visual profile controls"
powershell -ExecutionPolicy Bypass -File scripts/publish-windows.ps1 -OutputRoot artifacts/phase-1/win-x64-persistent-character-templates
git push origin master
```

Expected: `MapleProduct/Maple.WindowsHost.exe` and `MapleProduct-phase-1-win-x64.zip` exist under the new output root, and `master` matches `origin/master`.
