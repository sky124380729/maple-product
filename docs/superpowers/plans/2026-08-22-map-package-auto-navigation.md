# Map Package Auto Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a generic, manually selected `.mapzip` navigation mode that localizes the character on the package minimap, patrols a ladder-connected platform graph, approaches authorized monsters, attacks, and safely stops on any loss of observation or input safety.

**Architecture:** Keep package loading, localization, graph planning, target authorization, and navigation control as separate Host components. A Windows capture adapter publishes immutable navigation observations; the controller executes one leased Broker action at a time and replans after every fresh observation. React submits only directory/map/session intent and renders Host-published state.

**Tech Stack:** .NET 8, WPF/WebView2, Windows Graphics Capture, ONNX Runtime, `keybd_event` through the existing elevated Broker, React 19, TypeScript, Ant Design, xUnit, Vitest.

---

## Working Tree Constraint

The repository must remain on `master`. Existing unstaged changes in `MainWindow.xaml.cs`, React configuration files, stationary configuration, diagnostics, and their tests belong to the user. Read the current file immediately before each edit, merge incrementally, stage only the files named by the current task, and never reset or overwrite unrelated changes.

### Task 1: Package minimap metadata and map catalog

**Files:**
- Modify: `src/Maple.Host/Navigation/MapPackageLoader.cs`
- Create: `src/Maple.Host/Navigation/MapCatalog.cs`
- Test: `tests/Maple.Host.Tests/Navigation/MapPackageLoaderTests.cs`
- Create: `tests/Maple.Host.Tests/Navigation/MapCatalogTests.cs`

- [ ] **Step 1: Write failing metadata and catalog tests**

Add tests that load `minimap_rect` and `minimap_rect_source`, reject missing/out-of-bounds rectangles for navigation, scan only `.mapzip`, reject duplicate names and filename/manifest mismatches, and detect SHA-256 changes. Use a temporary directory and packages created with `ZipArchive`; do not reference the user's desktop in automated tests.

```csharp
[Fact]
public async Task Loads_navigation_minimap_metadata()
{
    await using MemoryStream package = CreatePackage(
        "{\"format\":\"madudu_map_package\",\"version\":1,\"map_name\":\"Swamp\",\"minimap_rect\":[5,103,223,72],\"minimap_rect_source\":\"manual\"}",
        "{\"platforms\":[]}");
    MapPackageSnapshot snapshot = await MapPackageLoader.LoadAsync(package);
    Assert.Equal(new MapMinimapRect(5, 103, 223, 72), snapshot.MinimapRect);
    Assert.Equal("manual", snapshot.MinimapRectSource);
}
```

- [ ] **Step 2: Run the focused tests and verify red**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --no-restore --filter "FullyQualifiedName~MapPackageLoaderTests|FullyQualifiedName~MapCatalogTests"`

Expected: compile failures for `MapMinimapRect`, `MapCatalog`, and new snapshot members.

- [ ] **Step 3: Implement immutable metadata and catalog contracts**

Add these contracts and validate positive dimensions and non-negative origin while parsing:

```csharp
public sealed record MapMinimapRect(int X, int Y, int Width, int Height);

public sealed record MapCatalogEntry(
    string PackagePath,
    string FileName,
    string Sha256,
    MapPackageSnapshot Snapshot,
    bool CanRun,
    string? WarningCode);

public sealed record MapCatalogResult(
    ImmutableArray<MapCatalogEntry> Entries,
    ImmutableArray<string> Errors);
