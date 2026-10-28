// Steady traffic for a canary rollout: the analysis needs requests on the canary pods to judge them.
// No thresholds: failures are expected while a faulty canary takes its share, the rollout is what is tested.
//
//   k6 run -e BASE_URL=http://canary.preview.localtest.me:8080 -e SIGNING_KEY=... -e DURATION=6m load/canary-traffic.js
import http from 'k6/http';
import { sleep } from 'k6';
import { EMPLOYEE, MANAGER, token } from './lib/token.js';

const BASE_URL = __ENV.BASE_URL;
const SIGNING_KEY = __ENV.SIGNING_KEY;

export const options = {
  scenarios: {
    steady: { executor: 'constant-arrival-rate', rate: 20, timeUnit: '1s', duration: __ENV.DURATION || '5m', preAllocatedVUs: 10 },
  },
};

export function setup() {
  return { employee: token(SIGNING_KEY, EMPLOYEE, false), manager: token(SIGNING_KEY, MANAGER, true) };
}

export default function (tokens) {
  const asManager = Math.random() < 0.5;
  http.get(`${BASE_URL}/v1/absence-requests?pageSize=20`, {
    headers: { Authorization: `Bearer ${asManager ? tokens.manager : tokens.employee}` },
    tags: { name: 'list' },
  });
  sleep(0.1);
}
