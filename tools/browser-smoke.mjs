// Browser smoke test: loads the game in headless Chromium, prints the page console, clicks the
// canvas, and saves before/after screenshots.
//
//   node tools/browser-smoke.mjs <chrome-binary> <url> <out-dir> [seconds]
//
// Uses the DevTools protocol directly (Node 22+ has a global WebSocket), so it has no npm deps.
import { spawn } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [chrome, url, outDir, seconds = '6'] = process.argv.slice(2);
if (!chrome || !url || !outDir) {
    console.error('usage: node browser-smoke.mjs <chrome-binary> <url> <out-dir> [seconds]');
    process.exit(2);
}

const sleep = ms => new Promise(r => setTimeout(r, ms));
const port = 9300 + Math.floor(Math.random() * 500);
const profile = mkdtempSync(join(tmpdir(), 'browser-smoke-'));
const browser = spawn(chrome, [
    '--headless=new',
    `--remote-debugging-port=${port}`,
    `--user-data-dir=${profile}`,
    // Software WebGL so this works on machines and CI runners without a GPU.
    '--use-angle=swiftshader',
    '--enable-unsafe-swiftshader',
    '--window-size=1280,720',
    'about:blank',
], { stdio: 'ignore' });

let failed = false;
try {
    let target;
    for (let i = 0; i < 100 && !target; i++) {
        try {
            const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
            target = list.find(t => t.type === 'page');
        } catch { /* not up yet */ }
        if (!target) await sleep(100);
    }
    if (!target) throw new Error('Chromium did not expose a page target');

    const ws = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });

    let nextId = 0;
    const pending = new Map();
    const send = (method, params = {}) => new Promise(resolve => {
        const id = ++nextId;
        pending.set(id, resolve);
        ws.send(JSON.stringify({ id, method, params }));
    });

    ws.onmessage = ({ data }) => {
        const msg = JSON.parse(data);
        if (msg.id && pending.has(msg.id)) {
            pending.get(msg.id)(msg.result ?? msg.error);
            pending.delete(msg.id);
        } else if (msg.method === 'Runtime.consoleAPICalled') {
            const text = msg.params.args.map(a => a.value ?? a.description ?? '').join(' ');
            console.log(`[console.${msg.params.type}] ${text}`);
        } else if (msg.method === 'Runtime.exceptionThrown') {
            failed = true;
            const d = msg.params.exceptionDetails;
            console.log(`[exception] ${d.exception?.description ?? d.text}`);
        } else if (msg.method === 'Log.entryAdded') {
            console.log(`[log.${msg.params.entry.level}] ${msg.params.entry.text}`);
        }
    };

    await send('Runtime.enable');
    await send('Log.enable');
    await send('Page.enable');
    await send('Page.navigate', { url });

    const screenshot = async name => {
        const { data } = await send('Page.captureScreenshot', { format: 'png' });
        writeFileSync(join(outDir, name), Buffer.from(data, 'base64'));
        console.log(`[smoke] saved ${name}`);
    };

    const half = Number(seconds) * 500;
    await sleep(half);
    await screenshot('browser-1.png');

    // Click near the top-left so new sprites are distinguishable from the initial burst.
    for (const type of ['mousePressed', 'mouseReleased']) {
        await send('Input.dispatchMouseEvent', { type, x: 250, y: 160, button: 'left', clickCount: 1 });
    }
    await sleep(half);
    await screenshot('browser-2.png');
    ws.close();
} catch (err) {
    failed = true;
    console.error(`[smoke] ${err}`);
} finally {
    browser.kill();
    await sleep(200);
    rmSync(profile, { recursive: true, force: true });
}
process.exit(failed ? 1 : 0);
