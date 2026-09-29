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
// NON-CONVENTIONAL SUBJECTS. A commit whose SUBJECT is not a conventional
// header at all — e.g. GitHub's "Merge branch 'main' into …" or a bare "wip" —
// is common here: this repo's strict branch protection makes "Update branch"
// merge commits routine on every PR. It is tempting to just skip such a
// commit's parse entirely, on the theory that release-please ignores it. THAT
// THEORY IS FALSE, measured directly: parseConventionalCommits's splitter
// looks for a conventional-type paragraph ANYWHERE in the message, not only at
// the subject. `Merge branch 'main' into x\n\nfix(a): real change\n\n<body>`
// splits into the (dropped) header chunk PLUS a second chunk starting
// `fix(a): real change`, and that second chunk is parsed and can itself drop —
// e.g. if its body carries cf8e956's bad line. So a commit with a
// non-conventional subject can still contribute a real conventional-commit
// chunk to release-please's output, and a bad line inside that later chunk is
// exactly as real a loss as one in a conventional commit's own body — under
// either squash, merge-commit or rebase-merge, since merge-commit and
// rebase-merge land the commit's message on main essentially verbatim.
//
// The fix is not a second copy of release-please's grammar: isConventionalSubject()
// asks the real parser whether the SUBJECT LINE ALONE is a conventional header,
// only to decide whether to trust the message's own subject or SUBSTITUTE a
// neutral one. commitVerdict() does the substitution — replacing only the
// first non-blank line with the fixed header `chore: non-conventional subject
// stands here`, byte-identical otherwise — and hands that substitute to
// droppedChunks(), so release-please's OWN splitter decides where the chunks
// are; nothing about its splitting rule is restated here. The original header
// chunk, now conventional, no longer produces a false drop from ITS content
// (which was never going to reach the changelog anyway, since the real message
// never had a conventional subject) — but a bad line inside that first chunk's
// own body, before any later conventional paragraph, WILL still red under the
// substitute. That is deliberate and conservative: it matches what the
// squash-message check reports for the same line. `skipped` in the verdict
// means "the real parser could not read the subject line" — it is a label
// for the info line, not a promise that the commit contributed no dropped
// chunks.
//
// WHAT THIS STILL DOES NOT COVER: isConventionalSubject() cannot tell a
// subject that is genuinely not conventional — a merge line, a bare "wip" —
// from one that IS a malformed conventional header, e.g. `fix(): x`,
// `fix(a(b)): x` or `fix(a)(b): x`. The real parser throws on all of them, so
// commitVerdict() substitutes all of them the same way. release-please treats
// the two cases differently, though: a genuinely non-conventional subject
// with no later conventional paragraph contributes nothing to the changelog,
// so substituting it loses nothing — but a malformed conventional subject IS
// a real intended change, and release-please DROPS it, with zero commits
// parsed and "could not be parsed" logged. After substitution this guard
// goes GREEN on that malformed subject too, in per-commit mode, exactly as it
// would for a genuine merge commit. Telling the two apart would need a
// second copy of the conventional-commit grammar as a regex, which this
// guard refuses by design (see above). Under a squash merge, this repo's
// standard, the PR TITLE is what actually reaches main, and the `pr-title`
// job in this workflow lints it, so a malformed title reds there already.
// The real exposure this guard does not cover is narrower than it first
// looks: a malformed conventional subject on a commit that is not the PR
// title, landing via merge-commit or rebase-merge rather than squash.
//
// The squash-message check is separate and unchanged: it runs droppedChunks()
// on the actual squash message every messages array would produce, and it
// remains the authoritative check for what a squash merge lands on main —
// independent of any per-commit substitution.
//
// Usage:
//   node check.js --self-test
//   node check.js --title "<PR title>" --messages <json array of commit messages>
// Exit: 0 clean · 1 a chunk would be dropped · 2 usage error or no input.
// ─────────────────────────────────────────────────────────────────────────────
const fs = require('fs');
const path = require('path');
const { parseConventionalCommits } = require('release-please/build/src/commit');
// @conventional-commits/parser is deliberately release-please's OWN transitive
// dependency, locked at 0.4.1 in package-lock.json (release-please's own pin,
// not ours) — never add it here as a direct dependency at a different version,
// or isConventionalSubject() would stop asking the same question the release
// action's parser actually answers.
const { parser: parseConventionalSubject } = require('@conventional-commits/parser');

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