```

`MapCatalog.ScanAsync(directory)` canonicalizes the directory, rejects a directory or package carrying the Windows `ReparsePoint` attribute, enumerates top-level `*.mapzip`, loads each with `MapPackageLoader`, computes SHA-256, and sorts by map name. The catalog marks `MAP_NAME_MISMATCH` when the filename before the optional `(level)` suffix does not equal `snapshot.Name`; it never follows subdirectories or links.

- [ ] **Step 4: Run focused tests and commit**

Run the Task 1 filter again. Expected: all pass.

Commit only Task 1 files with `git commit -m "feat: catalog navigation map packages"`.

### Task 2: Fixed-ROI map matching and character localization

**Files:**
- Create: `src/Maple.Host/Navigation/MapSignatureMatcher.cs`
- Create: `src/Maple.Host/Navigation/MinimapLocalizer.cs`
- Create: `src/Maple.Host/Navigation/NavigationObservation.cs`
- Create: `tests/Maple.Host.Tests/Navigation/MapSignatureMatcherTests.cs`
- Create: `tests/Maple.Host.Tests/Navigation/MinimapLocalizerTests.cs`

- [ ] **Step 1: Write failing pixel-frame tests**

Build synthetic BGRA frames with a package ROI, green platform segments, neutral ladder segments, and a yellow character marker. Cover five-frame preflight, three-frame mismatch, viewport rejection, unique platform assignment, ladder-transit null platform, and ambiguous platform rejection.

```csharp
NavigationLocalization localization = localizer.Observe(frame, map, NavigationTraversal.None);
Assert.Equal(new MapPoint(82, 55), localization.Self);
Assert.Equal(0, localization.PlatformId);
Assert.True(localization.MapMatched);
```

- [ ] **Step 2: Verify tests fail**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --no-restore --filter "FullyQualifiedName~MapSignatureMatcherTests|FullyQualifiedName~MinimapLocalizerTests"`

Expected: compile failures for matcher/localizer contracts.

- [ ] **Step 3: Implement stateless frame analysis plus stateful gates**

Define immutable output:

```csharp
public sealed record MapPoint(double X, double Y);
public sealed record NavigationLocalization(
    long FrameSequence,
    long CapturedAtMonoMs,
    bool MapMatched,
    double MatchConfidence,
    MapPoint? Self,
    int? PlatformId,
    string? FaultCode);
```

`MapSignatureMatcher` crops only `snapshot.MinimapRect`, compares expected platform/ladder raster locations in normalized ROI space, and requires 70% platform coverage with distance no greater than `Thresholds.Match`. `MinimapLocalizer` detects the largest plausible yellow component, maps its center to ROI pixels, and selects exactly one platform within X `+/-3` and Y `+/-5`. `NavigationLocalizationGate` requires five matching frames before arming and rejects three consecutive mismatches or 500ms staleness.

- [ ] **Step 4: Run tests and commit**

Run the Task 2 filter. Expected: all pass.

Commit with `git commit -m "feat: localize navigation maps from fixed minimap roi"`.

### Task 3: Ladder graph, A* and patrol selection

**Files:**
- Create: `src/Maple.Host/Navigation/NavigationGraph.cs`
- Create: `src/Maple.Host/Navigation/PatrolTargetSelector.cs`
- Create: `tests/Maple.Host.Tests/Navigation/NavigationGraphTests.cs`
- Create: `tests/Maple.Host.Tests/Navigation/PatrolTargetSelectorTests.cs`

- [ ] **Step 1: Write failing graph tests using the Swamp 3 shape**

Create a seven-platform/six-ladder in-memory snapshot matching the package topology without reading the desktop package. Assert every ordered platform pair has a route, route `3 -> 6` crosses platforms `3,2,1,0,4,5,6`, disconnected ladder-only maps return `MAP_GRAPH_UNSUPPORTED`, and patrol selects the least recently visited reachable platform while excluding the current platform.

```csharp
NavigationRoute route = graph.FindRoute(3, 6, currentX: 95);
Assert.True(route.Success);
Assert.Equal([3, 2, 1, 0, 4, 5, 6], route.PlatformIds);
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --no-restore --filter "FullyQualifiedName~NavigationGraphTests|FullyQualifiedName~PatrolTargetSelectorTests"`

Expected: compile failures for graph and selector.

- [ ] **Step 3: Implement deterministic A* and LRU patrol**

Represent each ladder traversal as:

```csharp
public sealed record NavigationEdge(
    int FromPlatformId,
    int ToPlatformId,
    int LadderId,
    double ApproachX,
    NavigationVerticalDirection Direction,
    double Cost);
```

Validate a connected ladder-only graph at construction. A* uses horizontal approach distance plus ladder vertical span; ties sort by destination platform ID and ladder ID. `PatrolTargetSelector` stores monotonic arrival times, picks the oldest reachable non-current platform, and uses route cost then platform ID as deterministic ties.

- [ ] **Step 4: Run tests and commit**

Run the Task 3 filter. Expected: all pass.

Commit with `git commit -m "feat: plan ladder-connected patrol routes"`.

### Task 4: Package monster template authorization

