# Server news

AO Nexus reads the publishers' public pages and displays plain-text headlines, excerpts, publication dates and links. It does not run their scripts, download images, sign in, or request account information. News does not claim to show game-server availability.

| Publisher | Endpoint | Latest article verified on 24 October 2026 |
| --- | --- | --- |
| Funcom AO | [Official news page](https://www.anarchy-online.com/news) | [22nd Anniversary Celebration](https://www.anarchy-online.com/news/22nd-anniversary-celebration), 25 June 2023 |
| Project Rubi-Ka | [Official Atom feed](https://project-rk.com/feed.xml) | [May Newsletter](https://project-rk.com/posts/0023-may-newsletter/), 29 May 2026 |
| AO Rebirth | [Official homepage news](https://www.ao-rebirth.com/index.php) | [Update for nanos & weapons !](https://www.ao-rebirth.com/news.php?id=62), 23 October 2026 |

All three article links returned HTTP 200 during verification. These are the latest entries available from those sources, not a claim that each publisher updates frequently. The Funcom feed serves both official dimensions. Local and custom servers have no inferred public feed.

The news source picker supports any combination of publishers. Choices save automatically in the AO Nexus profile for the next launch, independently of selected accounts and squads. New or older profiles without a saved choice show all public sources. An explicitly empty selection stays empty. Changing language or refreshing articles preserves the selection.

The Project Rubi-Ka Atom feed still embeds its former `project-rk.me` origin. The reader changes that exact historical origin to `https://project-rk.com`, preserving the article path. The resulting newest-article link was verified against the current site.

News refreshes on opening the app, through Refresh, and every 15 minutes while the panel is open. The three publishers are fetched concurrently. Each request has a 12-second deadline and a 1 MiB response cap. Only HTTPS URLs on the corresponding publisher's allowlisted domain can be opened. Redirects are bounded and remain within that allowlist. XML DTDs and external entities are disabled.

The most recent successful result for each publisher is cached separately under `%LOCALAPPDATA%\AONexus\News`. A failed request or changed source format leaves that publisher's last good cache visible and marks it as cached. Publication dates stay unchanged; the footer separately shows fetch time. The launcher continues to work when news is unavailable. Explicit UI-preview mode uses dated, verified headline examples without network requests or cache writes.

Run `tests\Verify-News.ps1` for deterministic parser, URL validation, size limit, cache, offline, format-change and cancellation checks. Add `-Live` to exercise all three current endpoints and their cache writes inside the project's test logs directory. UI-preview verification is separate. Full website downloads used during research are not included in the project or share package.
