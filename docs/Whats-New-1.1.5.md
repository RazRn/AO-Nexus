# AO Nexus v1.1.5 — Compact sections and clearer activity

- Team presets, accounts and details use border legends. Account buttons and expand/collapse links share a wrapping toolbar.
- Larger preset, account and GUI-capture forms. Short dialogs fit their contents within the available screen height; stale scrollbars are cleared. Long documents and lists retain scrolling.
- The thin header line shows login and logout progress. Confirmed character logins and actual client exits advance it; a moving segment indicates waiting. GUI operations show activity too.
- Preset logout still uses normal `/quit`. Nexus watches confirmed processes for up to 90 seconds, then reports any still open. It never forces a client closed.
- Click the terminal log preview to open its searchable window, or focus it and press Enter/Space. The separate Open log link is removed.
- Hand cursors identify clickable controls; editable text fields retain their text cursor.
- Choosing **No preset selected** clears current character checkboxes without rewriting saved presets. Startup without a default still preserves manual selections.

Validation uses synthetic accounts, offline news, English/Danish dialogs, normal and simulated 150% text scaling, and native input/rendering checks. No real game accounts were logged in or out during these tests. Guide screenshots were regenerated from this build.

Experimental same-account parallel login remains excluded.
