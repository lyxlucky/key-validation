import { spawn } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const env = { ...process.env };
const envPath = path.join(root, '.env');
if (existsSync(envPath)) {
  for (const line of readFileSync(envPath, 'utf8').split(/\r?\n/)) {
    const match = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$/.exec(line);
    if (!match || env[match[1]] !== undefined) continue;
    env[match[1]] = match[2].replace(/^(['"])(.*)\1$/, '$2');
  }
}
for (const variable of ['ConnectionStrings__Database', 'Licensing__Keys__v1']) {
  if (!env[variable]) {
    console.error(`缺少 ${variable}；请按 README 的本地开发步骤配置环境变量或 .env。`);
    process.exit(1);
  }
}
if (!existsSync(path.join(root, 'frontend/node_modules/vite/bin/vite.js'))) {
  console.error('请先在 frontend 目录运行 npm ci。');
  process.exit(1);
}
env.ASPNETCORE_ENVIRONMENT = 'Development';
env.ASPNETCORE_URLS = 'http://127.0.0.1:5080';
env.Licensing__CurrentKeyVersion ??= 'v1';
env.DataProtection__Path ??= path.join(root, '.local/data-protection');
const children = [
  spawn('dotnet', ['run', '--no-launch-profile', '--project', 'backend/src/KeyValidation.Api'], { cwd: root, env, stdio: 'inherit', windowsHide: true }),
  spawn(process.execPath, ['node_modules/vite/bin/vite.js', '--host', '127.0.0.1'], { cwd: path.join(root, 'frontend'), env, stdio: 'inherit', windowsHide: true }),
];
let stopping = false;
function stop() {
  if (stopping) return;
  stopping = true;
  for (const child of children) {
    if (!child.pid || child.exitCode !== null) continue;
    if (process.platform === 'win32') {
      // Scope termination to children launched by this invocation.
      spawn('taskkill', ['/pid', String(child.pid), '/t', '/f'], { windowsHide: true, stdio: 'ignore' });
    } else child.kill('SIGTERM');
  }
}
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
for (const child of children) {
  child.on('error', () => { console.error('启动开发进程失败，请检查 .NET 和 Node.js。'); process.exitCode = 1; stop(); });
  child.on('exit', code => { if (!stopping) { process.exitCode = code || 0; stop(); } });
}
console.log('开发地址 http://127.0.0.1:5173，API http://127.0.0.1:5080；Ctrl+C 结束本次启动的进程。');
