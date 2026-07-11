package io.blazornative.jni

/**
 * Phase 4.4 Gate 2 — the inspector page: ONE self-contained inline
 * HTML+CSS+JS string (no external requests, no build step, vanilla JS —
 * the design's page-scope mitigation). Served by [InspectorServer] at
 * `GET /`; everything dynamic arrives via the JSON API + SSE:
 *
 *  - header: component · port · live seq/frames · SSE status;
 *  - collapsible widget tree (`<details>/<summary>`, containers open by
 *    default; leaves are plain rows): `type#id "text" {props, styles}` plus
 *    per-event dispatch UI — a "fire <event>" button for click-like events,
 *    a payload input + "send change" for change; the returned rc (or error)
 *    shows inline next to the control until the SSE-triggered re-render
 *    replaces the pane (the dispatch also lands in the event log with its
 *    rc, so the outcome is never lost);
 *  - patch tail (newest first, last 50) fed INCREMENTALLY via
 *    `/api/patches?since=` with a client-side lastPatchSeq cursor;
 *  - event log (newest first), re-fetched on SSE `event-logged`;
 *  - SSE pull model: `tree-changed` → re-fetch tree + patch tail;
 *    EventSource auto-reconnects, the header shows live/reconnecting;
 *  - footer: the fast-restart honesty line (the 4.3 constraint restated).
 *
 * TEMPLATING: the Kotlin side injects only the component name and port via
 * token replacement (`@COMPONENT@`/`@PORT@`) — the page body is a raw string
 * deliberately free of `$` (no Kotlin interpolation, no JS template
 * literals), so what you read here is byte-for-byte what the browser gets.
 */
internal object InspectorPage {

    fun render(componentName: String, port: Int): String =
        PAGE.replace("@COMPONENT@", escapeHtml(componentName)).replace("@PORT@", port.toString())

    private fun escapeHtml(s: String): String = buildString(s.length + 8) {
        for (c in s) when (c) {
            '&' -> append("&amp;")
            '<' -> append("&lt;")
            '>' -> append("&gt;")
            '"' -> append("&quot;")
            else -> append(c)
        }
    }