**Files:**
- Create: `src/Maple.Host/Navigation/MonsterTemplateMatcher.cs`
- Create: `src/Maple.Host/Navigation/MonsterTargetStabilizer.cs`
- Create: `src/Maple.WindowsHost/Navigation/MapTemplateDecoder.cs`
- Create: `tests/Maple.Host.Tests/Navigation/MonsterTemplateMatcherTests.cs`

- [ ] **Step 1: Write failing raw-BGRA matcher tests**

Use a small in-memory transparent BGRA sprite and frame. Assert a candidate requires package correlation threshold in two of three distinct frame sequences, HUD/minimap candidates are excluded, overlapping Player/Self boxes suppress the candidate, and a generic recognition-only monster cannot authorize attack.

```csharp
AuthorizedMonsterSnapshot result = stabilizer.Update(
    frameSequence: 3,
    templateMatches: [new MonsterCandidate(400, 300, 20, 18, 0.72)],
    recognitionMonsters: [],
    excludedActors: []);
Assert.Single(result.Monsters);
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --no-restore --filter "FullyQualifiedName~MonsterTemplateMatcherTests"`

Expected: compile failures for template and authorization contracts.

- [ ] **Step 3: Implement matcher and Windows PNG decoder**

`MonsterTemplateMatcher` accepts decoded immutable `BgraTemplate` values and scans only the gameplay region at native scale for the first version. It samples opaque pixels, uses mean color-distance correlation, applies `Thresholds.MonsterColorCorrelation`, ground-support filtering, and IoU suppression. `MapTemplateDecoder` reads only loader-approved `mob_templates/` entries from the selected package and decodes PNG through WPF `BitmapDecoder`; no extraction to disk occurs.

- [ ] **Step 4: Run tests and commit**

Run Task 4 tests and `dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release --no-restore`.

Expected: tests pass and build has zero errors.

Commit with `git commit -m "feat: authorize map monsters from package templates"`.

### Task 5: Broker Up/Down actions

**Files:**
- Modify: `src/Maple.Core/Broker/BrokerProtocol.cs`
- Modify: `src/Maple.Host/Broker/NamedPipeBrokerClient.cs`
- Create: `src/Maple.Host/Navigation/NavigationInputContracts.cs`
- Modify: `src/Maple.InputBroker/BrokerInputSession.cs`
- Modify: `src/Maple.InputBroker/KeybdEventInputAdapter.cs`
- Modify: `tests/Maple.InputBroker.Tests/Broker/BrokerInputSessionTests.cs`
- Modify: `tests/Maple.InputBroker.Tests/Broker/KeybdEventInputAdapterTests.cs`
- Modify: `tests/Maple.Host.Tests/Broker/NamedPipeBrokerClientTests.cs`

- [ ] **Step 1: Write failing protocol and physical-key tests**

