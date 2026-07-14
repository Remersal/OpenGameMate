# OpenGameMate embedded-shell design QA

- Source visual truth: `user-provided UI reference image (not stored in the repository)`
- Implementation evidence: Computer Use window capture `screenshot-0`, captured from the Release build at 1468×894. The screenshot is intentionally not persisted because it contains the user's signed-in ChatGPT surface.
- Source viewport: 1925×1246.
- Implementation viewport: 1468×894.
- State: OpenGameMate main window with the embedded ChatGPT page loaded and signed in.

## Full-view comparison evidence

The implementation follows the selected reference's main composition: a warm off-white application shell, a narrow left control/navigation region, a dominant white content surface on the right, a quiet one-pixel divider, compact top status chrome, restrained radii, and low-saturation borders. OpenGameMate-specific controls replace the reference navigation while preserving the hierarchy and density appropriate to the product.

The reference and implementation use different available desktop sizes, so comparison was normalized by region proportion rather than raw pixels. The OpenGameMate sidebar is intentionally slightly wider because it contains operational controls and diagnostic copy rather than short navigation labels.

No separate focused crop was required: the full-view captures render the sidebar typography, header, status chips, control cards, WebView2 boundary, and loaded ChatGPT surface legibly. The ChatGPT page's own internal layout is third-party content and was checked only for correct containment, not visual restyling.

## Required fidelity surfaces

- Fonts and typography: Segoe UI with Windows Chinese fallback matches the reference's neutral system typography. Hierarchy is clear at 26px product title, 16px page title, 13px controls, and 11–12px supporting copy. Remaining density is intentional for the operational sidebar.
- Spacing and layout rhythm: 370px sidebar, 12px shell gutter, 14px content radius, compact 10–16px card rhythm, and a 58px WebView header produce the same narrow-sidebar/dominant-canvas balance as the source while keeping all controls scrollable.
- Colors and visual tokens: warm shell `#F2EEEB`, sidebar `#F7F4F2`, white surfaces, low-contrast taupe borders, charcoal primary actions, and restrained semantic status colors align with the selected reference.
- Image quality and asset fidelity: the source contains no product illustration or raster asset that belongs in OpenGameMate. No placeholder art, generated image, inline SVG, or decorative imitation was introduced. The real WebView2 surface is rendered directly.
- Copy and content: labels describe the real OpenGameMate actions, explicitly say that ChatGPT is embedded, preserve privacy wording, and avoid implying that the app reads webpage or Voice content.

## Interaction verification

- Opening ChatGPT initialized WebView2 inside the right-hand host.
- Only one OpenGameMate top-level window existed after open.
- Closing the embedded page returned to the in-shell placeholder without closing OpenGameMate.
- Reopening created a fresh embedded WebView2 and again retained one top-level window.
- The signed-in ChatGPT surface remained contained inside the application shell.

## Comparison history

### Iteration 1

- Finding [P2]: the initial bright-blue primary action drifted from the reference's quiet black/neutral action treatment.
- Fix: changed the primary action token to charcoal `#2C2A29` with a muted hover state.
- Post-fix evidence: the final Release capture shows charcoal actions, warm-neutral surfaces, and no competing saturated accent.

### Iteration 2

- No actionable P0, P1, or P2 visual differences remain for the requested reference-based direction.

## Follow-up polish

- [P3] A future pass could add a user-controlled sidebar width preference if real RC use shows that compact displays need more ChatGPT space. This is intentionally not added in the current no-new-feature scope.

final result: passed
