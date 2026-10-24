# Instructor accessibility audit (WCAG 2.2 AA target)

Status: **static source review and initial remediation only**. No certification or live browser/screen-reader audit has been completed.

## Reviewed source
- src/GuideMe.Instructor.Web/wwwroot/index.html
- src/GuideMe.Instructor.Web/wwwroot/main.js
- src/GuideMe.Instructor.Web/wwwroot/style.css

## Implemented improvements
- Hebrew document language and RTL, skip-to-main-content link, search labels, status announcements.
- Keyboard-accessible guide and step selection.
- Tab semantics, aria-selected and arrow/Home/End keyboard navigation (RTL-aware arrow direction).
- Modal native dialog and focus return when original opener remains in DOM.
- Form help text linked through aria-describedby.
- Improved focus indication, contrast and responsive typography.

## Outstanding checks (must pass before claiming AA)
1. Verify browser execution without JS errors and successful guide create/edit/save.
2. Keyboard-only: Tab/Shift+Tab, guide selection, step selection, tab arrow keys, dialogs, save/cancel, Escape, focus return.
3. NVDA with Chrome/Edge: headings, labels, tab selected state, step pressed state, errors, connection status.
4. At 200% and 400% zoom and narrow widths: no content or controls clipped; no two-dimensional scrolling except genuinely tabular data.
5. Check computed contrast for text (4.5:1 normal, 3:1 large) and essential UI boundaries (3:1).
6. Verify validation messages are actionable, near invalid fields, programmatically associated and announced. Native required validation alone does not cover server errors.
7. Ensure dialogs never lose focus when opener is removed by a page re-render.
8. Verify UI element picker and Chrome extension capture can be operated without a pointer, or document and remediate gaps.
9. Review accessibility semantics in source and perform manual checks only when explicitly requested.
10. Validate accessibility under Windows high-contrast and reduced-motion preferences.

## Suggested regression
- Publish Instructor and open library.
- Tab to guide search and first guide, activate by Enter.
- Navigate to tabs, change with arrow keys; verify selected content and screen-reader announcements.
- Open edit dialog, inspect labels/help text, close via Escape and verify focus.
- Trigger invalid input and server failure, verify visible and announced error.
- Repeat at 200%/400% zoom and with NVDA.

Do not mark WCAG AA compliance until the checks above have been completed and recorded with evidence.

## Testing policy

Do not add Playwright or automated visual/browser UI testing for Instructor. Accessibility review should use source review and targeted manual verification only when requested.