Assert `MoveUp` only accepts key `Up`, `MoveDown` only accepts `Down`, opposite vertical actions release each other, horizontal and vertical directions cannot remain held together, and mappings are `Up=VK 0x26/scan 0x48/extended`, `Down=VK 0x28/scan 0x50/extended`.

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Maple.InputBroker.Tests/Maple.InputBroker.Tests.csproj --no-restore --filter "FullyQualifiedName~BrokerInputSessionTests|FullyQualifiedName~KeybdEventInputAdapterTests"`

Expected: enum/mapping failures.

- [ ] **Step 3: Add navigation action sink and bump protocol version**

Define:

```csharp
public enum NavigationInputAction { Attack, MoveLeft, MoveRight, MoveUp, MoveDown }
public interface INavigationActionSink
{
    Task<InputActionResult> KeyDownAsync(NavigationInputAction action, int leaseMs, CancellationToken token);
    Task<InputActionResult> KeyUpAsync(NavigationInputAction action, CancellationToken token);
    Task<InputActionResult> ReleaseAllAsync(CancellationToken token);
}
```

Increment `BrokerProtocol.Version`, add `MoveUp/MoveDown` logical actions, overload `NamedPipeBrokerClient` for navigation actions, validate 1-5000ms movement leases, and release every other held movement direction before a new movement key-down. Attack may overlap no movement action: beginning Attack releases movement, and beginning movement releases Attack.

- [ ] **Step 4: Run Core/Host/Broker tests and commit**

Run: `dotnet test MapleProduct.sln --no-restore`

Expected: zero failures.

Commit with `git commit -m "feat: support vertical navigation input"`.

### Task 6: Closed-loop navigation controller

**Files:**
- Create: `src/Maple.Host/Navigation/NavigationContracts.cs`
- Create: `src/Maple.Host/Navigation/NavigationController.cs`
- Create: `tests/Maple.Host.Tests/Navigation/NavigationControllerTests.cs`

- [ ] **Step 1: Write failing controller state-machine tests**

Use a scripted observation source, fake monotonic scheduler, fake safety gate, and recording action sink. Cover: walk to ladder, align, climb up/down in pulses, verify target platform, choose patrol target, approach/attack same-platform monster, monster disappearance replan, three no-progress pulses stop, connector timeout, stale observation, map mismatch, cancellation, and unconditional release.

```csharp
await controller.RunAsync(sessionId, cancellation.Token);
Assert.Contains(NavigationInputAction.MoveUp, actions.DownActions);
Assert.Equal("NAVIGATION_STUCK", publisher.Last.StopReason);
Assert.Equal(1, actions.ReleaseAllCount);
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --no-restore --filter "FullyQualifiedName~NavigationControllerTests"`

Expected: compile failures for controller contracts.

- [ ] **Step 3: Implement one-action-at-a-time controller**

Define `NavigationPhase`, `NavigationState`, `INavigationObservationSource`, `INavigationSafetyGate`, and `INavigationStatePublisher`. The loop must consume a newer frame sequence before acting again. Horizontal pulses are clamped to 40-120ms, vertical pulses to 80-150ms, and attacks to 200-400ms. Every key-down has a `finally` key-up; outer `finally` always calls `ReleaseAllAsync(CancellationToken.None)`.

Do not reuse `StationaryMovementPlanner` or its cumulative movement budget. The selected map platform bounds and fresh localization are the navigation boundary.

- [ ] **Step 4: Run tests and commit**

Run Task 6 tests plus all Host tests. Expected: zero failures.

Commit with `git commit -m "feat: run closed-loop map navigation"`.

### Task 7: Windows navigation observation and session ownership

**Files:**
- Create: `src/Maple.WindowsHost/Navigation/NavigationObservationSession.cs`
- Create: `src/Maple.Host/Windows/NavigationSessionApplicationService.cs`
- Create: `src/Maple.WindowsHost/MainWindow.Navigation.cs`
- Modify: `src/Maple.WindowsHost/MainWindow.xaml.cs`
- Create: `tests/Maple.Host.Tests/Windows/NavigationSessionApplicationServiceTests.cs`

- [ ] **Step 1: Write failing preparation/lifecycle tests**

Assert preparation finds exactly one client, activates it, starts Broker without an initial-facing prompt, revalidates foreground after UAC, and disposes the Broker if post-UAC validation fails. Add lifecycle tests proving stop during preflight cancels start and that stationary/navigation sessions remain mutually exclusive.

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --no-restore --filter "FullyQualifiedName~NavigationSessionApplicationServiceTests"`

Expected: compile failures for navigation preparation service.

- [ ] **Step 3: Implement capture and orchestration**

`NavigationObservationSession` owns one `WindowsGraphicsCaptureSource`, one recognition provider, fixed-ROI localization, signature gate, template matcher, and a latest-observation channel. It processes lightweight minimap localization on every captured frame and throttles ONNX/template analysis without reusing an old frame sequence as new evidence. A session-pinned package watcher recomputes SHA-256 before arming and after every one-second monotonic interval; a mismatch stops with `MAP_PACKAGE_CHANGED` before another input action.

`MainWindow.Navigation.cs` handles `chooseMapDirectory`, `loadMapCatalog`, `startNavigation`, and `stopNavigation`. It stops stationary input before navigation, owns cancellation/Broker/heartbeat/capture/controller as one generation, and cleans them up exactly once. Closing the app awaits navigation cleanup before disposing shared logs and notifications.

- [ ] **Step 4: Build and commit**

Run Host tests and `dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release --no-restore`.

Expected: zero failures, warnings reviewed.

Commit only Task 7 files with `git commit -m "feat: host Windows navigation sessions"`.

### Task 8: React map selection and navigation status

