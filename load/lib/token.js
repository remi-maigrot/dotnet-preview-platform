// HS256 tokens signed with an environment's key, the way the API expects them (issuer, audience, sub, role).
import crypto from 'k6/crypto';
import encoding from 'k6/encoding';

// Fixed accounts created by the preview data seeder (src/Preview.DataSeeder/KnownAccounts.cs).
export const EMPLOYEE = '6f1c2c1e-7a3b-4c8e-9a51-0b6f0d1a0002';
export const MANAGER = '6f1c2c1e-7a3b-4c8e-9a51-0b6f0d1a0001';

function base64url(value) {
  return encoding.b64encode(value, 'rawurl');
}

export function token(signingKey, subject, manager) {
  const now = Math.floor(Date.now() / 1000);
  const header = base64url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const claims = { sub: subject, iss: 'preview-platform', aud: 'sample-api', iat: now, nbf: now, exp: now + 3600 };
  if (manager) {
    claims.role = 'manager';
  }
  const payload = base64url(JSON.stringify(claims));
  const signature = crypto.hmac('sha256', signingKey, `${header}.${payload}`, 'base64rawurl');
  return `${header}.${payload}.${signature}`;
}
