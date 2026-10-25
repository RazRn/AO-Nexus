# AO Nexus layout conventions

- Use the existing dark theme and `NexusPanel` border for distinct sections.
- Put a section heading on the left and its section-wide actions on the right of the same header row. Align their top edges and vertically center the heading text beside the buttons. Avoid a second row of buttons and the blank area it creates.
- Keep actions for a particular field beside that field: for example, preset Save new/Update/Edit/Delete stay next to the preset picker.
- Inline dropdowns and buttons have equal visible heights and aligned top edges. Match the dropdown to the established button height, including DPI scaling; do not shrink the buttons. Vertically center the text in each control.
- Use 12 px inset padding and consistent spacing between sections. Keep headings, controls and content aligned to the same inset edges.
- Allow more rows only where content or the supported window size requires them. Check English and Danish text for overlap and clipping.
- Keep preset selection and Update together in Team presets, without an explanatory status row. Enable Update only when the checked characters differ from the saved selection. Put names and editable shortcuts in the create/edit dialog, and apply them only on Save. Place account actions inside ACCOUNTS BY SERVER, above its list. Wrap the toolbar when needed rather than clipping actions or shrinking their button height.
- Do not add hover tooltips. Put necessary information in Details, dialog labels or visible help text; suppress native tree and grid hints as well.
- Use text links for lightweight view actions such as Expand all and Collapse all, aligned beside the section heading. Preserve keyboard focus, hover feedback and disabled states.

Reference: [Microsoft Windows layout guidelines](https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-layout), especially top alignment, text baselines and consistent control sizing. Nexus keeps its existing dark palette and button dimensions.

- Use a dark terminal panel with a readable monospace font for logs. Keep search and detailed reading in a separate window.
- News dates share the source-name row, right aligned. Measure wrapped headline height and leave a small consistent gap before the summary.
- Group settings by purpose in dark tabs (General, Accounts, Sidebar, Updates); keep Save/Cancel outside the tab content. Move long privacy documentation into its own readable dialog. Preserve drafts until Save.
- Optional news and health share sidebar tabs. Remove source selection from the dashboard when it is available in Settings. Health cards must distinguish a local connection check from a public report, include a text state as well as color, and label missing/stale data explicitly.
- Game GUI uses three tabs: Layouts, Skins and Backups. Keep character-level choices separate from installation-level skin changes. Show independent component checkboxes, review the rollout scope and retain an independent target selection without changing the dashboard's team. Keep Cancel/Close outside the working tab and show operation feedback in the dialog.