**Files:**
- Modify: `client/src/bridge/types.ts`
- Create: `client/src/components/NavigationControls.tsx`
- Create: `client/src/components/NavigationStatus.tsx`
- Modify: `client/src/pages/StationaryAttackPage.tsx`
- Modify: `client/src/pages/StationaryAttackPage.test.tsx`
- Modify: `client/src/styles/app.css`

- [ ] **Step 1: Write failing UI tests**

Test that the map dropdown is populated from `navigation.catalog.loaded`, invalid/mismatched packages are disabled, directory selection posts only an intent, start posts `{ command: 'startNavigation', packagePath }`, stationary and navigation starts are mutually exclusive, and state renders map/platform/path/action/fault without exposing raw key commands.

- [ ] **Step 2: Verify red**

Run: `npm test -- --run client/src/pages/StationaryAttackPage.test.tsx` from `client`.

Expected: missing navigation controls/messages.

- [ ] **Step 3: Implement compact operational controls**

Add a mode segmented control for stationary/navigation, a folder icon button, map `Select`, and one start/stop command. Reuse the existing header rather than adding nested cards. `NavigationStatus` renders compact operational fields and stable errors; it does not display instructions or raw keyboard details.

- [ ] **Step 4: Run React tests/build and commit**

Run: `npm test -- --run` and `npm run build`.

Expected: all tests pass; only the existing bundle-size warning is allowed.

Commit only Task 8 files with `git commit -m "feat: control automatic navigation from React"`.

### Task 9: Cross-component integration tests and evidence

**Files:**
- Create: `tests/Maple.Host.Tests/Navigation/SwampNavigationIntegrationTests.cs`
- Modify: `docs/PHASE_2_AUTO_NAVIGATION_SCOPE.md`
- Create: `docs/phase-2/evidence/map-package-auto-navigation.md`

- [ ] **Step 1: Add deterministic end-to-end simulation**

Create an in-memory Swamp 3 map and scripted observations that begin on platform 3, traverse down the left branch, cross platform 0, climb the right branch, authorize one same-platform monster, attack, and continue patrol. Assert all seven platform IDs are visited, both Up and Down are emitted, Attack occurs only after template authorization, and final release succeeds.

- [ ] **Step 2: Run focused and full verification**

Run:

```powershell
dotnet test MapleProduct.sln --no-restore
npm --prefix client test -- --run
npm --prefix client run build
dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release --no-restore
git diff --check
```

Expected: zero test/build failures and zero whitespace errors.

- [ ] **Step 3: Document automated evidence and commit**

Record exact test counts, package count, simulated platform coverage, safety-stop coverage, and remaining real-Windows requirement. Check only scope items actually proven.

Commit with `git commit -m "test: verify map package navigation flow"`.

### Task 10: Publish and complete full Windows acceptance

**Files:**
- Modify: `docs/phase-2/evidence/map-package-auto-navigation.md`
- Output only: `artifacts/auto-navigation-v1/win-x64/MapleProduct/`

- [ ] **Step 1: Publish a fresh self-contained build**

Run:

```powershell
& .\scripts\publish-windows.ps1 -Configuration Release -OutputRoot 'artifacts/auto-navigation-v1/win-x64'
```

Expected: Host and Broker publish successfully with the React build included.

- [ ] **Step 2: Run startup and package-catalog smoke checks**

Start `Maple.WindowsHost.exe`, verify the window opens, choose `C:\Users\Levi\Desktop\辅助\Kaelo_ok_sp\Kaelo_ok_sp\saved_maps`, and verify 42 packages load with the known mismatched packages disabled. Close the smoke instance cleanly before live input acceptance.

- [ ] **Step 3: Run the user-requested complete acceptance on Swamp 3**

With the 1366x768 client foregrounded in 沼泽地3, select the matching package and start complete navigation. Verify from diagnostics and observed gameplay that the session localizes the correct starting platform, visits multiple platforms using Up and Down, attacks only an authorized same-platform monster, resumes least-recently-visited patrol when no monster is present, and releases all input on Stop.

If any safety gate fails, record the exact stable code, fix through a red-green regression, republish, and repeat. Do not weaken map matching or input safety to force acceptance.

- [ ] **Step 4: Final verification, evidence, commit and push**

Re-run all commands from Task 9 Step 2 after the final live-test fix. Update evidence with the actual client result and executable path. Stage only navigation-owned files, commit with `git commit -m "feat: complete map package auto navigation"`, and push `master`.
