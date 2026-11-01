import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import https from 'node:https';
import { readFileSync } from 'node:fs';
test('loopback HTTPS helper: explicit CA, static assets, no listings or request logs', async () => {
  const dir = await mkdtemp(path.join(os.tmpdir(), 'localcorrect-tls-'));
  let server;
  try {
    await writeFile(path.join(dir, 'index.html'), '<p>Synthetic TLS fixture</p>');
    execFileSync('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-sha256', '-nodes', '-days', '1', '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost,IP:127.0.0.1', '-keyout', path.join(dir, 'key.pem'), '-out', path.join(dir, 'cert.pem')], { stdio: 'ignore' });
    server = spawn('python', ['scripts/serve_https.py', '--cert', path.join(dir, 'cert.pem'), '--key', path.join(dir, 'key.pem'), '--directory', dir, '--port', '0']);
    let stderr = ''; server.stderr.on('data', d => { stderr += d; });
    const port = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('TLS helper did not start')), 10000);
      server.once('error', reject); server.once('exit', code => { clearTimeout(timer); reject(new Error(`TLS helper exited ${code}: ${stderr}`)); });
      server.stdout.on('data', d => { const match = /localhost:(\d+)/.exec(String(d)); if (match) { clearTimeout(timer); resolve(Number(match[1])); } });
    });
    const request = requestPath => new Promise((resolve, reject) => {
      https.get({ hostname: '127.0.0.1', port, path: requestPath, ca: readFileSync(path.join(dir, 'cert.pem')) }, res => {
        let body = ''; res.on('data', d => { body += d; }); res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body }));
      }).on('error', reject);
    });
    const response = await request('/index.html');
    await mkdir(path.join(dir, 'empty'));
    assert.equal((await request('/empty/')).status, 404);
    assert.equal(response.status, 200); assert.match(response.body, /Synthetic TLS fixture/);
    assert.equal(response.headers['referrer-policy'], 'no-referrer'); assert.equal(response.headers['x-content-type-options'], 'nosniff');
    assert.equal(stderr, '');
  } finally {
    if (server && server.exitCode === null) { const done = new Promise(resolve => server.once('exit', resolve)); server.kill(); await done; }
    await rm(dir, { recursive: true, force: true });
  }
});