    // NOTE: raw string, no `$` anywhere below (see the TEMPLATING KDoc note).
    private val PAGE = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>BlazorNative Inspector</title>
<style>
  * { box-sizing: border-box; }
  body { font-family: Consolas, Menlo, "Courier New", monospace; font-size: 13px; margin: 0; background: #fafafa; color: #222; }
  header { padding: 8px 14px; background: #2d2d33; color: #eee; display: flex; gap: 16px; align-items: baseline; flex-wrap: wrap; }
  header h1 { font-size: 15px; margin: 0; }
  header .meta { color: #bbb; }
  header .meta b { color: #fff; }
  #sse-status { color: #9c9; }
  main { display: grid; grid-template-columns: minmax(0, 1.5fr) minmax(0, 1fr); gap: 10px; padding: 10px 14px; }
  section { background: #fff; border: 1px solid #ddd; border-radius: 4px; padding: 8px 10px; min-width: 0; }
  section h2 { font-size: 12px; text-transform: uppercase; letter-spacing: .05em; margin: 0 0 6px; color: #666; }
  #tree-section { grid-row: span 2; max-height: 82vh; overflow: auto; }
  #patches, #events { white-space: pre-wrap; word-break: break-word; max-height: 34vh; overflow: auto; margin: 0; font-size: 12px; }
  details { margin-left: 16px; }
  #tree > details, #tree > .leaf { margin-left: 0; }
  .leaf { margin-left: 33px; padding: 1px 0; }
  summary { cursor: pointer; padding: 1px 0; }
  .nodetype { color: #0550ae; font-weight: bold; }
  .nodeid { color: #999; }
  .nodetext { color: #953800; }
  .kv { color: #777; }
  .evt { color: #2b7a0b; }
  button { font: inherit; font-size: 11px; margin-left: 6px; padding: 0 7px; cursor: pointer; border: 1px solid #bbb; border-radius: 3px; background: #f3f3f3; }
  button:hover { background: #e7e7e7; }
  input.payload { font: inherit; font-size: 11px; width: 100px; margin-left: 6px; border: 1px solid #bbb; border-radius: 3px; padding: 0 4px; }
  .rc { margin-left: 5px; color: #0550ae; }
  footer { padding: 7px 14px; color: #888; border-top: 1px solid #ddd; font-size: 12px; }
</style>
</head>
<body>
<header>
  <h1>BlazorNative Inspector</h1>
  <span class="meta">component <b>@COMPONENT@</b> &middot; port @PORT@ &middot; seq <span id="seq">&ndash;</span> &middot; frames <span id="frames">&ndash;</span></span>
  <span id="sse-status">sse: connecting&hellip;</span>
</header>
<main>
  <section id="tree-section"><h2>Widget tree</h2><div id="tree"></div></section>
  <section><h2>Patches (newest first, last 50)</h2><pre id="patches"></pre></section>
  <section><h2>Event log (newest first)</h2><pre id="events"></pre></section>
</main>
<footer>native session over the published NativeAOT dll &middot; fast-restart, not hot-reload &mdash; restart the host (make inspect) to pick up a rebuilt dll &middot; http://127.0.0.1:@PORT@/</footer>
<script>
'use strict';
function byId(id) { return document.getElementById(id); }
var lastPatchSeq = 0;
var patchTail = [];

function dispatchEvt(handlerId, eventName, payload, rcEl) {
  var body = { handlerId: handlerId, eventName: eventName };
  if (payload !== null) body.payload = payload;
  fetch('/api/dispatch', { method: 'POST', body: JSON.stringify(body) })
    .then(function (r) { return r.json(); })
    .then(function (j) { rcEl.textContent = (j.rc !== undefined) ? 'rc=' + j.rc : 'error: ' + j.error; })
    .catch(function (e) { rcEl.textContent = 'failed: ' + e; });
}

function nodeLabel(n) {
  var span = document.createElement('span');
  var t = document.createElement('span'); t.className = 'nodetype'; t.textContent = n.type; span.appendChild(t);
  var i = document.createElement('span'); i.className = 'nodeid'; i.textContent = '#' + n.id; span.appendChild(i);
  if (n.text !== undefined) {
    var x = document.createElement('span'); x.className = 'nodetext'; x.textContent = ' "' + n.text + '"'; span.appendChild(x);
  }
  var kv = [];
  var k;
  if (n.props) for (k in n.props) kv.push(k + '=' + n.props[k]);
  if (n.styles) for (k in n.styles) kv.push(k + '=' + n.styles[k]);
  if (kv.length) {
    var p = document.createElement('span'); p.className = 'kv'; p.textContent = ' {' + kv.join(', ') + '}'; span.appendChild(p);
  }
  if (n.events) {
    for (k in n.events) {
      (function (evName, handlerId) {
        var e = document.createElement('span'); e.className = 'evt'; e.textContent = ' ' + evName + '=#' + handlerId; span.appendChild(e);
        var rc = document.createElement('span'); rc.className = 'rc';
        if (evName === 'change') {
          var inp = document.createElement('input'); inp.className = 'payload'; inp.placeholder = 'payload';
          inp.onclick = function (ev) { ev.stopPropagation(); };
          var send = document.createElement('button'); send.textContent = 'send change';
          send.onclick = function (ev) { ev.preventDefault(); ev.stopPropagation(); dispatchEvt(handlerId, evName, inp.value, rc); };
          span.appendChild(inp); span.appendChild(send);
        } else {
          var fire = document.createElement('button'); fire.textContent = 'fire ' + evName;
          fire.onclick = function (ev) { ev.preventDefault(); ev.stopPropagation(); dispatchEvt(handlerId, evName, null, rc); };
          span.appendChild(fire);
        }
        span.appendChild(rc);
      })(k, n.events[k]);
    }
  }
  return span;
}

function renderNode(n) {
  if (n.children && n.children.length) {
    var d = document.createElement('details'); d.open = true;
    var s = document.createElement('summary'); s.appendChild(nodeLabel(n)); d.appendChild(s);
    for (var i = 0; i < n.children.length; i++) d.appendChild(renderNode(n.children[i]));
    return d;
  }
  var leaf = document.createElement('div'); leaf.className = 'leaf'; leaf.appendChild(nodeLabel(n));
  return leaf;
}

function refreshTree() {
  return fetch('/api/tree').then(function (r) { return r.json(); }).then(function (j) {
    byId('seq').textContent = j.seq;
    byId('frames').textContent = j.framesApplied;
    var root = byId('tree'); root.textContent = '';
    for (var i = 0; i < j.roots.length; i++) root.appendChild(renderNode(j.roots[i]));
  });
}

function refreshPatches() {
  return fetch('/api/patches?since=' + lastPatchSeq).then(function (r) { return r.json(); }).then(function (j) {
    for (var i = 0; i < j.patches.length; i++) {
      var p = j.patches[i];
      if (p.seq > lastPatchSeq) lastPatchSeq = p.seq;
      patchTail.unshift('#' + p.seq + ' f' + p.frameId + ' ' + p.summary);
    }
    if (patchTail.length > 50) patchTail.length = 50;
    byId('patches').textContent = patchTail.join('\n');
  });
}

function refreshEvents() {
  return fetch('/api/events').then(function (r) { return r.json(); }).then(function (j) {
    var lines = [];
    for (var i = j.events.length - 1; i >= 0; i--) {
      var e = j.events[i];
      lines.push('#' + e.seq + ' [' + e.kind + '] ' + e.message);
    }
    byId('events').textContent = lines.join('\n');
  });
}

var es = new EventSource('/sse');
es.addEventListener('tree-changed', function () { refreshTree(); refreshPatches(); });
es.addEventListener('event-logged', function () { refreshEvents(); });
es.onopen = function () { byId('sse-status').textContent = 'sse: live'; };
es.onerror = function () { byId('sse-status').textContent = 'sse: reconnecting…'; };
refreshTree(); refreshPatches(); refreshEvents();
</script>
</body>
</html>
"""
}
