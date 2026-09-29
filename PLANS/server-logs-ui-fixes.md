# Server logs page — design review fixes

Review date: 2026-09-29. Screenshots taken headless at desktop 1440 px (English),
mobile 390 px (Russian) and tablet 820 px (English), logged in as a moderator.

Status (2026-09-29): fixed and deployed (gateway only). Notes:
- 6 was caused by an unlayered `button, input… { font: inherit }` rule overriding Tailwind sizes;
  it now lives in `@layer base`. This affects buttons on every page, not only this one.
- 7: the icon already has a tooltip and aria-label; left as is.
- 10: counts are right-aligned inside each row; the offset is the space for the exclude (−) button. Left as is.
- 19: badges that equal the previous start are grey; a change shows (+N / −N).
- 20: the index really only has main-log startups from 2026-09-22 (the game had ~5 days of
  files at first deploy). The tab now says only indexed startups are listed.

Status legend: [ ] open · [x] done · [-] decided not to change

## Critical
- [x] 1. Problems tab renders every signature at once (~26 000 px page). Add paging / "show more".
- [-] 2. Raw log lines show player IPs to moderators, unlike the Overview. Owner decision
       (2026-09-29): server logs hold nothing sensitive, so this stays.
- [x] 3. Mobile: facet panel sits above results (~1 500 px down). Collapse facets on mobile.

## Search tab
- [x] 4. Rows show only a time; multi-day ranges need dates (day separators).
- [x] 5. Histogram labels identical for 24 h (`7:54 AM … 7:54 AM`); no hover values.
- [x] 6. Mixed text sizes in the range / Live / noise / TXT / CSV row.
- [-] 7. Row "context" icon has no label or tooltip.
- [x] 8. "Try:" example chips disappear once something is typed.
- [x] 9. Level facet mixes "Audit" (a log) with levels; raw game level names untranslated.
- [-] 10. Facet counts not aligned with headers.
- [x] 11. Empty result: blank histogram and no next step (widen range / clear filters).

## Problems tab
- [x] 12. Time range and severity groups look like one row; "All" twice.
- [x] 13. "0 new problems since last start" line is low-contrast and not status-coloured.
- [x] 14. Sparklines have no tooltip.

## Players tab
- [x] 15. "Who was here?" form is cramped, bare X/Y/Z labels, button looks like text.
- [x] 16. "Activity per day" chart: no dates, legend mentions yellow bars that may be absent.
- [x] 17. Player list scrolls in its own box and cuts the last row mid-name.
- [x] 18. Detail pane mixes three type styles; Commands block scrolls internally.

## Startups tab
- [x] 19. Identical "35 errors / 159 warnings while starting" on every row; show change vs previous start.
- [x] 20. "30 days" selected but only 7 rows; no note that older startups aged out of the index.

## Shell (mobile)
- [x] 21. Server name overlaps the language selector; nav and tab bars clip items with no scroll hint;
       search placeholder truncated.
