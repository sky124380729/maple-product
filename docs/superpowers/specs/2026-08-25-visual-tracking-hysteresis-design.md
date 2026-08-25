# Visual Tracking Hysteresis Design

## Problem

Session `7dd9938d-b019-4d7e-9178-3ab04ee64681` remained inside the configured platform boundary. At cycle 184 the last trusted offset was about `-27px`, while the configured safe half-width was `84px`. The appearance score nevertheless crossed from `0.702` to `0.692`. Acquisition and established tracking both used a hard `0.70` threshold, so one small score change revoked trust. Later scores oscillated between `0.692` and `0.704`, preventing three consecutive recovery frames and forcing continuous fallback.

## Decision

Keep character acquisition at `0.70`. After the same character has been established, accept local tracking candidates at `0.68`. This creates a two-point hysteresis band for animation and rendering variation without widening the search area.

The existing robust score can give a uniform missing-character patch about `0.6906`, because per-sample losses are deliberately capped for occlusion tolerance. Before enabling the lower tracking threshold, the matcher must reject a candidate whose sampled luminance range is below `16`. This mirrors the existing template texture requirement and prevents the robust baseline from turning an empty local patch into a tracked identity.

Initial acquisition and established tracking both use a two-level search. The fast pass uses the saved source or last committed anchor inside a scaled `12px` window. If initial local evidence misses `0.70/0.06`, or established local evidence misses `0.68/0.04`, a second pass scans the complete user-selected yellow platform rectangle. An initial or distant yellow-area candidate must meet the acquisition score `0.70`, acquisition peak margin `0.06`, and three-new-frame stability before it can establish or rebase the committed anchor. This lets a character start or move anywhere inside the configured platform without leaving the recognition ROI while keeping one low-score background frame from walking the identity anchor away.

The green safe interior, edge guard width, and recenter band never crop either recognition pass; they authorize movement only after identity has been resolved. A local score below `0.68`, a yellow-area recovery score below `0.70`, ambiguity, an unconfirmed jump, a stale frame, or capture failure still revokes movement authorization immediately.

The yellow-area pass evaluates every pixel position with the 16 strongest edge samples from each template bank. It then reevaluates a `2px` neighborhood around both the best spatial peak and the best peak outside the same-target exclusion radius using the full sample banks (up to 128 samples each). Final ordering and ambiguity use the full refined scores. Local anchor matching uses full banks directly. This preserves exact-pixel full-area coverage and second-person rejection without multiplying the eight-template hot path by all 128 samples at every yellow-area pixel.

## Scope

Only the appearance tracking threshold, candidate low-texture guard, two-level appearance search, relocation stability, and their tests change. Initial acquisition, random movement, platform correction, fallback timing, and name-template compatibility remain unchanged.

## Verification

- A previously trusted local appearance remains trusted at score `0.692`.
- Initial acquisition at score `0.692` remains rejected.
- An established local track is revoked at score `0.67`.
- A uniform local patch scores below `0.68` and cannot preserve or recover identity.
- A character candidate outside the green safe interior remains searchable around the trusted motion anchor.
- A previously established character displaced beyond the local window is found anywhere inside the yellow platform rectangle.
- A new session can acquire the configured appearance anywhere inside the yellow platform rectangle without redrawing the blue source.
- A distant yellow-area candidate can rebase only after three unique `0.70` recovery frames.
- The maximum eight-template bank can acquire an arbitrary-pixel yellow-area candidate through sparse-to-full refinement.
- Two arbitrary-pixel spatial candidates remain ambiguous after both peaks are fully refined.
- Existing ambiguity and jump tests continue to pass.
