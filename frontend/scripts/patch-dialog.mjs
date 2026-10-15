import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';

// dialog 1.10.0 snapshots panelRef while React Activity reconnects layout
// effects. The snapshot can lack focus(), crashing the whole workspace when a
// retained dialog reappears. Forward each method to the current child instead.
// Keep the exact version/source guard so dependency upgrades require a review.
const require = createRequire(import.meta.url);
const packageFile = require.resolve('@rc-component/dialog/package.json');
const metadata = JSON.parse(readFileSync(packageFile, 'utf8'));
if (metadata.version !== '1.10.0') throw new Error('Review the Activity dialog compatibility patch before upgrading @rc-component/dialog.');

const original = `  React.useImperativeHandle(ref, () => ({
    ...panelRef.current,
    inMotion: dialogRef.current.inMotion,
    enableMotion: dialogRef.current.enableMotion
  }));`;
const corrected = `  React.useImperativeHandle(ref, () => ({
    focus: () => panelRef.current?.focus(),
    inMotion: () => dialogRef.current?.inMotion() ?? false,
    enableMotion: () => dialogRef.current?.enableMotion() ?? false
  }));`;

for (const moduleKind of ['es', 'lib']) {
  const sourceFile = path.join(path.dirname(packageFile), moduleKind, 'Dialog', 'Content', 'index.js');
  const source = readFileSync(sourceFile, 'utf8').replace(/\r\n/g, '\n');
  if (source.includes(corrected)) continue;
  if (source.split(original).length !== 2) throw new Error(`Unexpected ${moduleKind} dialog source; review the Activity compatibility patch.`);
  writeFileSync(sourceFile, source.replace(original, corrected), 'utf8');
}
