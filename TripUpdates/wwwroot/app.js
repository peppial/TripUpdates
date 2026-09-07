const REFRESH_MS = 30000;
const SETTINGS_KEY = "tripupdates.settings";
const LAST_GOOD_KEY = "tripupdates.lastGood";

const el = {
  directions: document.getElementById("directions"),
  banner: document.getElementById("banner"),
  lineName: document.getElementById("line-name"),
  stopName: document.getElementById("stop-name"),
  refresh: document.getElementById("refresh"),
  dialog: document.getElementById("settings"),
  form: document.getElementById("settings-form"),
  line: document.getElementById("setting-line"),
  stop: document.getElementById("setting-stop"),
};

// The last successful payload is kept in storage, not just in memory: opening the app cold
// at the stop with no signal should still show the last known times rather than a spinner.
let lastGood = readLastGood();
let timer = null;

function readLastGood() {
  try { return JSON.parse(localStorage.getItem(LAST_GOOD_KEY)); }
  catch { return null; }
}

function writeLastGood(data) {
  try { localStorage.setItem(LAST_GOOD_KEY, JSON.stringify(data)); } catch { /* private mode */ }
}

const readSettings = () => {
  try { return JSON.parse(localStorage.getItem(SETTINGS_KEY)) || {}; }
  catch { return {}; }
};
const writeSettings = (s) => {
  try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(s)); } catch { /* private mode */ }
};

function arrivalsUrl() {
  const { line, stop } = readSettings();
  const params = new URLSearchParams();
  if (line) params.set("line", line);
  if (stop) params.set("stop", stop);
  const query = params.toString();
  return query ? `/api/arrivals?${query}` : "/api/arrivals";
}

function showBanner(text, kind) {
  el.banner.textContent = text;
  el.banner.className = `banner ${kind || ""}`.trim();
  el.banner.hidden = !text;
}

function renderDirections(data, stale) {
  el.directions.replaceChildren(...data.directions.map((d) => {
    const card = document.createElement("section");
    card.className = "card";
    if (stale) card.classList.add("stale");

    const label = document.createElement("p");
    label.className = "label";
    label.textContent = `${data.line} ${d.label}`;

    const value = document.createElement("p");
    value.className = "value";
    const minutes = document.createElement("span");
    minutes.className = "minutes";

    if (!d.hasArrival) {
      card.classList.add("none");
      minutes.textContent = "няма курсове";
    } else if (d.minutes === 0) {
      card.classList.add("now");
      minutes.textContent = "пристига сега";
    } else {
      if (d.minutes <= 3) card.classList.add("soon");
      minutes.textContent = String(d.minutes);
    }
    value.append(minutes);

    if (d.hasArrival) {
      // The bus after next rides along in a smaller size: "4, 65 минути".
      if (d.thenMinutes != null) {
        const then = document.createElement("span");
        then.className = "then";
        then.textContent = `, ${d.thenMinutes}`;
        value.append(then);
      }
      if (d.minutes !== 0 || d.thenMinutes != null) {
        const unit = document.createElement("span");
        unit.className = "unit";
        unit.textContent = d.minutes === 1 && d.thenMinutes == null ? "минута" : "минути";
        value.append(unit);
      }
    }

    // The full sentence is what a screen reader announces.
    card.setAttribute("aria-label", d.message);
    card.append(label, value);

    // Timetable times are not a live prediction, and saying so is the difference between
    // "the bus is 57 minutes away" and "the bus is meant to be 57 minutes away".
    if (d.scheduled) card.classList.add("scheduled");
    if (d.scheduled || d.thenScheduled) {
      const note = document.createElement("p");
      note.className = "note";
      note.textContent = d.scheduled ? "по разписание" : "вторият е по разписание";
      card.append(note);
    }

    return card;
  }));
  el.directions.setAttribute("aria-busy", "false");
}

function render(data, { offline } = {}) {
  el.lineName.textContent = data.line;
  el.stopName.textContent = data.stop;
  renderDirections(data, data.stale || offline);

  if (offline) showBanner("Няма връзка с приложението — показаното е последното известно.", "error");
  else if (data.error) showBanner("Проблем с връзката към ЦГМ. Показва се последното известно.", "warn");
  else if (data.stale) showBanner("Данните не са обновявани скоро.", "warn");
  else showBanner("");
}

async function load() {
  try {
    const response = await fetch(arrivalsUrl(), { cache: "no-store" });
    if (response.status === 404) {
      const problem = await response.json().catch(() => ({}));
      showBanner(problem.detail || "Неразпозната линия или спирка.", "error");
      el.directions.setAttribute("aria-busy", "false");
      return;
    }
    if (!response.ok) throw new Error(`HTTP ${response.status}`);

    const data = await response.json();
    lastGood = data;
    writeLastGood(data);
    render(data);
  } catch {
    if (lastGood) {
      render(lastGood, { offline: true });
    } else {
      showBanner("Няма връзка с приложението.", "error");
      el.directions.setAttribute("aria-busy", "false");
    }
  }
}

function schedule() {
  clearInterval(timer);
  timer = setInterval(load, REFRESH_MS);
}

// Android throttles timers in the background, so a reopened app would otherwise show a
// stale number until the next tick. Refetch as soon as the app becomes visible again.
document.addEventListener("visibilitychange", () => {
  if (document.visibilityState !== "visible") return;
  load();
  schedule();
});

el.refresh.addEventListener("click", () => { load(); schedule(); });

document.getElementById("settings-open").addEventListener("click", () => {
  const current = readSettings();
  el.line.value = current.line || (lastGood?.line ?? "");
  el.stop.value = current.stop || (lastGood?.stop ?? "");
  el.dialog.showModal();
});

el.form.addEventListener("submit", (event) => {
  if (event.submitter?.value !== "save") return;
  writeSettings({ line: el.line.value.trim(), stop: el.stop.value.trim() });
  el.directions.setAttribute("aria-busy", "true");
  load();
  schedule();
});

if (lastGood) render(lastGood, { offline: true });
load();
schedule();

if ("serviceWorker" in navigator) {
  window.addEventListener("load", () => navigator.serviceWorker.register("/sw.js").catch(() => {}));
}
