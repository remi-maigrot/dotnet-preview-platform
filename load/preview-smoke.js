// Short load test run against every preview environment (preview workflow) and the release candidate.
// It is a guard against obvious regressions (an N+1 query, a missing index), not a capacity test.
//
//   k6 run -e BASE_URL=http://pr-42.preview.localtest.me:8080 -e SIGNING_KEY=... load/preview-smoke.js
//
// Thresholds mirror the SLO in docs/observability.md: 95 % of requests under 300 ms, less than 1 % errors.
import http from 'k6/http';
import crypto from 'k6/crypto';
import encoding from 'k6/encoding';
import { check, sleep } from 'k6';

const BASE_URL = __ENV.BASE_URL;
const SIGNING_KEY = __ENV.SIGNING_KEY;

// Fixed accounts created by the preview data seeder (src/Preview.DataSeeder/KnownAccounts.cs).
const EMPLOYEE = '6f1c2c1e-7a3b-4c8e-9a51-0b6f0d1a0002';
const MANAGER = '6f1c2c1e-7a3b-4c8e-9a51-0b6f0d1a0001';

export const options = {
  scenarios: {
    browse_and_submit: {
      executor: 'ramping-vus',
      stages: [
        { duration: '10s', target: 10 },
        { duration: '30s', target: 10 },
        { duration: '5s', target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<300'],
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
  },
};

function base64url(value) {
  return encoding.b64encode(value, 'rawurl');
}

// HS256 token signed with the preview's key, the way the API expects it (issuer, audience, sub, role).
function token(subject, manager) {
  const now = Math.floor(Date.now() / 1000);
  const header = base64url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const claims = { sub: subject, iss: 'preview-platform', aud: 'sample-api', iat: now, nbf: now, exp: now + 900 };
  if (manager) {
    claims.role = 'manager';
  }
  const payload = base64url(JSON.stringify(claims));
  const signature = crypto.hmac('sha256', SIGNING_KEY, `${header}.${payload}`, 'base64rawurl');
  return `${header}.${payload}.${signature}`;
}

export function setup() {
  if (!BASE_URL || !SIGNING_KEY) {
    throw new Error('BASE_URL and SIGNING_KEY are required.');
  }
  const ready = http.get(`${BASE_URL}/health/ready`);
  if (ready.status !== 200) {
    throw new Error(`The environment is not ready: /health/ready returned ${ready.status}.`);
  }
  return { employee: token(EMPLOYEE, false), manager: token(MANAGER, true) };
}

function mondayInWeeks(weeks) {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() + ((8 - date.getUTCDay()) % 7) + 7 * weeks);
  return date.toISOString().slice(0, 10);
}

function plusDays(isoDate, days) {
  const date = new Date(`${isoDate}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

export default function (tokens) {
  const asEmployee = { headers: { Authorization: `Bearer ${tokens.employee}` }, tags: { name: 'list' } };
  const asManager = { headers: { Authorization: `Bearer ${tokens.manager}` }, tags: { name: 'list' } };

  check(http.get(`${BASE_URL}/v1/absence-requests?pageSize=20`, asEmployee), { 'employee list 200': (r) => r.status === 200 });
  check(http.get(`${BASE_URL}/v1/absence-requests?status=Submitted`, asManager), { 'manager list 200': (r) => r.status === 200 });

  // Submit then cancel, so the calendar stays free for the next iterations. A 409 only means two virtual users
  // picked the same random week: it is a correct answer from the API, not an error.
  const start = mondayInWeeks(10 + Math.floor(Math.random() * 3000));
  const submit = http.post(
    `${BASE_URL}/v1/absence-requests`,
    JSON.stringify({ type: 'PaidLeave', startDate: start, endDate: plusDays(start, 2), comment: 'load test' }),
    {
      headers: {
        Authorization: `Bearer ${tokens.employee}`,
        'Content-Type': 'application/json',
        'Idempotency-Key': `${__VU}-${__ITER}-${Date.now()}`,
      },
      tags: { name: 'submit' },
      responseCallback: http.expectedStatuses(201, 409),
    },
  );
  check(submit, { 'submit 201 or 409': (r) => r.status === 201 || r.status === 409 });

  if (submit.status === 201) {
    const id = submit.json('id');
    const cancel = http.post(`${BASE_URL}/v1/absence-requests/${id}/cancel`, null, {
      headers: { Authorization: `Bearer ${tokens.employee}` },
      tags: { name: 'cancel' },
    });
    check(cancel, { 'cancel 200': (r) => r.status === 200 });
  }

  sleep(0.5);
}

// Markdown summary for the pull request comment, plus the default console output.
export function handleSummary(data) {
  const duration = data.metrics.http_req_duration.values;
  const failed = data.metrics.http_req_failed.values.rate;
  const rate = data.metrics.http_reqs.values.rate;
  const ok = (name) => (Object.values(data.metrics[name].thresholds || {}).every((t) => t.ok) ? '✅' : '❌');
  const markdown = [
    '| Metric | Value | Threshold |',
    '|---|---|---|',
    `| p95 latency | ${duration['p(95)'].toFixed(0)} ms | < 300 ms ${ok('http_req_duration')} |`,
    `| Error rate | ${(failed * 100).toFixed(2)} % | < 1 % ${ok('http_req_failed')} |`,
    `| Throughput | ${rate.toFixed(1)} req/s | |`,
    `| Requests | ${data.metrics.http_reqs.values.count} | |`,
  ].join('\n');
  return {
    stdout: `\n${markdown}\n`,
    'load/summary.md': markdown,
  };
}
