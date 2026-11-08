"use strict";
/* SpeakSafe web client. Vanilla JS, no build step. Talks to /api/v1 (envelope: { ok, data | error }). */

// ------------------------------------------------------------------ helpers
const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
const esc = s => String(s ?? "").replace(/[&<>"']/g, m => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[m]));
const fmtDate = iso => iso ? new Date(iso).toLocaleDateString([], { day: "numeric", month: "short", year: "numeric" }) : "";
const fmtTime = iso => iso ? new Date(iso).toLocaleString([], { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }) : "";
const kb = n => n >= 1048576 ? (n / 1048576).toFixed(1) + " MB" : Math.max(1, Math.round(n / 1024)) + " KB";

let META = null, ME = null, ORGS = [];
let timers = [];
let pendingTab = null, pendingConnect = null, preselectOrg = null;

async function api(path, { method = "GET", body, form } = {}) {
  const opts = { method, credentials: "same-origin", headers: {} };
  if (form) opts.body = form;
  else if (body !== undefined) { opts.headers["Content-Type"] = "application/json"; opts.body = JSON.stringify(body); }
  let res;
  try { res = await fetch("/api/v1" + path, opts); }
  catch { throw new Error("Can't reach SpeakSafe. Check your connection and try again."); }
  let json = null;
  try { json = await res.json(); } catch { /* non-JSON */ }
  if (!res.ok || !json || json.ok === false) {
    const e = new Error((json && json.error) || `Request failed (${res.status}).`);
    e.status = res.status;
    throw e;
  }
  return json.data;
}

function toast(t) {
  const el = $("#toast"); el.textContent = t; el.classList.add("on");
  clearTimeout(toast.h); toast.h = setTimeout(() => el.classList.remove("on"), 2800);
}

function showErr(sel, msg) { const el = $(sel); if (!el) return; el.textContent = msg || ""; el.classList.toggle("on", !!msg); }

/** buttons: [label, cls, fn]. fn may be async; return false to keep the dialog open. Errors show inside the dialog. */
function modal(title, html, buttons, wide) {
  $("#modalTitle").textContent = title;
  $("#modalBody").innerHTML = html;
  showErr("#modalErr", "");
  $(".modal").style.maxWidth = wide ? "640px" : "";
  const box = $("#modalBtns"); box.innerHTML = "";
  buttons.forEach(([label, cls, fn]) => {
    const b = document.createElement("button");
    b.type = "button"; b.className = "btn " + cls; b.textContent = label;
    b.onclick = async () => {
      if (!fn) { closeModal(); return; }
      b.disabled = true;
      try { const keep = await fn(); if (keep !== false) closeModal(); }
      catch (e) { showErr("#modalErr", e.message); }
      finally { b.disabled = false; }
    };
    box.appendChild(b);
  });
  $("#modal").classList.add("on");
  const first = $("#modalBody input, #modalBody select, #modalBody textarea") || box.firstChild;
  if (first) first.focus();
}
function closeModal() { $("#modal").classList.remove("on"); }
$("#modal").addEventListener("click", e => { if (e.target.id === "modal") closeModal(); });

async function copy(text) {
  try { await navigator.clipboard.writeText(text); toast("Copied. Keep it somewhere private."); }
  catch { toast("Copy was blocked. Write it down instead."); }
}

function nickname() {
  const a = ["Brave", "Quiet", "Bright", "Steady", "Calm", "Bold", "Kind", "Free", "Rising", "Gentle"];
  const b = ["Sun", "River", "Star", "Palm", "Dawn", "Wave", "Bird", "Moon", "Oak", "Leaf"];
  return a[Math.floor(Math.random() * 10)] + b[Math.floor(Math.random() * 10)] + Math.floor(10 + Math.random() * 90);
}

const STATUS_CLASS = { Submitted: "p-sub", Referred: "p-rev", InProgress: "p-ref", Closed: "p-sub" };
const pill = (status, label) => `<span class="pill ${STATUS_CLASS[status] || "p-sub"}"><i></i>${esc(label || status)}</span>`;
const avatar = (ini, color) => `<div class="avatar" style="background:${esc(color)}">${esc(ini)}</div>`;
const role = () => ME ? ME.role : "";

// ------------------------------------------------------------------ routing
const VIEWS = ["home", "register", "signin", "recover", "dashboard", "report", "done", "case", "connect", "messages", "org", "admin"];
const SECTIONS = ["about", "mission", "resources"];
const GUARD = {
  dashboard: ["survivor"], messages: ["survivor", "tracker", "officer"], org: ["officer"], admin: ["admin"],
  case: ["survivor", "tracker", "officer"],
};

function clearTimers() { timers.forEach(clearInterval); timers = []; }

function route() {
  clearTimers();
  let raw = decodeURIComponent(location.hash.slice(1)) || "home";
  let [view, arg] = raw.split(":");
  let section = null;
  if (SECTIONS.includes(view)) { section = view; view = "home"; }
  if (!VIEWS.includes(view)) view = "home";
  if (GUARD[view] && !GUARD[view].includes(role())) {
    pendingTab = view === "org" ? "officer" : view === "admin" ? "admin" : "survivor";
    location.hash = "signin";
    if (ME) toast("You don't have access to that page.");
    return;
  }
  $$(".view").forEach(v => v.classList.toggle("active", v.id === "v-" + view));
  $("#links").classList.remove("open"); $("#menuBtn").setAttribute("aria-expanded", "false");
  const r = RENDER[view]; if (r) r(arg);
  if (section) requestAnimationFrame(() => document.getElementById(section).scrollIntoView({ behavior: "smooth" }));
  else window.scrollTo(0, 0);
}
window.addEventListener("hashchange", route);
$("#menuBtn").onclick = () => { const o = $("#links").classList.toggle("open"); $("#menuBtn").setAttribute("aria-expanded", o); };
document.addEventListener("click", e => { const t = e.target.closest("[data-tabjump]"); if (t) pendingTab = t.dataset.tabjump; });

function renderAuth() {
  const r = role(), n = esc(ME?.name);
  const html = {
    "": `<a class="btn btn-ghost btn-sm hide-sm" href="#signin">Sign in</a><a class="btn btn-red btn-sm" href="#register">Register</a>`,
    survivor: `<a class="btn btn-ghost btn-sm hide-sm" href="#messages">Messages</a><a class="btn btn-green btn-sm" href="#dashboard">${n}</a><button class="btn btn-ghost btn-sm" id="outBtn" type="button">Sign out</button>`,
    tracker: `<a class="btn btn-green btn-sm" href="#case">Report ${n}</a><a class="btn btn-ghost btn-sm hide-sm" href="#messages">Messages</a><button class="btn btn-ghost btn-sm" id="outBtn" type="button">Close</button>`,
    officer: `<a class="btn btn-green btn-sm" href="#org">Cases</a><a class="btn btn-ghost btn-sm hide-sm" href="#messages">Messages</a><button class="btn btn-ghost btn-sm" id="outBtn" type="button">Sign out</button>`,
    admin: `<a class="btn btn-green btn-sm" href="#admin">Coordinator</a><button class="btn btn-ghost btn-sm" id="outBtn" type="button">Sign out</button>`,
  }[r];
  $("#authArea").innerHTML = html;
  const out = $("#outBtn");
  if (out) out.onclick = async () => { try { await api("/auth/logout", { method: "POST" }); } catch { } ME = null; renderAuth(); location.hash = "home"; toast("Signed out."); };
  const strip = $("#portalStrip");
  strip.hidden = !(r === "officer" || r === "admin");
  if (r === "officer") { $("#portalWho").innerHTML = `Signed in as <b>${n}</b> · ${esc(ME.org)}`; $("#portalHint").textContent = "Organisation portal"; }
  if (r === "admin") { $("#portalWho").innerHTML = `Signed in as <b>Coordinator</b>`; $("#portalHint").textContent = "Totals only. No report content."; }
}

async function refreshMe() { ME = await api("/me"); renderAuth(); }

function homeFor(r) { return { survivor: "dashboard", tracker: "case", officer: "org", admin: "admin" }[r] || "home"; }

// ------------------------------------------------------------------ register / sign in / recover
$("#shuffle").onclick = () => { $("#regUser").value = nickname(); };

$("#regForm").addEventListener("submit", async e => {
  e.preventDefault();
  if ($("#regPass").value !== $("#regPass2").value) return showErr("#regErr", "The two passphrases do not match.");
  try {
    const d = await api("/auth/register", { method: "POST", body: { nickname: $("#regUser").value.trim(), passphrase: $("#regPass").value, email: $("#regEmail").value.trim() || null } });
    showErr("#regErr", ""); e.target.reset();
    await refreshMe();
    showRecovery(d.recoveryCode, () => afterSignIn());
  } catch (err) { showErr("#regErr", err.message); }
});

function showRecovery(code, then) {
  modal("Save your recovery code", `<p>This is the only way back into your account if you forget your passphrase. We can't show it again.</p><div class="secret">${esc(code)}</div>`,
    [["Copy", "btn-ghost", () => { copy(code); return false; }], ["I have saved it", "btn-green", () => { then && then(); }]]);
}

function afterSignIn() {
  if (pendingConnect && role() === "survivor") { const o = pendingConnect; pendingConnect = null; startConversation(o); return; }
  location.hash = homeFor(role());
}

function setTab(tab) {
  $$("#signTabs [data-tab]").forEach(b => b.setAttribute("aria-selected", b.dataset.tab === tab));
  $$("[data-pane]").forEach(p => p.hidden = p.dataset.pane !== tab);
  showErr("#inErr", "");
  renderDemo(tab);
}
$("#signTabs").addEventListener("click", e => { const b = e.target.closest("[data-tab]"); if (b) setTab(b.dataset.tab); });

function renderDemo(tab) {
  const d = META?.demo, box = $("#demoBox");
  if (!d) { box.hidden = true; return; }
  let html = `<b>Demo sign-ins</b>`;
  if (tab === "survivor") html += `<button type="button" data-fill="survivor">${esc(d.survivor)} · passphrase <code>${esc(d.password)}</code></button>`;
  if (tab === "track") html += d.track.map(t => `<button type="button" data-fill="track" data-code="${esc(t.code)}" data-pin="${esc(t.pin)}">${esc(t.code)} · PIN <code>${esc(t.pin)}</code></button>`).join("");
  if (tab === "officer") html += d.officers.map(o => `<button type="button" data-fill="officer" data-user="${esc(o.username)}">${esc(o.name)} · <code>${esc(o.username)}</code></button>`).join("") + `<span>Passphrase <code>${esc(d.password)}</code></span>`;
  if (tab === "admin") html += `<span>Use the PIN from appsettings (development: <code>0000</code>).</span>`;
  box.innerHTML = html; box.hidden = false;
}
$("#demoBox").addEventListener("click", e => {
  const b = e.target.closest("[data-fill]"); if (!b) return;
  const pw = META.demo.password;
  if (b.dataset.fill === "survivor") { $("#inUser").value = META.demo.survivor; $("#inPass").value = pw; }
  if (b.dataset.fill === "track") { $("#tCode").value = b.dataset.code; $("#tPin").value = b.dataset.pin; }
  if (b.dataset.fill === "officer") { $("#oUser").value = b.dataset.user; $("#oPass").value = pw; }
});

async function signInWith(path, body, form) {
  try { await api(path, { method: "POST", body }); form.reset(); showErr("#inErr", ""); await refreshMe(); toast("Signed in."); afterSignIn(); }
  catch (err) { showErr("#inErr", err.message); }
}
$("#inForm").addEventListener("submit", e => { e.preventDefault(); signInWith("/auth/signin", { nickname: $("#inUser").value.trim(), passphrase: $("#inPass").value }, e.target); });
$("#trackForm").addEventListener("submit", e => { e.preventDefault(); signInWith("/auth/track", { code: $("#tCode").value.trim(), pin: $("#tPin").value.trim() }, e.target); });
$("#offForm").addEventListener("submit", e => { e.preventDefault(); signInWith("/auth/officer", { username: $("#oUser").value.trim(), passphrase: $("#oPass").value }, e.target); });
$("#admForm").addEventListener("submit", e => { e.preventDefault(); signInWith("/auth/admin", { pin: $("#aPin").value.trim() }, e.target); });

$("#recForm").addEventListener("submit", async e => {
  e.preventDefault();
  try {
    const d = await api("/auth/recover", { method: "POST", body: { nickname: $("#rUser").value.trim(), recoveryCode: $("#rCode").value.trim(), newPassphrase: $("#rPass").value } });
    e.target.reset(); showErr("#recErr", ""); await refreshMe();
    showRecovery(d.recoveryCode, () => { location.hash = "dashboard"; });
  } catch (err) { showErr("#recErr", err.message); }
});

// ------------------------------------------------------------------ views
const RENDER = {};

RENDER.register = () => { if (!$("#regUser").value) $("#regUser").value = nickname(); };
RENDER.signin = () => {
  if (ME) { location.hash = homeFor(role()); return; }
  $('#signTabs [data-tab="admin"]').hidden = !META?.adminEnabled;
  setTab(pendingTab || "survivor"); pendingTab = null;
};

// ---------- dashboard
RENDER.dashboard = async () => {
  $("#dashHello").textContent = "Hello, " + ME.name;
  try {
    const [reports, convs] = await Promise.all([api("/reports/mine"), api("/conversations")]);
    $("#tReports").textContent = reports.length;
    $("#tOpen").textContent = reports.filter(r => r.status !== "Closed").length;
    $("#tUnread").textContent = convs.reduce((n, c) => n + c.unread, 0);
    $("#caseList").innerHTML = reports.length ? reports.map(caseCard).join("") :
      `<div class="empty">You have not made a report yet.<br><a href="#report">Start a report</a></div>`;
    $("#threadMini").innerHTML = convs.length ? convs.slice(0, 5).map(threadMini).join("") :
      `<div class="empty">No conversations yet.<br><a href="#connect">Find an organisation</a></div>`;
  } catch (e) { $("#caseList").innerHTML = `<div class="err on">${esc(e.message)}</div>`; }
};

const STEPS = ["Submitted", "Referred", "InProgress"];
function caseCard(r) {
  const idx = r.status === "Closed" ? 3 : STEPS.indexOf(r.status);
  return `<a class="case" href="#case:${esc(r.code)}" style="text-decoration:none;color:inherit;display:grid">
    <div class="case-top"><span class="case-id">${esc(r.code)}</span>${pill(r.status, r.statusLabel)}</div>
    <div class="tags">${r.types.map(t => `<span>${esc(t)}</span>`).join("")}<span>${esc(r.place)}</span>${r.state ? `<span>${esc(r.state)}</span>` : ""}</div>
    <div class="tl">${["Submitted", "Sent to organisation", "Organisation is helping"].map((s, i) => `<div class="${i <= idx ? "done" : ""}">${s}</div>`).join("")}</div>
    <div style="font-size:13px;color:var(--muted)">Made ${fmtDate(r.createdUtc)} · ${r.org ? "Shared with " + esc(r.org) : "Not shared with any organisation"}</div>
  </a>`;
}
function threadMini(c, selId) {
  return `<a class="thread-mini${c.id === selId ? " sel" : ""}" href="#messages:${c.id}" style="text-decoration:none;color:inherit">${avatar(c.orgInitials, c.orgColor)}
    <div style="flex:1;min-width:0"><b>${esc(role() === "officer" ? c.with : c.orgName)}${c.unread ? ` <span class="badge">${c.unread}</span>` : ""}</b><span>${esc(c.lastText)}</span></div></a>`;
}

// ---------- report wizard
let step = 0, pickedOrg = null, files = [];

function chipHTML(name, type, items) {
  return items.map(it => {
    const [v, l, d, danger] = typeof it === "string" ? [it, it] : [it.id, it.label, it.detail, it.id === "assault"];
    return `<label class="opt${danger ? " danger" : ""}"><input type="${type}" name="${name}" value="${esc(v)}" data-label="${esc(l)}"><span><span><b>${esc(l)}</b>${d ? `<small>${esc(d)}</small>` : ""}</span></span></label>`;
  }).join("");
}
const checked = name => $$(`input[name=${name}]:checked`);
const opts = (list, first) => (first ? `<option value="">${esc(first)}</option>` : "") + list.map(x => `<option>${esc(x)}</option>`).join("");

function buildForms() {
  $("#typeChips").innerHTML = chipHTML("type", "checkbox", META.types);
  $("#placeChips").innerHTML = chipHTML("place", "radio", META.places);
  $("#supportChips").innerHTML = chipHTML("support", "checkbox", META.support);
  $("#rState").innerHTML = opts(META.states, "Prefer not to say");
  $("#rFreq").innerHTML = opts(META.frequencies);
  $("#rWho").innerHTML = opts(META.responsible);
  $("#rWit").innerHTML = opts(META.witnesses);
  $("#rDesc").maxLength = META.maxNarrative; $("#descMax").textContent = META.maxNarrative;
  $("#evHint").textContent = `Up to ${META.maxEvidence} files, ${META.maxEvidenceMb} MB each. Files are encrypted when stored.`;
  $("#fService").innerHTML = opts(META.services, "All services");
  $("#fState").innerHTML = opts(META.states, "All states");
}

$("#typeChips").addEventListener("change", () => $("#urgentAlert").classList.toggle("on", !!$('input[name=type][value=assault]:checked')));
$("#rDesc").addEventListener("input", e => $("#descCount").textContent = e.target.value.length);
$("#rFiles").addEventListener("change", e => {
  const add = [...e.target.files]; e.target.value = "";
  for (const f of add) {
    if (files.length >= META.maxEvidence) { toast(`Up to ${META.maxEvidence} files.`); break; }
    if (f.size > META.maxEvidenceMb * 1048576) { toast(`${f.name} is larger than ${META.maxEvidenceMb} MB.`); continue; }
    if (!META.evidenceTypes.includes((f.type || "").toLowerCase())) { toast(`${f.name}: use photos, PDFs, audio, MP4 video or text files.`); continue; }
    files.push(f);
  }
  renderFiles();
});
function renderFiles() {
  $("#fileList").innerHTML = files.map((f, i) => `<div><span>${esc(f.name)} · ${kb(f.size)}</span><button type="button" class="x" data-rm="${i}" aria-label="Remove ${esc(f.name)}" style="border:none;background:none;cursor:pointer;color:var(--red)">✕</button></div>`).join("");
}
$("#fileList").addEventListener("click", e => { const b = e.target.closest("[data-rm]"); if (b) { files.splice(+b.dataset.rm, 1); renderFiles(); } });
$("#supportChips").addEventListener("change", renderPick);

RENDER.report = async () => {
  $("#repForm").reset(); step = 0; files = []; renderFiles(); pickedOrg = preselectOrg; preselectOrg = null;
  $("#descCount").textContent = "0"; $("#urgentAlert").classList.remove("on");
  $("#rDate").max = new Date().toISOString().slice(0, 10);
  showStep();
  if (!ORGS.length) { try { ORGS = await api("/orgs"); } catch { } }
};

function showStep() {
  $$(".wstep").forEach(s => s.hidden = +s.dataset.step !== step);
  $$("#progress div").forEach((d, i) => d.classList.toggle("on", i <= step));
  $("#backBtn").textContent = step === 0 ? "Cancel" : "Back";
  $("#nextBtn").textContent = step === 3 ? "Submit report" : "Continue";
  $("#nextBtn").className = "btn " + (step === 3 ? "btn-red" : "btn-green");
  if (step === 3) { renderPick(); renderReview(); }
  showErr("#repErr", "");
}

function renderPick() {
  const want = checked("support").map(i => i.value);
  const svc = new Set(want.flatMap(w => META.supportToServices[w] || []));
  const st = $("#rState").value;
  const covers = o => !st || o.states.includes(META.allStates) || o.states.includes(st);
  let list = ORGS.filter(o => !svc.size || o.services.some(s => svc.has(s)));
  list.sort((a, b) => covers(b) - covers(a));
  list = list.slice(0, 4);
  if (pickedOrg && !list.some(o => o.id === pickedOrg)) { const p = ORGS.find(o => o.id === pickedOrg); if (p) list.unshift(p); }
  $("#pickOrgs").innerHTML = `<div class="org pick${!pickedOrg ? " sel" : ""}" data-pick="" tabindex="0" role="button"><h3>Don't send to anyone yet</h3><p>Keep the report private. You can share it later.</p></div>` +
    list.map(o => `<div class="org pick${pickedOrg === o.id ? " sel" : ""}" data-pick="${esc(o.id)}" tabindex="0" role="button">
      <div class="org-top">${avatar(o.initials, o.color)}<div><h3>${esc(o.name)}</h3><div class="loc">${esc(o.states.join(", "))}</div></div></div>
      <div class="tags">${o.services.map(s => `<span>${esc(s)}</span>`).join("")}</div></div>`).join("");
}
function pick(el) { pickedOrg = el.dataset.pick || null; renderPick(); renderReview(); }
$("#pickOrgs").addEventListener("click", e => { const c = e.target.closest("[data-pick]"); if (c) pick(c); });
$("#pickOrgs").addEventListener("keydown", e => { const c = e.target.closest("[data-pick]"); if (c && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); pick(c); } });

function renderReview() {
  const desc = $("#rDesc").value.trim();
  const rows = [
    ["What happened", checked("type").map(i => i.dataset.label).join(", ") || "Not given"],
    ["Where", (checked("place")[0]?.dataset.label || "Not given") + ($("#rState").value ? ", " + $("#rState").value : "")],
    ["When", $("#rDate").value || "Not given"], ["How often", $("#rFreq").value], ["Responsible", $("#rWho").value],
    ["Description", desc ? (desc.length > 120 ? desc.slice(0, 120) + "…" : desc) : "Not given"],
    ["Evidence", files.length ? files.length + " file(s)" : "None"],
    ["Identity", $("input[name=ident]:checked").value === "share" ? "Nickname shared with chosen organisation" : "Fully anonymous"],
    ["Sent to", pickedOrg ? (ORGS.find(o => o.id === pickedOrg)?.name || pickedOrg) : "No one yet"],
  ];
  $("#review").innerHTML = rows.map(([k, v]) => `<div><dt>${k}</dt><dd style="margin:0">${esc(v)}</dd></div>`).join("");
}

$("#backBtn").onclick = () => { if (step === 0) { location.hash = role() === "survivor" ? "dashboard" : "home"; return; } step--; showStep(); };
$("#nextBtn").onclick = () => {
  if (step === 0 && !checked("type").length) return showErr("#repErr", "Select at least one option, or choose the closest match.");
  if (step === 1 && !checked("place").length) return showErr("#repErr", "Choose where it happened. Pick 'Somewhere else' if none fit.");
  if (step < 3) { step++; showStep(); window.scrollTo({ top: 0, behavior: "smooth" }); return; }
  submitReport();
};

async function submitReport() {
  const ident = $("input[name=ident]:checked").value;
  if (ident === "share" && role() !== "survivor") {
    modal("Sharing a nickname needs an account", "You can go back and choose fully anonymous, or create an account first. Your answers here will be lost if you leave this page.",
      [["Choose anonymous", "btn-green", () => { $('input[name=ident][value=anonymous]').checked = true; renderReview(); }], ["Cancel", "btn-ghost"]]);
    return;
  }
  const btn = $("#nextBtn"); btn.disabled = true; btn.textContent = "Submitting…";
  try {
    const d = await api("/reports", {
      method: "POST", body: {
        types: checked("type").map(i => i.value), place: checked("place")[0]?.value, state: $("#rState").value || null,
        incidentDate: $("#rDate").value || null, frequency: $("#rFreq").value, responsible: $("#rWho").value,
        narrative: $("#rDesc").value, witnesses: $("#rWit").value, support: checked("support").map(i => i.value),
        identity: ident, orgId: pickedOrg,
      },
    });
    await refreshMe();
    $("#doneCode").textContent = d.code; $("#donePin").textContent = d.pin;
    $("#doneOrg").textContent = d.org ? `Shared with ${d.org}. Their replies will appear in your messages.` : "This report is private. You can share it with an organisation later.";
    $("#doneNext").href = "#case:" + d.code;
    $("#copyCode").onclick = () => copy(`SpeakSafe case code: ${d.code}  PIN: ${d.pin}`);
    $("#doneUpload").textContent = "";
    location.hash = "done";
    if (files.length) await uploadAll(d.code, files, $("#doneUpload"));
    files = [];
  } catch (e) { showErr("#repErr", e.message); }
  finally { btn.disabled = false; btn.textContent = "Submit report"; }
}

async function uploadAll(code, list, statusEl) {
  let okCount = 0;
  for (let i = 0; i < list.length; i++) {
    statusEl.textContent = `Uploading evidence ${i + 1} of ${list.length}…`;
    const fd = new FormData(); fd.append("file", list[i], list[i].name);
    try { await api(`/reports/${encodeURIComponent(code)}/evidence`, { method: "POST", form: fd }); okCount++; }
    catch (e) { statusEl.textContent = `${list[i].name}: ${e.message}`; return okCount; }
  }
  statusEl.textContent = `${okCount} file${okCount === 1 ? "" : "s"} uploaded and encrypted.`;
  return okCount;
}

// ---------- case detail (reporter and officer)
RENDER.case = async arg => {
  const code = arg || (role() === "tracker" ? ME.name : null);
  if (!code) { location.hash = homeFor(role()); return; }
  const body = $("#caseBody"); body.innerHTML = `<div class="loading">Loading…</div>`;
  let r;
  try { r = await api("/reports/" + encodeURIComponent(code)); }
  catch (e) { body.innerHTML = `<div class="card"><div class="err on">${esc(e.message)}</div></div>`; return; }
  const officer = role() === "officer";
  const done = new Set(r.events.map(e => e.kind));
  const steps = [["Submitted", "Report submitted"], ["Referred", "Sent to organisation"], ["InProgress", "Organisation is helping"], ["Closed", "Closed"]];
  const evtFor = k => r.events.filter(e => e.kind === k).pop();
  body.innerHTML = `
    <div class="dash-head">
      <div><h1 style="display:flex;gap:12px;align-items:center;flex-wrap:wrap">${esc(r.code)} ${pill(r.status, r.statusLabel)} ${r.urgent ? `<span class="pill p-urg"><i></i>Urgent</span>` : ""}</h1>
        <p>${officer ? esc(r.reporter) + " · " : ""}Made ${fmtDate(r.createdUtc)}${r.org ? " · " + esc(r.org.name) : ""}</p></div>
      <div class="row-actions" id="caseActions"></div>
    </div>
    <div class="case-layout">
      <section class="card stack">
        <div><h2>What happened</h2><div class="tags" style="margin-bottom:12px">${r.types.map(t => `<span>${esc(t)}</span>`).join("")}</div>
          ${r.narrative ? `<div class="narr">${esc(r.narrative)}</div>` : `<p style="color:var(--muted)">No description was written.</p>`}</div>
        <dl class="kv">
          <dt>Where</dt><dd>${esc(r.place)}${r.state ? ", " + esc(r.state) : ""}</dd>
          <dt>When</dt><dd>${r.incidentDate ? esc(fmtDate(r.incidentDate)) : "Not given"}</dd>
          <dt>How often</dt><dd>${esc(r.frequency)}</dd>
          <dt>Responsible</dt><dd>${esc(r.responsible)}</dd>
          <dt>Witnesses</dt><dd>${esc(r.witnesses)}</dd>
          <dt>Support wanted</dt><dd>${r.support.length ? esc(r.support.join(", ")) : "Not given"}</dd>
          <dt>Identity</dt><dd>${r.identity === "ShareNickname" ? "Nickname shared with the organisation" : "Fully anonymous"}</dd>
        </dl>
        <div><h2>Evidence</h2><div id="evList">${r.evidence.length ? r.evidence.map(e => `<div class="ev"><span>${esc(e.fileName)} · ${kb(e.size)}</span><a class="btn btn-ghost btn-sm" href="/api/v1/evidence/${e.id}">Download</a></div>`).join("") : `<p style="color:var(--muted)">No files.</p>`}</div>
          ${!officer && r.status !== "Closed" && r.evidence.length < META.maxEvidence ? `<label class="drop" for="caseFile" style="margin-top:10px;display:block">Add a file</label><input type="file" id="caseFile" hidden><p class="upload-progress" id="caseUpload"></p>` : ""}</div>
      </section>
      <section class="card">
        <h2>Progress</h2>
        <div class="vtl">${steps.map(([k, label]) => { const ev = evtFor(k); return `<div class="${done.has(k) ? "done" : ""}"><i></i><span><b>${label}</b><small>${ev ? esc(fmtTime(ev.utc)) + (ev.note ? " · " + esc(ev.note) : "") : ""}</small></span></div>`; }).join("")}</div>
        ${r.conversationId ? `<a class="btn btn-green" href="#messages:${r.conversationId}" style="width:100%;margin-top:6px">${officer ? "Message the reporter" : "Open conversation"}</a>` : ""}
      </section>
    </div>`;
  const actions = $("#caseActions");
  if (officer) {
    if (r.status === "Referred") actions.insertAdjacentHTML("beforeend", `<button class="btn btn-ghost" type="button" id="markProg">Mark in progress</button>`);
    if (r.status !== "Closed") actions.insertAdjacentHTML("beforeend", `<button class="btn btn-red" type="button" id="closeCase">Close case</button>`);
    $("#markProg")?.addEventListener("click", () => officerStatus(r.code, "InProgress"));
    $("#closeCase")?.addEventListener("click", () => officerStatus(r.code, "Closed"));
  } else {
    if (r.canShare) actions.insertAdjacentHTML("beforeend", `<button class="btn btn-green" type="button" id="shareBtn">Share with an organisation</button>`);
    if (r.canClose) actions.insertAdjacentHTML("beforeend", `<button class="btn btn-ghost" type="button" id="closeBtn">Close report</button>`);
    $("#shareBtn")?.addEventListener("click", () => shareDialog(r.code));
    $("#closeBtn")?.addEventListener("click", () => modal("Close this report?", "<p>Closed reports can still be read, but organisations can no longer reply.</p>",
      [["Close report", "btn-red", async () => { await api(`/reports/${encodeURIComponent(r.code)}/close`, { method: "POST" }); toast("Report closed."); RENDER.case(r.code); }], ["Cancel", "btn-ghost"]]));
    $("#caseFile")?.addEventListener("change", async e => {
      const f = e.target.files[0]; if (!f) return;
      await uploadAll(r.code, [f], $("#caseUpload")); RENDER.case(r.code);
    });
  }
};

function officerStatus(code, status) {
  const closing = status === "Closed";
  modal(closing ? "Close this case?" : "Mark in progress", `<div class="field"><label for="stNote">Note for the reporter (optional)</label><input type="text" id="stNote" maxlength="300" placeholder="${closing ? "For example: resolved with the employer" : "For example: case officer assigned"}"></div>`,
    [[closing ? "Close case" : "Save", closing ? "btn-red" : "btn-green", async () => {
      await api(`/org/cases/${encodeURIComponent(code)}/status`, { method: "POST", body: { status, note: $("#stNote").value } });
      toast("Case updated."); RENDER.case(code);
    }], ["Cancel", "btn-ghost"]]);
}

async function shareDialog(code, orgId) {
  if (!ORGS.length) ORGS = await api("/orgs");
  modal("Share with an organisation", `<p>They will see the report and can message you. Your identity stays as you chose.</p>
    <div class="field"><label for="shareOrg">Organisation</label><select id="shareOrg">${ORGS.map(o => `<option value="${esc(o.id)}"${o.id === orgId ? " selected" : ""}>${esc(o.name)}</option>`).join("")}</select></div>`,
    [["Share report", "btn-green", async () => {
      await api(`/reports/${encodeURIComponent(code)}/share`, { method: "POST", body: { orgId: $("#shareOrg").value } });
      toast("Report shared."); location.hash = "case:" + code; RENDER.case(code);
    }], ["Cancel", "btn-ghost"]]);
}

// ---------- organisations directory
RENDER.connect = async () => {
  const list = $("#orgList");
  try {
    const q = new URLSearchParams(); if ($("#fService").value) q.set("service", $("#fService").value); if ($("#fState").value) q.set("state", $("#fState").value);
    const orgs = await api("/orgs?" + q);
    if (!$("#fService").value && !$("#fState").value) ORGS = orgs;
    const canChat = !["officer", "admin"].includes(role());
    list.innerHTML = orgs.length ? orgs.map(o => `<article class="org">
      <div class="org-top">${avatar(o.initials, o.color)}<div><h3>${esc(o.name)}</h3><div class="loc">${esc(o.states.join(", "))}</div></div></div>
      ${o.demo ? `<div><span class="demo-tag">DEMO PARTNER</span></div>` : ""}
      <p>${esc(o.description)}</p>
      <div class="tags">${o.services.map(s => `<span>${esc(s)}</span>`).join("")}</div>
      ${canChat ? `<button class="btn btn-green btn-sm" type="button" data-connect="${esc(o.id)}">Start a private conversation</button>` : ""}
      <div class="meta"><span>${esc(o.responseNote)}</span><span>${esc(o.languages)}</span></div>
    </article>`).join("") : `<div class="empty" style="grid-column:1/-1">No organisations match those filters. Try "All states".</div>`;
  } catch (e) { list.innerHTML = `<div class="err on">${esc(e.message)}</div>`; }
};
$("#fService").onchange = () => RENDER.connect();
$("#fState").onchange = () => RENDER.connect();
$("#orgList").addEventListener("click", e => {
  const b = e.target.closest("[data-connect]"); if (!b) return;
  const id = b.dataset.connect;
  if (role() === "survivor") return startConversation(id);
  if (role() === "tracker") return shareDialog(ME.name, id);
  pendingConnect = id;
  modal("How would you like to reach them?", "<p>Create a private account to chat under a nickname, or make an anonymous report and send it to this organisation.</p>",
    [["Report anonymously", "btn-red", () => { preselectOrg = id; pendingConnect = null; location.hash = "report"; }],
     ["Create account", "btn-green", () => { location.hash = "register"; }],
     ["Sign in", "btn-ghost", () => { pendingTab = "survivor"; location.hash = "signin"; }]]);
});

async function startConversation(orgId) {
  try { const d = await api("/conversations", { method: "POST", body: { orgId } }); location.hash = "messages:" + d.id; }
  catch (e) { toast(e.message); }
}

// ---------- messages
let chatState = { id: null, lastId: 0 };

RENDER.messages = async arg => {
  const r = role();
  $("#newConv").hidden = r !== "survivor";
  $("#msgSub").textContent = r === "officer" ? "Conversations with reporters. They stay anonymous unless they chose a nickname." : "Private conversations with the organisations you contacted.";
  const initial = arg ? +arg : null;
  chatState = { id: initial, lastId: 0 };
  if (initial) await openChat(initial);
  else { $("#chat").innerHTML = `<div class="empty" style="margin:20px">${r === "survivor" ? `Choose a conversation, or <a href="#connect">find an organisation</a>.` : "Choose a conversation."}</div>`; await loadThreads(); }
  timers.push(setInterval(pollChat, 4000));
  timers.push(setInterval(loadThreads, 15000));
};

async function loadThreads() {
  try {
    const convs = await api("/conversations");
    if (!chatState.id && convs.length && window.innerWidth > 800) { chatState.id = convs[0].id; openChat(chatState.id); }
    $("#msgList").innerHTML = convs.length ? convs.map(c => threadMini(c, chatState.id)).join("") : `<div class="empty">No conversations yet.</div>`;
  } catch (e) { $("#msgList").innerHTML = `<div class="err on">${esc(e.message)}</div>`; }
}

async function openChat(id) {
  const convs = await api("/conversations");
  const c = convs.find(x => x.id === id);
  if (!c) { $("#chat").innerHTML = `<div class="empty" style="margin:20px">Conversation not found.</div>`; return; }
  chatState = { id, lastId: 0 };
  const officer = role() === "officer";
  $("#chat").innerHTML = `<div class="chat-head">${avatar(c.orgInitials, c.orgColor)}<div><b>${esc(officer ? c.with : c.orgName)}</b><br><small>${c.reportCode ? `<a href="#case:${esc(c.reportCode)}">Case ${esc(c.reportCode)}</a>` : "General conversation"}</small></div></div>
    <div class="bubbles" id="bubbles"></div>
    <form class="composer" id="composer"><label for="msgIn" style="position:absolute;left:-9999px">Message</label><input type="text" id="msgIn" placeholder="Write a message…" autocomplete="off" maxlength="2000"><button class="btn btn-green" type="submit">Send</button></form>`;
  $("#composer").addEventListener("submit", async e => {
    e.preventDefault();
    const v = $("#msgIn").value.trim(); if (!v) return;
    $("#msgIn").value = "";
    try { await api(`/conversations/${id}/messages`, { method: "POST", body: { text: v } }); while (polling) await new Promise(r => setTimeout(r, 50)); await pollChat(); }
    catch (err) { $("#msgIn").value = v; toast(err.message); }
  });
  await pollChat();
  await loadThreads();
  $("#msgIn")?.focus();
}

let polling = false;
async function pollChat() {
  if (polling || !chatState.id || !$("#bubbles")) return;
  const id = chatState.id;
  let msgs;
  polling = true;
  try { msgs = await api(`/conversations/${id}/messages?after=${chatState.lastId}`); } catch { return; } finally { polling = false; }
  msgs = msgs.filter(m => m.id > chatState.lastId);
  if (id !== chatState.id || !msgs.length) return;
  const mine = role() === "officer" ? "Officer" : "Survivor";
  const box = $("#bubbles");
  box.insertAdjacentHTML("beforeend", msgs.map(m => {
    const cls = m.from === "System" ? "sys" : m.from === mine ? "me" : "them";
    return `<div class="bub ${cls}">${cls === "them" ? `<small style="margin:0 0 3px;opacity:.8">${esc(m.who)}</small>` : ""}${esc(m.text)}${cls !== "sys" ? `<small>${esc(fmtTime(m.utc))}</small>` : ""}</div>`;
  }).join(""));
  chatState.lastId = msgs[msgs.length - 1].id;
  box.scrollTop = box.scrollHeight;
}

// ---------- organisation portal
let orgFilter = "open";
RENDER.org = async () => {
  $("#orgTitle").textContent = ME.org ? ME.org + " cases" : "Cases";
  await loadCases();
  timers.push(setInterval(loadCases, 20000));
};
$("#orgTabs").addEventListener("click", e => {
  const b = e.target.closest("[data-f]"); if (!b) return;
  orgFilter = b.dataset.f; $$("#orgTabs [data-f]").forEach(x => x.setAttribute("aria-selected", x === b)); loadCases();
});
async function loadCases() {
  try {
    const cases = await api("/org/cases");
    $("#oNew").textContent = cases.filter(c => c.status === "Referred").length;
    $("#oOpen").textContent = cases.filter(c => c.status === "InProgress").length;
    $("#oClosed").textContent = cases.filter(c => c.status === "Closed").length;
    const list = cases.filter(c => orgFilter === "all" || (orgFilter === "closed" ? c.status === "Closed" : c.status !== "Closed"))
      .sort((a, b) => (b.urgent - a.urgent) || ((b.status === "Referred") - (a.status === "Referred")) || (new Date(b.updatedUtc) - new Date(a.updatedUtc)));
    $("#orgCases").innerHTML = list.length ? list.map(c => `<a class="case-row${c.urgent && c.status !== "Closed" ? " urgent" : ""}" href="#case:${esc(c.code)}" style="text-decoration:none;color:inherit">
      <span class="case-id">${esc(c.code)}</span>
      <span><span class="tags">${c.types.map(t => `<span>${esc(t)}</span>`).join("")}<span>${esc(c.place)}</span>${c.state ? `<span>${esc(c.state)}</span>` : ""}</span>
        <span class="who">${esc(c.reporter)} · updated ${esc(fmtTime(c.updatedUtc))}</span></span>
      <span style="display:flex;gap:8px;align-items:center">${c.unread ? `<span class="badge" title="Unread messages">${c.unread}</span>` : ""}${pill(c.status, c.statusLabel)}</span>
    </a>`).join("") : `<div class="empty">No ${orgFilter === "all" ? "" : orgFilter + " "}cases.</div>`;
  } catch (e) { $("#orgCases").innerHTML = `<div class="err on">${esc(e.message)}</div>`; }
}

// ---------- coordinator
let OVERVIEW = null;
RENDER.admin = async () => {
  try { OVERVIEW = await api("/admin/overview"); } catch (e) { toast(e.message); return; }
  const o = OVERVIEW;
  $("#aReports").textContent = o.reports; $("#aUrgent").textContent = o.urgent;
  $("#aOrgs").textContent = o.organisations.filter(x => x.org.isActive).length;
  const bars = (obj, labels) => { const max = Math.max(1, ...Object.values(obj)); return Object.entries(obj).map(([k, v]) => `<div class="bar-row"><span>${esc(labels?.[k] || k)}</span><span class="meter"><span class="fill" style="display:block;width:${v / max * 100}%"></span></span><b>${v}</b></div>`).join(""); };
  $("#aTypes").innerHTML = bars(o.byType);
  $("#aStatus").innerHTML = bars(o.byStatus, { Referred: "Sent to organisation", InProgress: "Organisation is helping" });
  $("#aPlace").innerHTML = bars(o.byPlace);
  $("#aOrgRows").innerHTML = o.organisations.map(x => `<tr>
    <td><div style="display:flex;gap:10px;align-items:center">${avatar(x.org.initials, x.org.color)}<div><b>${esc(x.org.name)}</b>${x.org.demo ? ` <span class="demo-tag">DEMO</span>` : ""}<br><small style="color:var(--muted)">${esc(x.org.states.join(", "))}</small></div></div></td>
    <td>${esc(x.org.services.join(", "))}</td><td>${x.officers}</td><td>${x.openCases}</td><td>${x.closedCases}</td>
    <td>${x.org.isActive ? `<span class="pill p-ref"><i></i>Active</span>` : `<span class="pill p-sub"><i></i>Paused</span>`}</td>
    <td><div class="row-actions"><button class="btn btn-ghost btn-sm" type="button" data-edit="${esc(x.org.id)}">Edit</button><button class="btn btn-ghost btn-sm" type="button" data-officers="${esc(x.org.id)}">Officers</button></div></td>
  </tr>`).join("");
};
$("#addOrgBtn").onclick = () => orgEditor(null);
$("#aOrgRows").addEventListener("click", e => {
  const ed = e.target.closest("[data-edit]"); if (ed) return orgEditor(OVERVIEW.organisations.find(x => x.org.id === ed.dataset.edit).org);
  const of = e.target.closest("[data-officers]"); if (of) return officersDialog(OVERVIEW.organisations.find(x => x.org.id === of.dataset.officers).org);
});

function orgEditor(o) {
  const isNew = !o;
  o = o || { id: "", name: "", initials: "", color: "#0b7a4b", services: [], states: [], description: "", responseNote: "", languages: "English", isActive: true };
  const ms = (name, items, sel) => `<div class="ms">${items.map(i => `<label><input type="checkbox" name="${name}" value="${esc(i)}"${sel.includes(i) ? " checked" : ""}>${esc(i)}</label>`).join("")}</div>`;
  modal(isNew ? "Add organisation" : "Edit " + o.name, `
    <div class="grid2"><div class="field"><label for="eoId">Short ID</label><input type="text" id="eoId" value="${esc(o.id)}" ${isNew ? "" : "disabled"} placeholder="kano-legal"></div>
      <div class="field"><label for="eoColor">Colour</label><input type="text" id="eoColor" value="${esc(o.color)}"></div></div>
    <div class="field"><label for="eoName">Name</label><input type="text" id="eoName" value="${esc(o.name)}" maxlength="150"></div>
    <div class="field"><span class="flabel">Services</span>${ms("eoSvc", META.services, o.services)}</div>
    <div class="field"><span class="flabel">States covered</span>${ms("eoSt", [META.allStates, ...META.states], o.states)}</div>
    <div class="field"><label for="eoDesc">Description</label><textarea id="eoDesc" maxlength="500" style="min-height:80px">${esc(o.description)}</textarea></div>
    <div class="grid2"><div class="field"><label for="eoResp">Response time</label><input type="text" id="eoResp" value="${esc(o.responseNote)}" maxlength="80"></div>
      <div class="field"><label for="eoLang">Languages</label><input type="text" id="eoLang" value="${esc(o.languages)}" maxlength="120"></div></div>
    <label class="check"><input type="checkbox" id="eoActive"${o.isActive ? " checked" : ""}> Active (visible to reporters, officers can sign in)</label>`,
    [["Save", "btn-green", async () => {
      await api("/admin/orgs", {
        method: "POST", body: {
          id: $("#eoId").value.trim(), name: $("#eoName").value.trim(), initials: o.initials, color: $("#eoColor").value.trim(),
          services: checked("eoSvc").map(i => i.value), states: checked("eoSt").map(i => i.value),
          description: $("#eoDesc").value, responseNote: $("#eoResp").value, languages: $("#eoLang").value, isActive: $("#eoActive").checked,
        },
      });
      ORGS = []; toast("Organisation saved."); RENDER.admin();
    }], ["Cancel", "btn-ghost"]], true);
}

async function officersDialog(org) {
  let list = [];
  try { list = await api(`/admin/orgs/${encodeURIComponent(org.id)}/officers`); } catch (e) { toast(e.message); return; }
  modal("Officers · " + org.name, `
    <div id="offList">${list.length ? list.map(o => `<div class="ev"><span><b style="color:var(--ink)">${esc(o.name)}</b> · ${esc(o.username)}</span>
      <button class="btn btn-ghost btn-sm" type="button" data-toggle="${o.id}" data-active="${o.isActive}">${o.isActive ? "Disable" : "Enable"}</button></div>`).join("") : "<p>No officers yet.</p>"}</div>
    <div class="grid2"><div class="field"><label for="noName">Name</label><input type="text" id="noName" maxlength="100"></div>
      <div class="field"><label for="noUser">Username</label><input type="text" id="noUser" maxlength="40" placeholder="name.org"></div></div>`,
    [["Add officer", "btn-green", async () => {
      const d = await api(`/admin/orgs/${encodeURIComponent(org.id)}/officers`, { method: "POST", body: { name: $("#noName").value, username: $("#noUser").value } });
      modal("Officer created", `<p>Give ${esc(d.name)} these sign-in details privately. The passphrase is shown once.</p><div class="secret">${esc(d.username)}<br>${esc(d.passphrase)}</div>`,
        [["Copy", "btn-ghost", () => { copy(`Username: ${d.username}  Passphrase: ${d.passphrase}`); return false; }], ["Done", "btn-green", () => RENDER.admin()]]);
      return false;
    }], ["Close", "btn-ghost", () => RENDER.admin()]], true);
  $("#offList").addEventListener("click", async e => {
    const b = e.target.closest("[data-toggle]"); if (!b) return;
    try { await api(`/admin/officers/${b.dataset.toggle}/active`, { method: "POST", body: { active: b.dataset.active !== "true" } }); officersDialog(org); }
    catch (err) { showErr("#modalErr", err.message); }
  });
}

// ------------------------------------------------------------------ quick exit
function quickExit() {
  try { navigator.sendBeacon("/api/v1/auth/logout"); } catch { }
  document.title = "Easy Jollof Rice";
  $("#decoy").classList.add("on");
  $$(".view,header,.strip,.portal-strip,.exit,.modal-bg").forEach(el => el.style.display = "none");
  ME = null;
  window.location.replace("https://www.google.com/search?q=jollof+rice+recipe");
}
$("#quickExit").onclick = quickExit;
let lastEsc = 0;
document.addEventListener("keydown", e => {
  if (e.key !== "Escape") return;
  if ($("#modal").classList.contains("on")) { closeModal(); return; }
  const t = Date.now(); if (t - lastEsc < 600) quickExit(); lastEsc = t;
});

// ------------------------------------------------------------------ start
(async function start() {
  try {
    [META, ME] = await Promise.all([api("/meta"), api("/me")]);
    buildForms();
  } catch (e) {
    toast(e.message);
  }
  renderAuth();
  route();
})();
