# AO Nexus v1.2.1

- **Find your characters quickly.** Search by name, character ID, account or server without losing your current selection. The launcher keeps the total selected count visible, including selected characters hidden by your search. The search field and Clear button now align, with padding and centred text.
- **Choose which confirmations you see.** Login and team logout each offer **Don't show this confirmation again**. Re-enable either independently in Settings. Cancelling a confirmation does not save the checkbox; password prompts, account limits and client identity checks still apply.
- **Understand client monitoring at a glance.** **Character status and help** shows each character's status, a short explanation and what to do next. Measurements remain available under **Show technical details**. Missing measurements are kept separate from a client that is not responding, and an exited client is not automatically labelled a crash.
- **Notice casts attempted while falling.** The updated monitoring plugin reports this recent action issue separately from responsiveness. Repeated reports are grouped, with an explanation and next step. An observed attempt is distinguished from a confirmed game rejection. Alerts expire after 30 seconds without a new report; expiry does not prove success. Other failure reasons are not yet covered.
- **Cleaner layout and updated help.** The tagline sits below AO Nexus. Monitoring uses dark scrollbars and spaced help buttons. The README overview and player guide now include fresh screenshots of search, confirmations and character status.

Monitoring still requires the optional Nexus Health Monitor plugin in the relevant AOSharp profiles. Reload the updated plugin through AOSharp when safe to enable action reports. No chat text is retained and nothing is uploaded automatically.

The falling-cast feature has passed synthetic end-to-end checks; exact live error-message coverage still needs verification. Check the result in game after retrying a nano.

Download **AO-Nexus-Luckyspawn.zip** from the release assets. Existing profiles and saved password preferences are preserved. The automatically generated source ZIP is for developers.
