// Prints a token of the seeded employee, for the shell steps of e2e.sh (k6 already knows how to sign one).
import { EMPLOYEE, token } from '../../load/lib/token.js';

export const options = { iterations: 1, vus: 1 };

export default function () {
  console.log(`token=${token(__ENV.SIGNING_KEY, EMPLOYEE, false)}`);
}
