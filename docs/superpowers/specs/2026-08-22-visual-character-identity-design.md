# Visual Character Identity Design

## Goal

Replace the fragile mandatory name-template identity in newly configured visual stationary sessions with a manually selected character-appearance identity. The new tracker must tolerate ordinary idle/attack animation, facing changes, and partial pet occlusion without ever falling back to a global "nearest player" choice.

The existing random attack and movement behavior, platform safety gate, facing recovery gate, Broker input path, and original continuous-attack mode remain unchanged.

## Confirmed Selection Semantics

- The yellow rectangle is the platform outer safety range selected by the user. It may contain the character and terrain; horizontal bounds are authoritative for movement safety.
- The green rectangle is the automatically derived safe core after left/right guard bands are removed. It is not a second user selection.
- The blue rectangle is the identity selection. Existing profiles use a name row; new profiles use the character's head and upper body.

The reported `VISUAL_NAME_TEMPLATE_TOO_TALL` was produced because the current profile validator always interprets the blue selection as a single-line name template. The user's platform selection was not the cause.

## Chosen Approach

New visual profiles use `CharacterAppearance` identity by default. The user selects the platform, then tightly selects the character's head and upper body while excluding the name plate, pet, and large weapon/skill effects.

After the second selection, setup keeps receiving live frames for 1.5 seconds and samples at 150ms intervals. It aligns candidates within 6 scaled pixels on each axis around the selected location and stores at most eight appearance templates. A sample is added only when its similarity to every stored template is below `0.97`; duplicate animation frames are discarded. Horizontally mirrored variants are generated at runtime so facing changes do not require another selection. The frozen selection frame is always the first template, so calibration cannot complete with an empty bank.

This is preferred over:

- increasing the name-height limit, which does not solve pet and effect occlusion;
- one full-frame character template, which is brittle across animation and facing;
- a generic player detector, which can promote another player to self;
- online template replacement, which can permanently drift from the character to a pet or overlapping player.

## Profile And Compatibility

Visual profile schema version 2 adds an identity kind and an immutable appearance template bank. Character profiles store:

- capture viewport dimensions;
- platform outer rectangle;
- selected character source rectangle;
- one to eight same-size BGRA templates;
- calibration timestamp and matcher version.

Version 1 name profiles remain readable and continue through the existing name matcher. They are marked as legacy in structured status. Reconfiguring creates a version 2 character profile; no existing profile is silently reinterpreted as character pixels.

Character selection is rejected when it is outside the frame, smaller than `24x32` pixels or larger than `112x144` pixels at a 1366-wide reference viewport, after proportional width scaling, or visually uniform. It is not subject to `VISUAL_NAME_TEMPLATE_TOO_TALL`. Validation errors use character-specific stable codes.

## Matching And Tracking

The matcher compares each fixed calibration template and its horizontal mirror against candidates in a bounded local search region. It uses the existing allocation-conscious robust color/edge score, including the capped local feature-loss component, generalized to a template bank. No full-frame player search is permitted. All pixel distances below are multiplied by `frameWidth / 1366` with a minimum of one pixel.

Identity state follows these rules:

1. Session acquisition searches at most 12 pixels on each axis around the saved character source position and requires three increasing frame sequences. The best score must be at least `0.88` and exceed a spatially distinct second candidate by at least `0.06`.
2. Once trusted, the next candidate must remain within 12 pixels on each axis around the last accepted center and score at least `0.82`.
3. A second candidate is distinct when its center is separated by at least one third of the selected template width. During tracking, the best local candidate must exceed a distinct second candidate by at least `0.04`.
4. A distant candidate, even with a higher raw score, cannot replace the local track.
5. Missing, ambiguous, low-score, stale, or excessive-jump observations immediately become untrusted and cancel both movement directions.
6. Recovery searches only within the same 12-pixel-per-axis neighborhood around the last accepted center and again requires three increasing frames at the `0.82` tracking threshold and `0.04` local margin. It never resets into a global player search.
7. Runtime frames never replace or append to the fixed template bank.

The platform safety gate consumes the tracked character center X. Negative visual offset remains left of the platform center and positive remains right.

## Occlusion And Animation Behavior

The robust score is tested with up to 20% synthetic patch occlusion from a pet or effect, but safety remains fail-closed. A usable unoccluded portion must still uniquely match one of the calibrated or mirrored templates. If that evidence is insufficient, lateral movement freezes immediately; the tracker does not guess.

Attack may continue during ordinary visual loss only under the existing facing invariant. If the first movement segment already changed facing, `FacingRestorePending` continues to block the next attack until character identity and an authorized restoring movement return.

## Setup And Runtime Status

Setup status explicitly shows two steps:

- `1/2`: select the platform outer safety range;
- `2/2`: select the character head and upper body, excluding name, pet, and effects.

During the 1.5-second collection window, the preview shows calibration progress and does not accept another drag. Success publishes `CharacterAppearance`, template count, source rectangle, and viewport size. Failures keep the platform selection and return to character selection.

Runtime structured state continues to expose confidence, safety state, guard width, and signed visual offset. It additionally exposes `identityKind=CharacterAppearance`. React receives no pixels or template data.

## Safety And Failure Paths

- Capture closure, viewport mismatch, focus loss, Broker failure, cancellation, and release failure retain their existing stop/release behavior.
- Character identity loss cancels direction authorization atomically through the existing observation session.
- Calibration cancellation saves nothing; the previous valid profile remains intact.
- A partially written or invalid schema 2 profile is rejected and cannot replace the prior profile.
- Character matching does not send input and cannot bypass the direction-bound movement authorization or platform guard state.

## Verification

Automated tests cover:

- schema 1 name-profile compatibility and schema 2 character-profile round trips;
- character selection dimensions and texture validation without the name-height rule;
- multi-template and mirrored matching;
- small local movement across increasing frames;
- partial synthetic occlusion;
- a higher-scoring distant player that cannot steal the track;
- local ambiguity, low confidence, stale frames, capture failure, and excessive jumps revoking movement;
- three-frame acquisition and recovery;
- unchanged platform guards, facing recovery, random movement, and original continuous mode;
- setup cancellation and failed calibration preserving the previous profile.

Windows acceptance uses the real selected character with idle and attack animation, the pet overlapping the body/name area, another player crossing nearby, and repeated movement toward both platform guards. The result must remain fail-closed and must not promote another player to self.

## Legacy Reference

The desktop `辅助/Kaelo_ok_sp` package is a binary distribution with an OpenCV runtime but no reviewable source for its self-identification algorithm. It is therefore only evidence that OpenCV-style vision is operationally feasible, not a source to copy. This implementation stays within the current repository's capture, matching, safety, and test boundaries.
