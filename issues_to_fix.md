# Improvement Report

This report lists practical fixes and improvements for the Spotify plugin within its existing MusicBee plugin setup. It focuses on reliability, security, usability, and maintainability without changing the host requirements.

## Summary

The plugin already includes several useful safeguards: PKCE authentication, protection against duplicate authentication attempts, stale-search protection, search-result caching, artwork caching, and diagnostic logging.

The most important remaining work is to protect persisted tokens, make authentication and API failures visible and recoverable, and prevent failed library checks from being shown as negative results.

## Recommended improvements

### 1. Protect saved authentication tokens — High

`SerializeConfig` writes the token response as readable JSON. Its `rsaKey` parameter is not used, and the token file is named `token.xml` despite containing JSON.

**Recommended fixes**
- Protect refresh and access tokens using a Windows user-scoped protection mechanism supported by the existing plugin environment.
- Write the token file atomically so an interrupted save cannot leave a partial or corrupt token.
- Use a file name that matches the actual format, or change the format consistently.
- Remove the saved token when the user explicitly signs out or changes the Spotify app configuration.

**Done when:** persisted token contents are not readable as plain text, interrupted writes preserve the previous valid token, and the token file format is unambiguous.

### 2. Make authentication state and recovery reliable — High

The authentication flow has several broad catches that silently fall back or write only to the console. The callback handler uses a fixed loopback port, and the received OAuth state is not visibly checked before the authorization code is exchanged.

**Recommended fixes**
- Validate the OAuth state returned by the callback against the state created for the current login attempt.
- Show actionable status for callback-server startup failure, a port already in use, browser launch failure, denied consent, and token-exchange failure.
- Ensure the authentication-in-progress flag is cleared for every completion and failure path.
- Handle an expired or revoked refresh token by clearing the invalid token and prompting the user to authenticate again.
- Stop and dispose of the callback server after success, failure, or cancellation, and avoid leaving an authentication attempt pending indefinitely.

**Done when:** each login attempt either succeeds or returns the panel to a usable state with a clear next step; unrelated or stale callbacks cannot complete the active login.

### 3. Distinguish API failures from “not found” and “not saved” — High

Search/API exceptions currently set `_trackMissing`, which is also used for a genuine no-results response. Library-check helper methods return `false` when a request fails, making an unknown result appear as “not saved” or “not followed.”

**Recommended fixes**
- Represent service errors separately from a genuine no-match result.
- Preserve the last known library/follow status, or show an unknown/error state, when a check request fails.
- Handle rate limiting and transient network failures with bounded retries where appropriate, respecting Spotify’s retry guidance.
- Give the user a retry or re-authentication action when the failure is recoverable.

**Done when:** an API or network error is never displayed as a definitive “track not found” or “not saved” result.

### 4. Improve track matching and search feedback — Medium

Track lookup searches using a combined title-and-artist string and uses the first result by default. This can select the wrong version when there are remasters, live recordings, duplicate titles, or multiple artists.

**Recommended fixes**
- Compare candidate results against the MusicBee title and artist tags and prefer the closest match.
- Provide a way to review or cycle through alternatives when the best match is ambiguous.
- Clear or replace stale track details consistently when tags are missing or a search returns no results.
- Make it clear whether the plugin is still searching, has found no result, or encountered an API failure.

**Done when:** common duplicate/version cases are less likely to select the wrong Spotify track, and the panel communicates search outcomes clearly.

### 5. Reduce repeated Spotify requests — Medium

The plugin caches search responses, but a successful track search also performs separate track, album, and artist status checks. Failures in those checks are currently folded into a false result.

**Recommended fixes**
- Cache library/follow status for the current track and invalidate it after the user saves/removes a track, saves/removes an album, or follows/unfollows an artist.
- Avoid repeating the same checks when the same track is already displayed and no user action changed its status.
- Keep cache limits and eviction behavior predictable; the current concurrent dictionary removes an arbitrary key rather than the least-recently-used entry.

**Done when:** repeated notifications for the same track do not trigger unnecessary status requests, and user actions immediately update the displayed status.

### 6. Keep UI updates safe and predictable — Medium

Authentication is started from the panel paint handler, and several asynchronous event paths update controls or invoke MusicBee APIs. This makes UI lifecycle issues harder to reason about and can leave the panel stale if a control is disposed during an operation.

**Recommended fixes**
- Start authentication from an explicit user action or initialization event rather than from painting.
- Route UI changes through one UI-thread-safe refresh helper and check that the panel is still available before posting updates.
- Replace `async void` methods where possible with `Task`-returning methods; keep `async void` only where required by an event-handler signature and handle exceptions there.
- Show a busy/working state during authentication and long API operations, and prevent duplicate clicks while an action is in progress.

**Done when:** repainting the panel has no network or authentication side effects, and asynchronous operations cannot update a disposed panel.

### 7. Clean up UI and image resources — Low

Several `Font` instances are created during panel drawing and are not disposed. The cached artwork bitmap is replaced safely, but it should also be released when the plugin is closed or unloaded.

**Recommended fixes**
- Reuse fonts created for the panel lifetime and dispose of them when the panel/plugin is closed.
- Dispose of the cached artwork bitmap and other owned graphics resources during cleanup.
- Remove unused fields and imports after confirming they are no longer needed.

**Done when:** repeated panel repainting does not continually allocate new fonts, and plugin shutdown releases its owned graphics resources.

### 8. Add focused regression tests — Medium

There are no obvious automated tests covering the key behaviors identified in this report.

**Recommended tests**
- Token save/load, token protection, and recovery from a corrupt or incomplete token file.
- OAuth callback state validation and cleanup after success, failure, and cancellation.
- Search results for no match, ambiguous versions, and stale responses arriving out of order.
- Library/follow check failures remaining distinct from a legitimate negative result.
- Cache invalidation after save/remove/follow actions.

**Done when:** the core state transitions and failure cases can be verified without relying on manual Spotify account testing.

## Suggested order of work

1. Protect and safely persist tokens.
2. Harden OAuth callback validation and ensure authentication attempts always finish cleanly.
3. Separate API failures from negative search/library results.
4. Improve track matching and user feedback.
5. Reduce redundant API requests and add focused regression tests.
6. Address UI lifecycle and resource cleanup.

## Already in place

- PKCE-based authorization.
- A guard against starting multiple authentication attempts simultaneously.
- Protection against stale search results replacing newer results.
- Search-response and artwork caching.
- Diagnostic logging for several Spotify API and artwork failures.

These are useful foundations; the recommendations above target remaining behavior and reliability gaps rather than replacing those existing safeguards.
