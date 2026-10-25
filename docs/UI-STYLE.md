# AO Nexus layout conventions

- Use the existing dark theme and `NexusPanel` border for distinct sections.
- Put a section heading on the left and its section-wide actions on the right of the same header row. Align their top edges and vertically center the heading text beside the buttons. Avoid a second row of buttons and the blank area it creates.
- Keep actions for a particular field beside that field: for example, preset Save/Update/Rename/Delete stay next to the preset picker.
- Inline dropdowns and buttons have equal visible heights and aligned top edges. Match the dropdown to the established button height, including DPI scaling; do not shrink the buttons. Vertically center the text in each control.
- Use 12 px inset padding and consistent spacing between sections. Keep headings, controls and content aligned to the same inset edges.
- Allow more rows only where content or the supported window size requires them. Check English and Danish text for overlap and clipping.
- Keep preset selection, its save status and both shortcut fields together in YOUR SQUADS. Account actions remain beside the account list.

Reference: [Microsoft Windows layout guidelines](https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-layout), especially top alignment, text baselines and consistent control sizing. Nexus keeps its existing dark palette and button dimensions.