/**
 * Approximates GitHub's PR_TITLE + COMMIT_MESSAGES squash message. GitHub
 * appends " (#N)" to the title and reorders trailers; neither has changed a
 * parse result here.
 */
function squashMessage(title, messages) {
  return [title, ...messages.map(m => `* ${m.trim()}`)].join('\n\n');
}

/**
 * Index of the first non-blank line. git's own cleanup strips leading blank
 * lines before a commit message is stored, but a message built by hand — as
 * the self-test and the GitHub API both can produce — may still carry one,
 * and release-please's splitter treats a leading blank line as insignificant
 * (`"\nfix: x"` still parses as `fix: x`). Both isConventionalSubject() and
 * the substitution below must agree on which line is "the subject" or they
 * would disagree with each other on the same message.
 */
function firstNonBlankLineIndex(lines) {
  let i = 0;
  while (i < lines.length && lines[i].trim() === '') i++;
  return i;
}

/**
 * True when this message's first NON-BLANK line alone parses as a
 * conventional-commit header, using the real @conventional-commits/parser
 * (never a regex — the throwing shape is narrower than any pattern we'd
 * hand-write).
 */
function isConventionalSubject(message) {
  const lines = message.split('\n');
  const i = firstNonBlankLineIndex(lines);
  if (i >= lines.length) return false;
  try {
    parseConventionalSubject(lines[i]);
    return true;
  } catch {
    return false;
  }
}

/**
 * Replaces only the first non-blank line with a fixed, neutral conventional
 * header, leaving every other line byte-identical — so line numbers in a
 * parser error still point at the real line. This does not decide anything
 * about the message; it only lets release-please's OWN splitter see a
 * parseable subject, so its splitting rule — not a copy of it — decides where
 * the chunks are.
 */
function substituteNeutralSubject(message) {
  const lines = message.split('\n');
  const i = firstNonBlankLineIndex(lines);
  if (i < lines.length) lines[i] = 'chore: non-conventional subject stands here';
  return lines.join('\n');
}

/**
 * The per-commit verdict check() and the self-test both drive. `skipped` is
 * true when the message's own subject is not conventional — a label for the
 * info line, not a promise that nothing was dropped: release-please's
 * splitter can still find a real conventional chunk later in the body, and a
 * bad line there is reported exactly like any other dropped chunk. When the
 * subject is not conventional, droppedChunks() runs on a substitute with only
 * the subject line replaced, so the header's own non-conventional content
 * never produces a false drop, while everything after it is parsed exactly as
 * release-please would parse it.
 */
function commitVerdict(label, message) {
  const skipped = !isConventionalSubject(message);
  const parsed = skipped ? substituteNeutralSubject(message) : message;
  return { skipped, dropped: droppedChunks(label, parsed) };
}

const BAD_LINE = '`Width="@(Width is { } w ? (BnAutoLength?)w : null)"` spelled out by hand';

