// Offline analysis of opt-in development.jsonl recordings. No game/network access.
const fs = require('node:fs');
const path = require('node:path');

const finite = value => typeof value === 'number' && Number.isFinite(value);
const distance = (a, b) => Math.hypot(a.X - b.X, a.Z - b.Z);
function pearson(pairs) {
  pairs = pairs.filter(p => p.every(finite));
  if (pairs.length < 10) return { n: pairs.length, r: null };
  const mx = pairs.reduce((s, p) => s + p[0], 0) / pairs.length;
  const my = pairs.reduce((s, p) => s + p[1], 0) / pairs.length;
  let xx = 0, yy = 0, xy = 0;
  for (const [x, y] of pairs) { xx += (x - mx) ** 2; yy += (y - my) ** 2; xy += (x - mx) * (y - my); }
  return { n: pairs.length, r: xx > 0 && yy > 0 ? xy / Math.sqrt(xx * yy) : null };
}
function percentile(values, fraction) {
  const sorted = values.filter(finite).sort((a, b) => a - b);
  return sorted.length ? sorted[Math.floor((sorted.length - 1) * fraction)] : null;
}
const metrics = ['ReplyMs', 'ReplyAgeMs', 'HelperAgeMs', 'UpdateAgeMs', 'ObservedUpdateGapMs', 'RecentMaxGapMs', 'CpuPercent'];
function analyze(records, { leader, frequency, maxSkewMs = 250, zoneGuardSeconds = 15 }) {
  if (!(leader > 0) || !(frequency > 0)) throw new Error('Explicit leader ID and QPC frequency required.');
  const polls = records.filter(r => r.Kind === 'monitor-poll' && finite(r.Qpc)).sort((a, b) => a.Qpc - b.Qpc);
  const active = c => !c.ExitedUtc && c.State !== 'Exited' && c.Vitals?.Active && c.Movement;
  const key = c => `${c.Pid}/${c.StartTicks}`;
  const excluded = {}, samples = [], transitions = new Map(), lastArea = new Map();
  const reject = reason => { excluded[reason] = (excluded[reason] || 0) + 1; };
  // Record transitions per process and launcher run; never connect separate sessions.
  for (const poll of polls) for (const c of poll.Data.Clients.filter(active)) {
    const k = `${poll.Run}/${key(c)}`, m = c.Movement;
    const old = lastArea.get(k);
    if ((old !== undefined && old !== m.Playfield) || c.Vitals.Zoning || c.Measurement?.Zoning) {
      if (!transitions.has(k)) transitions.set(k, []);
      transitions.get(k).push(poll.Qpc);
    }
    lastArea.set(k, m.Playfield);
  }
  const nearZone = (poll, c) => (transitions.get(`${poll.Run}/${key(c)}`) || [])
    .some(q => Math.abs(q - poll.Qpc) <= zoneGuardSeconds * frequency);
  const fresh = (poll, c) => c.Movement.Qpc > 0 && c.Movement.Qpc <= poll.Qpc
    && poll.Qpc - c.Movement.Qpc <= 3 * frequency
    && [c.Movement.X, c.Movement.Y, c.Movement.Z].every(finite);
  let previousLeader;
  for (const poll of polls) {
    const clients = poll.Data.Clients.filter(active);
    const driver = clients.find(c => c.Movement.CharacterId === leader);
    if (!driver || !fresh(poll, driver)) { reject('leaderMissingOrStale'); previousLeader = null; continue; }
    const lm = driver.Movement, prior = previousLeader;
    previousLeader = { run: poll.Run, key: key(driver), qpc: poll.Qpc, movement: lm };
    if (!driver.Measurement?.Foreground || !lm.Enabled || !lm.SendMovement) { reject('leaderNotDriving'); continue; }
    if (!prior || prior.run !== poll.Run || prior.key !== key(driver) || prior.movement.Playfield !== lm.Playfield
        || poll.Qpc <= prior.qpc || poll.Qpc - prior.qpc > 2.5 * frequency || distance(lm, prior.movement) < 0.2) {
      reject('leaderStationaryOrDiscontinuous'); continue;
    }
    if (nearZone(poll, driver)) { reject('leaderZoneTransition'); continue; }
    for (const c of clients) {
      const m = c.Movement, measurement = c.Measurement;
      if (m.CharacterId === leader) continue;
      if (!measurement || !fresh(poll, c)) { reject('followerMissingOrStale'); continue; }
      if (m.Adapter !== 'AOSP' || lm.Adapter !== 'AOSP' || !m.Enabled || m.Channel < 0 || m.Channel !== lm.Channel
          || m.Playfield !== lm.Playfield || measurement.Foreground) { reject('incompatibleState'); continue; }
      if (nearZone(poll, c)) { reject('followerZoneTransition'); continue; }
      const skew = Math.abs(m.Qpc - lm.Qpc) * 1000 / frequency;
      if (skew > maxSkewMs) { reject('positionSampleSkew'); continue; }
      if (!m.VisiblePlayers?.includes(leader)) { reject('leaderNotVisible'); continue; }
      const gap = distance(m, lm), limit = 2 * (1 + Math.max(0, m.RunSpeed) / 100);
      const sample = { utc: poll.Utc, run: poll.Run, qpc: poll.Qpc, session: key(c), character: m.CharacterId,
        area: m.Playfield, distance: gap, limit, rangeRatio: gap / limit, beyondRange: gap > limit,
        minimized: measurement.Minimized, sampleSkewMs: skew };
      for (const metric of metrics) sample[metric] = finite(measurement[metric]) ? measurement[metric] : null;
      // An old successful reply must not masquerade as current healthy IPC.
      if (!finite(sample.ReplyAgeMs) || sample.ReplyAgeMs > 2500 || !finite(sample.HelperAgeMs) || sample.HelperAgeMs > 2500)
        sample.ReplyMs = null;
      samples.push(sample);
    }
  }
  const groups = new Map();
  for (const sample of samples) {
    const k = `${sample.character}/${sample.minimized}`;
    if (!groups.has(k)) groups.set(k, []);
    groups.get(k).push(sample);
  }
  const summaries = [...groups.values()].map(group => {
    const correlations = {};
    for (const metric of metrics) {
      correlations[metric] = {};
      for (const lag of [0, 1, 2, 3]) {
        const pairs = [];
        for (let i = 0; i < group.length; i++) {
          const current = group[i];
          let candidate = lag === 0 ? current : null, best = 0.45;
          for (let j = i - 1; lag > 0 && j >= 0; j--) {
            const previous = group[j], delta = (current.qpc - previous.qpc) / frequency;
            if (delta > lag + 0.45) break;
            if (current.run !== previous.run || current.session !== previous.session || current.area !== previous.area) continue;
            const error = Math.abs(delta - lag);
            if (delta > 0 && error < best) { candidate = previous; best = error; }
          }
          if (candidate) pairs.push([candidate[metric], current.rangeRatio]);
        }
        correlations[metric][`lead${lag}s`] = pearson(pairs);
      }
    }
    return { character: group[0].character, minimized: group[0].minimized, samples: group.length,
      beyondRangeSamples: group.filter(s => s.beyondRange).length,
      distanceP95: percentile(group.map(s => s.distance), .95), distanceMax: Math.max(...group.map(s => s.distance)),
      localReplyP95: percentile(group.map(s => s.ReplyMs), .95), correlations };
  });
  return { method: { leader, frequency, maxSkewMs, zoneGuardSeconds, movingLeaderOnly: true,
    target: 'Observed horizontal separation / follower movement-sync cutoff',
    timing: 'lead1s means the timing measurement approximately one second BEFORE the separation sample',
    caveats: ['Position samples are asynchronous; these are approximate separations, not confirmed rejected packets.',
      'Pearson r is descriptive, not causation or statistical significance; successive samples are dependent.',
      'RecentMaxGapMs repeats the maximum over a rolling ten-second window.',
      'ReplyMs is the last completed IPC probe; ReplyAgeMs records its age. Lag calculations use observation times, not exact probe-send times.',
      'Only comparable visible, same-channel, same-area followers are included. Zoning failures and distant/invisible followers are excluded, not disproved.',
      'Use recordings from one known server/team: older records do not identify the server endpoint.'],
    serverReply: 'Not recorded. Local ReplyMs is IPC, not a game-server reply. Sidebar TCP connection checks cannot substitute for game movement acknowledgements.' },
    polls: polls.length, excluded, summaries, samples };
}

