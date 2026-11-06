import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

test('setup checks the system x64 VC runtime before install mutations and leaves uninstall independent', async () => {
  const source = await readFile('native/Installer/Program.cs', 'utf8');
  const prerequisite = source.indexOf('EnsureVisualCppRuntime();');
  assert.ok(prerequisite > source.indexOf('if (args.Contains("/uninstall"))'));
  assert.ok(prerequisite < source.indexOf('string retained = Install();'));
  assert.match(source, /RegistryKey\.OpenBaseKey\(RegistryHive\.LocalMachine, RegistryView\.Registry64\)/);
  assert.match(source, /machine\.OpenSubKey\(VisualCppPrerequisite\.RegistryPath, false\)/);
  assert.match(source, /Environment\.Is64BitProcess \? "System32" : "Sysnative"/);
});

test('native packaging uses a system VC prerequisite and excludes the redistribution recipe', async () => {
  const build = await readFile('scripts/build-native.mjs', 'utf8');
  assert.doesNotMatch(build, /native-helper-runtime\.py|native-helper-signatures\.ps1|vc-runtime-verification\.json/);
  assert.match(build, /deployment:'system prerequisite; not redistributed'/);
  assert.match(build, /vcruntime140_cor3\.dll/);
  assert.match(build, /Unexpected redistributed VC runtime file/);
});