function selfTest() {
  const rows = [
    { file: 'cf8e956.txt', expect: 'red', why: 'lost headline — the #302 breaking change' },
    { file: '6ec3894.txt', expect: 'red', why: 'lost later chunk — the headline survived, a sub-commit did not' },
    { file: 'e345bca.txt', expect: 'green', why: 'nested parentheses that DO parse — the over-match guard' },
    { file: 'clean.txt', expect: 'green', why: 'an ordinary message' },
    { file: 'merge.txt', expect: 'green', why: 'a non-conventional subject with no later conventional paragraph — nothing for release-please to find' },
  ];
  let wrong = 0;
  for (const row of rows) {
    const text = fs.readFileSync(path.join(__dirname, 'fixtures', row.file), 'utf8');
    const got = commitVerdict(row.file, text).dropped.length > 0 ? 'red' : 'green';
    const ok = got === row.expect;
    if (!ok) wrong++;
    console.log(`${ok ? 'ok  ' : 'FAIL'} ${row.file}: expected ${row.expect}, got ${got} — ${row.why}`);
  }

  // Non-conventional subject, but a LATER paragraph is a real conventional
  // chunk carrying cf8e956's bad line. Proves the substitute approach catches
  // what a whole-commit skip would miss: release-please's own splitter still
  // finds "fix(a): real change" and drops it for the nested-paren line.
  const mergeWithBadParagraph = {
    name: 'merge-with-conventional-paragraph (synthetic)',
    message: `Merge branch 'main' into x\n\nfix(a): real change\n\n${BAD_LINE}`,
    expect: 'red',
    why: 'a later conventional paragraph with a bad line reds even though the subject is a merge commit',
  };
  // Its clean twin, same shape, no bad line — proves the substitute does not
  // over-report on an ordinary later paragraph.
  const mergeWithCleanParagraph = {
    name: 'merge-with-conventional-paragraph-clean (synthetic)',
    message: `Merge branch 'main' into x\n\nfix(a): real change\n\na clean line`,
    expect: 'green',
    why: 'the clean twin — a later conventional paragraph with no bad line stays green',
  };
  for (const row of [mergeWithBadParagraph, mergeWithCleanParagraph]) {
    const got = commitVerdict(row.name, row.message).dropped.length > 0 ? 'red' : 'green';
    const ok = got === row.expect;
    if (!ok) wrong++;
    console.log(`${ok ? 'ok  ' : 'FAIL'} ${row.name}: expected ${row.expect}, got ${got} — ${row.why}`);
    rows.push({ ...row, file: row.name });
  }

  // The squash-contrast row: the squash-message check is unchanged and does
  // not go through commitVerdict()'s substitution at all — it parses the
  // ACTUAL squash message, so it must still catch the bad line on its own.
  const contrastCommit = `Merge branch 'main' into x\n\n${BAD_LINE}`;
  const squashRow = {
    file: 'squash-contrast (synthetic)',
    expect: 'red',
    why: 'the squash-message check is unchanged and still catches a bad line on its own',
  };
  const squashGot = droppedChunks(squashRow.file, squashMessage('fix: contrast', [contrastCommit])).length > 0 ? 'red' : 'green';
  const squashOk = squashGot === squashRow.expect;
  if (!squashOk) wrong++;
  console.log(`${squashOk ? 'ok  ' : 'FAIL'} ${squashRow.file}: expected ${squashRow.expect}, got ${squashGot} — ${squashRow.why}`);
  rows.push(squashRow);

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
  let skipped = 0;
  messages.forEach((m, i) => {
    const label = `commit ${i + 1} of ${messages.length}, "${m.split('\n')[0]}"`;
    const verdict = commitVerdict(label, m);
    if (verdict.skipped) {
      skipped++;
      console.log(`${label}: the parser cannot read this subject — release-please ignores a non-conventional one, but drops a malformed conventional one; its later paragraphs are still checked`);
    }
    dropped.push(...verdict.dropped);
  });
  dropped.push(...droppedChunks('the squash message a merge would produce', squashMessage(title, messages)));
  if (dropped.length === 0) {
    console.log(`commit-parse-check: ${messages.length} commit(s) (${skipped} skipped as non-conventional) and the squash message all parse.`);
    return 0;
  }
  console.error('release-please would SILENTLY DROP part of this PR from the release notes:');
  for (const d of dropped) console.error(`  - ${d}`);
  console.error('Reword the named line: a "(" attached to a word, with another "(" inside it before it closes, is a common cause. See CONTRIBUTING.md.');
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