if (require.main === module) {
  const args = process.argv.slice(2), inputs = [];
  let leader, frequency, output;
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--leader') leader = Number(args[++i]);
    else if (args[i] === '--frequency') frequency = Number(args[++i]);
    else if (args[i] === '--output') output = args[++i];
    else inputs.push(args[i]);
  }
  if (!inputs.length || !output) throw new Error('Usage: node tools/analyze-movement.cjs --leader ID --frequency QPC_HZ --output DIRECTORY input.jsonl [...]');
  let malformed = 0;
  const records = inputs.flatMap(file => fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(Boolean).flatMap(line => {
    try { return [JSON.parse(line.replace(/^\uFEFF/, ''))]; } catch { malformed++; return []; }
  }));
  const report = analyze(records, { leader, frequency });
  const samples = report.samples; delete report.samples; report.malformedLines = malformed;
  fs.mkdirSync(output, { recursive: true });
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify(report, null, 2));
  const columns = ['utc', 'character', 'minimized', 'distance', 'limit', 'rangeRatio', 'sampleSkewMs', ...metrics];
  fs.writeFileSync(path.join(output, 'samples.csv'), [columns.join(','), ...samples.map(s => columns.map(c => JSON.stringify(s[c] ?? '')).join(','))].join('\n'));
  console.log(JSON.stringify({ polls: report.polls, samples: samples.length, malformed, excluded: report.excluded,
    summaries: report.summaries.map(s => ({ ...s, correlations: { ReplyMs: s.correlations.ReplyMs, UpdateAgeMs: s.correlations.UpdateAgeMs } })) }, null, 2));
}
module.exports = { analyze, pearson };
