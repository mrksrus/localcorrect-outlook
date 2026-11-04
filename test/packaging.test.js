import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync, mkdirSync, writeFileSync, copyFileSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {execFileSync} from 'node:child_process';
test('legacy archive excludes developer tools, test profiles and generated native payloads', () => {
 const root=mkdtempSync(join(tmpdir(),'localcorrect-package-'));
 try {
  for (const dir of ['scripts','dist','build/site','src','work/toolchain','test-results/profile','native/AddIn/bin','native/AddIn/obj','native/assets']) mkdirSync(join(root,dir),{recursive:true});
  copyFileSync('scripts/package.py',join(root,'scripts/package.py'));
  for (const file of ['build/site/index.html','src/app.js','work/toolchain/sdk.dat','test-results/profile/private.txt','native/AddIn/bin/compiled.dll','native/AddIn/obj/cache.dat','native/assets/payload.zip','native/assets/.gitkeep']) writeFileSync(join(root,file),'synthetic packaging fixture');
  execFileSync('python',['scripts/package.py'],{cwd:root,stdio:'pipe'});
  const names=JSON.parse(execFileSync('python',['-c','import zipfile,json; print(json.dumps(zipfile.ZipFile("dist/localcorrect-outlook-1.0.0-rc.1.zip").namelist()))'],{cwd:root,encoding:'utf8'}));
  assert.ok(names.includes('site/index.html'));assert.ok(names.includes('outlook-source/src/app.js'));assert.ok(names.includes('outlook-source/native/assets/.gitkeep'));
  for(const excluded of ['sdk.dat','private.txt','compiled.dll','cache.dat','payload.zip']) assert.ok(!names.some(n=>n.endsWith(excluded)),excluded);
 }finally{rmSync(root,{recursive:true,force:true});}
});
