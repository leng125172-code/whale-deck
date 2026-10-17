# Directory move UI verification inventory

Local production-preview candidate verified; not published and no installed
browser claim yet. Uses the
playwright-interactive skill for functional and separate visual checks.

| Claim/control | Functional checks | Visual states/evidence |
| --- | --- | --- |
| Independent move permission and directory action | move is not implied by copy; protected/file rows excluded; ordinary initial/reload zero requests | directory action at 320/1045/1440 light/dark |
| Plan form and explicit confirmation | target validation, scan never writes, exact original confirmation and two jobs | form/Ready/Moving/Completed, long Chinese paths and UUIDs |
| Historical publication/removal/retention facts | unknown remains unknown; contradictory facts rejected; private originals not HDD backup | Ready null facts, Completed known facts, Unknown partial state |
| Original request recovery | distinct move route key/ID, refresh/lost replies only GET; new login never auto-registers | recovered Ready/404/response lost |
| Fold/reopen/close reference | fold keeps polling; closing never cancels/replays, reversible controls exercised | status bar, warning and reachable footer |
| Cancel and acknowledge unknown | explicit second confirmation, no late revival/replay; original owner/version | cancellation, Unknown and acknowledged |
| Permission/expiry/errors | 403 clears UI, expired disables confirmation; resume query after transient failure | denied/expired/connection error |

Minimum checks include 320px and explicit desktop, all relevant dialog regions
and footer bounds, no page horizontal overflow, and manual screenshot review
independent of functional tests. Exploratory scenarios: both copy and move
references in a URL; close/reopen during a lost confirmation, change theme and
refresh. Local protocol fixtures do not prove SQL/Agent/production identity.

## Local candidate verification

- 229 frontend unit tests across 31 files, lint, type checking, formatting and
  production build passed. Final joint copy/move Playwright run: **47 passed
  (5.9 minutes)**, 18 copy plus 29 move. Earlier 41 joint and 25 move runs passed
  before final root-name/wording and long-content coverage changes; the final
  run supersedes them for current source.
- New coverage: 320px light/dark initial form, empty/traversal path rejection,
  no eligible target-root explanation without writes, long legal target body
  scrolling while footer remains reachable. Existing original-key/response-loss,
  ordinary zero requests/403, separate grants, cancellation/expiry, Unknown
  acknowledgment and contradictory receipts remain covered.
- Separate normal-input visual review of desktop Ready/Completed, mobile
  light/dark Ready/Completed, protected rejection, Unknown and initial form.
  Long paths/UUIDs wrap, registered roots show names with IDs as title/fallback,
  no horizontal page overflow, action rows do not overlap. Exploratory theme
  round trip and fold/reopen/query do not submit another confirmation; competing
  copy/move references do not stack dialogs. Natural plan expiry correctly
  disabled confirmation; the expired fixture was replaced, not extended.
- Initial dense Unknown at 320x800 measured height 810.578px and was a visual
  failure despite reachable buttons. Fixed maximum dialog height with a
  scrollable body and nonshrinking header/footer, and removed duplicate Unknown
  prose. Rechecked dense Unknown inside the viewport. Final 12-component legal
  target: dialog y=12/h=776/bottom=788; body client=616/scroll=805/overflow=auto;
  both top and bottom independently inspected, footer fixed, page width=320.
  Normal mouse wheel reached the final historical-fact text; one registration,
  zero execution requests during that review.
- Screenshots under ignored `deploy/artifacts`: `move-ui-desktop-ready.jpg`,
  `move-ui-desktop-complete.jpg`, mobile dark/light completed captures,
  `move-ui-mobile-dark-unknown-final.jpg`, `move-ui-mobile-light-form.jpg`,
  `move-ui-mobile-light-protected.jpg`, and `move-ui-mobile-long-ready-{top,bottom}.jpg`.
  Earlier screenshots are state-specific history; final long-content and Unknown
  captures reflect the latest build. The first form capture caught a transition;
  replaced only after its real visible input appeared, no forced interaction.
- Browser-tool setup initially lacked its default headless executable (zero
  tests executed); used the already-installed Chrome explicitly. One manual
  kernel timeout and an expired-plan click were tool/fixture failures, not
  passing interactions. Existing owned preview and browser remain alive for
  the upcoming installed acceptance, with protocol-fixture contexts kept
  separate from production contexts.
- GLM/glm-5.3-flash read-only UI review: one successful call, no retries,
  169740ms, 238953 input / 204608 cached / 4428 output tokens, price unavailable.
  GPT independently checked findings, implemented wording/root-name/empty-root
  fixes and added regression checks. No credentials or deployment access were
  delegated; permissions and outcome semantics remain GPT-reviewed.

## Required before production UI acceptance

Frozen-source four-job CI and real PostgreSQL (including newly added false-
publication Unknown case), actual publication, matching HTTP/Gateway assets,
real administrator desktop/mobile moves and independent byte/inode/Root receipt
observer, ordinary real login/CSRF denial, and exact fixture/account cleanup.
The new `tree_move_browser_fixture.py` is a prepared exact-scope observer, not
evidence that it has executed. Root private originals are not HDD backups.
Current-admin disposition after loss of the original login remains P0-03 work;
the validation janitor does not implement it. Full 76-item scope stays active.
