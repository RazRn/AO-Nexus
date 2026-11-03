const test = require('node:test');
const assert = require('node:assert/strict');
const { analyze, pearson } = require('../tools/analyze-movement.cjs');
const options = { leader: 1, frequency: 1000, zoneGuardSeconds: 2 };
function recordings() {
  return Array.from({ length: 24 }, (_, i) => {
    const qpc = (i + 1) * 1000;
    return { Kind: 'monitor-poll', Run: 'fixture', Utc: new Date(qpc).toISOString(), Qpc: qpc,
      Data: { Clients: [1, 2].map(id => ({ Pid: id, StartTicks: id, State: 'Healthy',
        Vitals: { Active: true, Zoning: false },
        Measurement: { Foreground: id === 1, Minimized: id === 2, ReplyMs: i + 1, ReplyAgeMs: 1000, HelperAgeMs: 10, UpdateAgeMs: 5 },
        Movement: { CharacterId: id, Qpc: qpc - 20, X: i * 3 + (id === 2 ? (i + 1) / 10 : 0), Y: 0, Z: 0,
          Adapter: 'AOSP', Enabled: true, SendMovement: true, Channel: 4, Playfield: 5, RunSpeed: 100, VisiblePlayers: [1, 2] } })) } };
  });
}
test('correlates paired samples and preceding observations', () => {
  const r = analyze(recordings(), options);
  assert.equal(r.samples.length, 23);
  assert.ok(r.summaries[0].correlations.ReplyMs.lead0s.r > .9999);
  assert.equal(r.summaries[0].correlations.ReplyMs.lead1s.n, 22);
});
test('missing and constant metrics cannot produce a correlation', () => {
  assert.equal(pearson(Array(20).fill([1, 3])).r, null);
  assert.equal(pearson(Array(20).fill([null, 3])).n, 0);
  const records = recordings();
  for (const r of records) r.Data.Clients[1].Measurement.ReplyAgeMs = 9000;
  assert.equal(analyze(records, options).summaries[0].correlations.ReplyMs.lead0s.r, null);
});
test('rejects stale and badly skewed positions', () => {
  const records = recordings();
  for (const r of records) r.Data.Clients[1].Movement.Qpc -= 300;
  const r = analyze(records, options);
  assert.equal(r.samples.length, 0);
  assert.equal(r.excluded.positionSampleSkew, 23);
});
test('excludes area transition neighbourhoods and invisible leaders', () => {
  const records = recordings();
  for (const r of records.slice(10)) for (const c of r.Data.Clients) c.Movement.Playfield = 6;
  const result = analyze(records, options);
  assert.ok(!result.samples.some(s => s.qpc >= 9000 && s.qpc <= 13000));
  for (const r of records) r.Data.Clients[1].Movement.VisiblePlayers = [];
  assert.equal(analyze(records, options).samples.length, 0);
});
test('lag pairing cannot bridge process restarts', () => {
  const records = recordings();
  for (const r of records.slice(12)) r.Data.Clients[1].StartTicks = 99;
  assert.equal(analyze(records, options).summaries[0].correlations.ReplyMs.lead1s.n, 21);
});
