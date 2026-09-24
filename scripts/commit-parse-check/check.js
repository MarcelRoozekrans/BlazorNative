#!/usr/bin/env node
'use strict';
// ─────────────────────────────────────────────────────────────────────────────
// commit-parse-check — does every commit survive release-please's OWN parser? (#302)
//
// release-please splits a commit body into chunks wherever a blank line is followed
// by a paragraph starting `feat|fix|…(scope)?: `, parses each chunk separately, and
// DROPS a chunk that throws, logging it at debug level only. The workflow stays
// green. That is how cf8e956, the 0.12.0 breaking change, vanished from the notes:
// its bad line was in the FIRST chunk. 6ec3894 lost a later chunk the same way.
//
// This runs the real parseConventionalCommits at the version the release action
// bundles, recorded in action-version.json and pinned by ReleaseParserVersionPinTests.
// It does NOT restate the grammar: the throwing shape is narrower than
// "nested parentheses" and a regex would be wrong in both directions.
//
// Usage:
//   node check.js --self-test
//   node check.js --title "<PR title>" --messages <json array of commit messages>
// Exit: 0 clean · 1 a chunk would be dropped · 2 usage error or no input.
// ─────────────────────────────────────────────────────────────────────────────
const fs = require('fs');
const path = require('path');
const { parseConventionalCommits } = require('release-please/build/src/commit');

/** Every chunk release-please would drop from this message, as "label: reason". */
function droppedChunks(label, message) {
  const events = [];
  const record = (...args) => events.push(args.filter(a => typeof a === 'string').join(' '));
  const quiet = () => {};
  const logger = { error: record, warn: quiet, info: quiet, debug: record, trace: quiet };
  parseConventionalCommits([{ sha: label, message, files: [] }], logger);
  const dropped = [];
  for (let i = 0; i < events.length; i++) {
    if (events[i].startsWith('commit could not be parsed')) {
      const reason = (events[i + 1] || '').replace(/^error message:\s*/, '');
      dropped.push(`${label}: ${reason || '(no reason logged)'}`);
    }
  }
  return dropped;
}

/** The squash message GitHub builds under PR_TITLE + COMMIT_MESSAGES. */
function squashMessage(title, messages) {
  return [title, ...messages.map(m => `* ${m.trim()}`)].join('\n\n');
}

function selfTest() {
  const rows = [
    { file: 'cf8e956.txt', expect: 'red', why: 'lost headline — the #302 breaking change' },
    { file: '6ec3894.txt', expect: 'red', why: 'lost later chunk — the headline survived, a sub-commit did not' },
    { file: 'e345bca.txt', expect: 'green', why: 'nested parentheses that DO parse — the over-match guard' },
    { file: 'clean.txt', expect: 'green', why: 'an ordinary message' },
  ];
  let wrong = 0;
  for (const row of rows) {
    const text = fs.readFileSync(path.join(__dirname, 'fixtures', row.file), 'utf8');
    const got = droppedChunks(row.file, text).length > 0 ? 'red' : 'green';
    const ok = got === row.expect;
    if (!ok) wrong++;
    console.log(`${ok ? 'ok  ' : 'FAIL'} ${row.file}: expected ${row.expect}, got ${got} — ${row.why}`);
  }
  if (!rows.some(r => r.expect === 'red') || !rows.some(r => r.expect === 'green')) {
    console.log('FAIL the self-test needs at least one red and one green row, or it proves nothing');
    return 1;
  }
  return wrong === 0 ? 0 : 1;
}

function check(title, messagesFile) {
  const messages = JSON.parse(fs.readFileSync(messagesFile, 'utf8'));
  if (!Array.isArray(messages) || messages.length === 0) {
    console.error('commit-parse-check: zero commit messages to check — refusing to pass vacuously.');
    return 2;
  }
  const dropped = [];
  messages.forEach((m, i) => dropped.push(...droppedChunks(`commit ${i + 1} of ${messages.length}, "${m.split('\n')[0]}"`, m)));
  dropped.push(...droppedChunks('the squash message a merge would produce', squashMessage(title, messages)));
  if (dropped.length === 0) {
    console.log(`commit-parse-check: ${messages.length} commit(s) and the squash message all parse.`);
    return 0;
  }
  console.error('release-please would SILENTLY DROP part of this PR from the release notes:');
  for (const d of dropped) console.error(`  - ${d}`);
  console.error('Reword the named line: a "(" attached to a word, with another "(" inside it before it closes, is the usual cause. See CONTRIBUTING.md.');
  return 1;
}

function main(argv) {
  if (argv[0] === '--self-test') return selfTest();
  const t = argv.indexOf('--title');
  const m = argv.indexOf('--messages');
  if (t < 0 || m < 0 || !argv[t + 1] || !argv[m + 1]) {
    console.error('usage: check.js --self-test | check.js --title "<title>" --messages <file.json>');
    return 2;
  }
  return check(argv[t + 1], argv[m + 1]);
}

process.exit(main(process.argv.slice(2)));
