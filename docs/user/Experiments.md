# Experimental features in 1.3.0

Open **Settings → Advanced** to enable AO-VK management/tuning, automatic team formation and leader recovery, or nearby combat reports (All/NPC). These new switches start off, including after upgrading an older profile.

The team automation experiment also enables movement controls beside **Team** in the Control Panel. Choose a leader and **Follow**, **Circle** or **Funny business**, then press **Start**. Follow uses spacing between characters; Circle uses a radius around the leader (3 m by default), with the followers evenly distributed around the full circle. The circle moves with the leader but does not rotate when the leader turns. Each mode remembers its distance. **Gather** brings followers close instead. **Stop** releases formation control and restores each client's previous movement-sync setting. Attack sync stays enabled. All teammates must be running locally with updated Nexus Shortcuts, in the same area and on the same SyncManager channel.

**Funny business** gives each follower independent random movement and emote timers inside the selected radius. New destinations are picked every 1–4 seconds and emotes every 0.8–4 seconds; emotes do not wait for arrival or for the previous animation to finish. Each follower shuffles the client's available emotes independently. The leader stays under your control. Formation followers match the leader's walking or running pace. Combat and casting pause formation movement and emotes.

Choosing another **Leader** also transfers actual in-game leadership. The current leader must be running locally and the team roster must agree. Active formation pauses during transfer and resumes after the new leader is confirmed. Failed transfers leave formation stopped. Funny business now tries a clear direction away from an obstacle; if none is available, that follower waits and retries. Zoning, lost contact and excessive separation still stop formation.

Hover over the formation controls to see their effects and trade-offs. The card's **Aggro** bar counts nearby living mobs observed attacking that character, excluding players and identified pets. It shows up to three names alphabetically and the total count, updated twice a second. This is current combat-target observation, not the server's threat table. Old monitors, zoning and stale observations show unavailable rather than zero. Relog clients to load this monitor update.

Follow uses the leader's recorded route and ordinary movement. It stops at blocked routes, excessive distance, stale data or loss of Nexus contact; it does not teleport characters or find routes around obstacles. Zoning stops formation. Wait for everyone to finish zoning, then press Start again. Closing the Control Panel stops formation too. Leader and spacing choices are saved; movement never starts automatically when the panel opens.

The **Sync** selector beside these controls offers **events only**, **5 Hz trial**, and **10 Hz trial**. Start with events only for a baseline, then compare the same route at 5 Hz. This experiment supplements ordinary SyncManager movement between updated Nexus clients on this PC; it does not change the plugin's LAN broadcast rate. It pauses during Follow/Gather, combat, casting, zoning or other navigation. Keep the Control Panel open during the trial.

Extra corrections use fresh position and rotation snapshots within the existing SyncManager distance limit. A stopped leader's state is repeated for two seconds, allowing the current follower to stop outside that limit without changing its coordinates. Refresh never restarts a stopped follower. It cannot repair a stalled game loop or route around obstacles. The separation guard remains enabled. Record diagnostics to compare native event gaps, refresh counts, snapshot age, separation and local update delays; 10 Hz is not assumed to be better.

Your accounts, plugin sets and saved team order remain intact. To resume automatic invitations for an existing preset, enable automatic teams here; its saved formation choice is retained. Team combat statistics and ordered login work without these experiments.

Automatic minimization is also on this page. Its existing saved choice is kept. NPC death observation is an experimental checkbox in each timer's editor; manual timers remain available.

Disabling AO-VK here stops Nexus graphics management and AO-VK startup tuning. It **does not remove an installed renderer** or change a running client's graphics. To remove AO-VK, temporarily enable its management, close the game clients and disable **Use AO-VK** on the Graphics page, then save.

Disabling nearby reports hides All/NPC and excludes them from new script exports. It does not delete scripts already exported. Disabling automatic teams cancels Nexus team plans; it does not change settings in independent AO# plugins.

The regular Control Panel, health/nano readings, team statistics, manual logout/relog, manual timers, scripts, layouts, backups and diagnostics remain available. Some require your own compatible AO# installation and the appropriate Nexus helper plugin.

## Automatic regroup after sync loss

With the team-automation experiment enabled, **Control Panel → Team → Auto regroup** allows a separated follower to try returning to the actual team leader after the sync guard stops it. The checkbox is remembered; it defaults off. Keep the Control Panel open. All participating clients need updated Health Monitor and Shortcuts helpers, fresh matching team membership, the same zone and sync channel, and a locally running leader.

Recovery temporarily suspends only that follower's movement sync. It uses ordinary movement on a clear nearby route, up to 60 metres, and restores sync when close enough. It does not teleport or navigate around obstacles. A blocked route or four seconds without closing the distance makes the follower stay stopped and attempt one Team message: **Stuck while regrouping**, with a clickable location. Automatic location messages have a 30-second cooldown; they require a confirmed Team chat channel. Recovery times out after 20 seconds even if progress continues.

Selecting the follower's game window, combat, casting, zoning, stale state or a closed leader cancels recovery. **Stop**, disabling Auto regroup, closing the Control Panel or disabling the experiment releases recovery within three seconds of the last permission refresh. Use Stop before starting another formation. If a blocked follower is brought back within range, sync resumes. Attack sync is unchanged.
