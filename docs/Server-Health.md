# Server health

Enable **Settings → Sidebar → Show Server health**, choose the servers to offer in its picker, and Save. Server news has an independent toggle and source selection. With both panels enabled they share tabs; with both disabled accounts reclaim the sidebar width.

Only the currently displayed health server is checked. Every batch has four rows: login connection, world/zone connection, chat connection and official service report. Checks run every 60 seconds while visible, pause in the tray/on the news tab, and cancel when hidden. Refresh and server switching have a global 15-second throttle; batches never overlap. At most four operations run together, with a 3-second TCP timeout and 5-second HTTP timeout. JSON responses are limited to 64 KiB; redirects are not followed.

**Reachable** means a TCP connection succeeded from this PC, not that authentication or zoning will succeed. **Unreachable** can also mean local routing, DNS, firewall or internet trouble. **Reported online/issue** comes from the server's public API. **Unknown** is explicit when there is no verified check, the report is missing/stale, or it cannot be read. Old samples become outdated rather than continuing to claim current health. History bars are up to 24 observed batches in this session, not a continuous uptime record or historical incident feed.

## Verified endpoints used in v1.0.20

| Server | Login TCP | World/zone TCP | Chat TCP | Public report |
| --- | --- | --- | --- | --- |
| AO Rebirth Live | `2.24.96.30:7500` | `2.24.96.30:7501` | `2.24.96.30:7012` | [AO Rebirth status](https://www.ao-rebirth.com/server-status.php): fresh login/zone/chat/account-broker flags. |
| Project Rubi-Ka Ithaca | `5.161.243.214:7000` | Unknown | Unknown | [PRK status](https://project-rk.com/api/status): matching IP and port in the public server list. No per-service status or source timestamp is available. Zero players is not an outage. |
| Funcom Rubi-Ka | `37.18.192.41:7505` | Unknown | `chat.d1.funcom.com:7105` | Unknown |
| Funcom Rubi-Ka 2019 | `37.18.192.41:7506` | Unknown | `chat.d1.funcom.com:7106` | Unknown |
| Custom/local | Its configured address and port | Unknown | Unknown | Unknown |

Known-service association uses the actual login address and port, not an editable display name or profile ID. A changed endpoint therefore gets a conservative custom-server view. Service endpoints may change; this is a versioned catalog, not discovery by scanning.

AO Rebirth reports must say `ok`, not be stale, include all four flags, have an age of no more than 180 seconds and a matching recent generation timestamp (30 seconds future clock tolerance). Explicit false flags are reported issues. Incomplete or malformed data is Unknown. PRK's public list is interpreted only as a reported server listing, not proof that every service works.

## Research sources and scope

- [AO Rebirth's official dimension list](https://ao-rebirth.com/new-dimensions/dimensions_v3.txt) and [public status API](https://www.ao-rebirth.com/server-status.php). Zone/chat listeners match the established AO Rebirth deployment and were checked with connection-only probes.
- [Project Rubi-Ka website](https://project-rk.com/) and its [site JavaScript](https://project-rk.com/scripts/site.js), which uses [the public status endpoint](https://project-rk.com/api/status). No private administrative APIs are used.
- [Funcom's dimension list](https://dimensions.anarchy-online.com/live-dimensions/dimensions.txt) and [Nadybot's AOChatProxy documentation](https://github.com/Nadybot/Nadybot/wiki/AOChatProxy) for the chat listeners. A static launcher page saying Online is deliberately not treated as a fresh service report.

The app performs no authentication probes, zone enumeration, scanning, ICMP polling, Discord notifications or access to private metrics. DNS/TLS/website checks are not duplicated as separate rows: failures are surfaced by the relevant connection/report. This keeps the panel focused and light. It can help investigate a disconnect but cannot prove why a particular character was kicked.
