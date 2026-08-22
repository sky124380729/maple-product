# Visual Character Tracking Threshold 75% Design

## Scope

Only lower the schema 2 `CharacterAppearance` tracking and recovery score threshold from `0.82` to `0.75`.

## Unchanged Behavior

- Initial acquisition remains `0.88` with a `0.06` peak margin and three new frames.
- Tracking/recovery peak margin remains `0.04`, with three new frames required after trust is lost.
- Local search radius, fixed templates, horizontal mirrors, platform guards, random movement, input delivery, and fail-closed behavior remain unchanged.
- Schema 1 name-template thresholds remain unchanged.

## Verification

- A character stabilizer accepts a local `0.75` candidate after trust is established.
- A `0.74` candidate revokes trust, and recovery still requires three `0.75` frames.
- The production observation session exposes and uses the `0.75` character tracking threshold.
- Full Release tests and Windows x64 packaging run before delivery.
