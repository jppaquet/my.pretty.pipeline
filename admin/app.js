// Admin SPA logic. Concerns:
//   1. MSAL.js redirect-flow sign-in against the Entra app registration in
//      window.__ADMIN_CONFIG__ (rendered from config.template.js at deploy).
//   2. Calling /v1/admin/* on the Function App with a Bearer access token.
//   3. Two tabs: testers (allowlist) and projects (producer keys).
//
// Vanilla JS, no build step. Each tab is one table + the small bit of logic
// that drives it; a framework would dwarf the actual code.

(function () {
  const cfg = window.__ADMIN_CONFIG__;
  if (!cfg || !cfg.tenantId || !cfg.clientId || !cfg.apiBase) {
    document.body.innerHTML =
      "<p style='padding:2rem'>config.js missing or unrendered — cd-deploy " +
      "didn't substitute the placeholders. Check the publish-admin-swa job.</p>";
    return;
  }

  // The UMD bundle exposes itself as `window.msal`, not `window.msalBrowser`
  // — verified against the v3.20.0 minified source. Naming the
  // PublicClientApplication instance `pca` avoids shadowing the namespace.
  const pca = new msal.PublicClientApplication({
    auth: {
      clientId: cfg.clientId,
      authority: "https://login.microsoftonline.com/" + cfg.tenantId,
      redirectUri: window.location.origin,
    },
    cache: {
      // Session storage — the token survives a page refresh but not a tab
      // close. Localstorage would cache MFA across tabs but persists longer
      // than we want for an admin surface.
      cacheLocation: "sessionStorage",
    },
  });

  // App-id-uri scope. The Entra app registration exposes `access_as_user`
  // (minted by FORK-SETUP §7's bootstrap). We must request that explicit
  // scope by URI — not `${api://<clientId>}/.default` — because when the
  // SPA's client and the API's resource are the same app registration,
  // Entra rejects `.default` with AADSTS90009 ("Application is requesting
  // a token for itself"). The `.default` shortcut is for client-credentials
  // flows where client ≠ resource. The token returned still carries the
  // user's `roles` claim, which is what AdminAuthMiddleware checks.
  const scopes = ["api://" + cfg.clientId + "/access_as_user"];

  let account = null;

  // ── elements ───────────────────────────────────────────────────────
  const $ = (id) => document.getElementById(id);
  const signinSection = $("signin-section");
  const contentSection = $("content-section");
  const userLabel = $("user-label");

  // ── sign-in ────────────────────────────────────────────────────────
  async function init() {
    await pca.initialize();
    const result = await pca.handleRedirectPromise();
    account = (result && result.account) || pca.getAllAccounts()[0] || null;
    render();
    if (account) {
      await loadAllowlist();
      await loadProjects();
    }
  }

  function render() {
    if (account) {
      userLabel.textContent = account.username;
      $("signout-btn").hidden = false;
      signinSection.hidden = true;
      contentSection.hidden = false;
    } else {
      userLabel.textContent = "";
      $("signout-btn").hidden = true;
      signinSection.hidden = false;
      contentSection.hidden = true;
    }
  }

  $("signin-btn").addEventListener("click", () =>
    pca.loginRedirect({ scopes }));
  $("signout-btn").addEventListener("click", () =>
    pca.logoutRedirect({ postLogoutRedirectUri: window.location.origin }));

  // ── tab switcher ────────────────────────────────────────────────────
  document.querySelectorAll(".tab").forEach((btn) => {
    btn.addEventListener("click", () => {
      const target = btn.dataset.tab;
      document.querySelectorAll(".tab").forEach((b) =>
        b.classList.toggle("active", b === btn));
      document.querySelectorAll(".tab-panel").forEach((p) =>
        p.hidden = p.dataset.panel !== target);
    });
  });

  // ── token acquisition ──────────────────────────────────────────────
  async function getToken() {
    try {
      const r = await pca.acquireTokenSilent({ account, scopes });
      return r.accessToken;
    } catch (e) {
      await pca.acquireTokenRedirect({ scopes });
      return null;
    }
  }

  async function api(path, method = "GET", body = null) {
    const token = await getToken();
    if (!token) return null;
    const init = { method, headers: { Authorization: "Bearer " + token } };
    if (body !== null) {
      init.headers["content-type"] = "application/json";
      init.body = JSON.stringify(body);
    }
    const r = await fetch(cfg.apiBase + path, init);
    const text = await r.text();
    if (!r.ok) {
      // Try to surface server-side error JSON; fall back to status line.
      try {
        const j = JSON.parse(text);
        throw new Error(j.error || (j.errors && j.errors[0]?.message) || ("HTTP " + r.status));
      } catch {
        throw new Error("HTTP " + r.status + (text ? ": " + text : ""));
      }
    }
    return text ? JSON.parse(text) : null;
  }

  function setStatus(elId, msg, error = false) {
    const el = $(elId);
    el.textContent = msg;
    el.classList.toggle("error", error);
  }

  // ── allowlist tab ──────────────────────────────────────────────────
  const allowlistBody = document.querySelector("#allowlist-table tbody");
  $("allowlist-refresh-btn").addEventListener("click", loadAllowlist);

  async function loadAllowlist() {
    setStatus("allowlist-status", "loading…");
    try {
      const data = await api("/v1/admin/allowlist");
      renderAllowlist(data.items || []);
      setStatus("allowlist-status", (data.items?.length || 0) + " rows");
    } catch (e) {
      setStatus("allowlist-status", e.message, true);
    }
  }

  function renderAllowlist(rows) {
    allowlistBody.innerHTML = "";
    if (rows.length === 0) {
      allowlistBody.innerHTML =
        "<tr><td colspan='5' class='muted'>no testers have signed in yet</td></tr>";
      return;
    }
    for (const row of rows) {
      const tr = document.createElement("tr");
      tr.innerHTML =
        "<td class='sub'>" + escapeHtml(row.sub) + "</td>" +
        "<td>" + (row.approved
          ? "<span class='badge approved'>approved</span>"
          : "<span class='badge pending'>pending</span>") + "</td>" +
        "<td class='muted'>" + fmt(row.firstSeenAt) + "</td>" +
        "<td class='muted'>" + (row.approvedAt ? fmt(row.approvedAt) : "—") + "</td>" +
        "<td></td>";
      const btn = document.createElement("button");
      if (row.approved) {
        btn.textContent = "revoke";
        btn.className = "danger";
        btn.onclick = () => mutateAllowlist(row.sub, "revoke", btn);
      } else {
        btn.textContent = "approve";
        btn.onclick = () => mutateAllowlist(row.sub, "approve", btn);
      }
      tr.querySelector("td:last-child").appendChild(btn);
      allowlistBody.appendChild(tr);
    }
  }

  async function mutateAllowlist(sub, op, btn) {
    btn.disabled = true;
    setStatus("allowlist-status", op + " " + sub + "…");
    try {
      await api("/v1/admin/allowlist/" + encodeURIComponent(sub) + "/" + op, "POST");
      await loadAllowlist();
    } catch (e) {
      setStatus("allowlist-status", e.message, true);
      btn.disabled = false;
    }
  }

  // ── projects tab ───────────────────────────────────────────────────
  const projectsBody = document.querySelector("#projects-table tbody");
  $("projects-refresh-btn").addEventListener("click", loadProjects);
  $("project-mint-btn").addEventListener("click", openMintDialog);

  async function loadProjects() {
    setStatus("projects-status", "loading…");
    try {
      const data = await api("/v1/admin/projects");
      renderProjects(data.items || []);
      setStatus("projects-status", (data.items?.length || 0) + " projects");
    } catch (e) {
      setStatus("projects-status", e.message, true);
    }
  }

  function renderProjects(rows) {
    projectsBody.innerHTML = "";
    if (rows.length === 0) {
      projectsBody.innerHTML =
        "<tr><td colspan='4' class='muted'>no projects yet — mint one to start producing</td></tr>";
      return;
    }
    for (const row of rows) {
      const tr = document.createElement("tr");
      tr.innerHTML =
        "<td class='sub'>" + escapeHtml(row.id) + "</td>" +
        "<td>" + escapeHtml(row.displayName) + "</td>" +
        "<td>" + (row.active
          ? "<span class='badge approved'>active</span>"
          : "<span class='badge pending'>revoked</span>") + "</td>" +
        "<td></td>";
      const action = tr.querySelector("td:last-child");
      if (row.active) {
        const btn = document.createElement("button");
        btn.textContent = "revoke";
        btn.className = "danger";
        btn.onclick = () => revokeProject(row.id, btn);
        action.appendChild(btn);
      } else {
        action.innerHTML = "<span class='muted'>—</span>";
      }
      projectsBody.appendChild(tr);
    }
  }

  async function revokeProject(id, btn) {
    if (!confirm("Revoke producer project '" + id + "'? Future requests with its key will return 401.")) return;
    btn.disabled = true;
    setStatus("projects-status", "revoking " + id + "…");
    try {
      await api("/v1/admin/projects/" + encodeURIComponent(id) + "/revoke", "POST");
      await loadProjects();
    } catch (e) {
      setStatus("projects-status", e.message, true);
      btn.disabled = false;
    }
  }

  // ── mint dialog ────────────────────────────────────────────────────
  const mintDialog = $("mint-dialog");
  const mintFormStep = $("mint-form-step");
  const mintResultStep = $("mint-result-step");
  const mintIdInput = $("mint-id");
  const mintNameInput = $("mint-name");
  const mintErrorEl = $("mint-error");
  const mintKeyEl = $("mint-key-value");
  const mintConfirmEl = $("mint-stored-confirm");
  const mintDoneBtn = $("mint-done-btn");

  function openMintDialog() {
    mintFormStep.hidden = false;
    mintResultStep.hidden = true;
    mintIdInput.value = "";
    mintNameInput.value = "";
    mintErrorEl.textContent = "";
    mintConfirmEl.checked = false;
    mintDoneBtn.disabled = true;
    mintDialog.showModal();
    mintIdInput.focus();
  }

  $("mint-cancel-btn").addEventListener("click", () => mintDialog.close());

  $("mint-submit-btn").addEventListener("click", async () => {
    mintErrorEl.textContent = "";
    const projectId = mintIdInput.value.trim();
    const displayName = mintNameInput.value.trim();
    if (!projectId || !displayName) {
      mintErrorEl.textContent = "both fields are required";
      return;
    }
    try {
      const data = await api("/v1/admin/projects", "POST", { projectId, displayName });
      mintKeyEl.textContent = data.key;
      mintFormStep.hidden = true;
      mintResultStep.hidden = false;
    } catch (e) {
      mintErrorEl.textContent = e.message;
    }
  });

  $("mint-copy-btn").addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(mintKeyEl.textContent);
      $("mint-copy-btn").textContent = "copied!";
      setTimeout(() => $("mint-copy-btn").textContent = "copy", 1500);
    } catch {
      // Older browsers / locked-down clipboard policy — fall back to selecting
      // so the operator can hit cmd-C themselves.
      const range = document.createRange();
      range.selectNodeContents(mintKeyEl);
      const sel = window.getSelection();
      sel.removeAllRanges();
      sel.addRange(range);
    }
  });

  mintConfirmEl.addEventListener("change", () => {
    mintDoneBtn.disabled = !mintConfirmEl.checked;
  });

  mintDoneBtn.addEventListener("click", async () => {
    mintDialog.close();
    await loadProjects();
  });

  // ── send-test tab ──────────────────────────────────────────────────
  // Posts directly to /v1/notifications with x-api-key. We deliberately
  // *don't* proxy this through an admin endpoint — the point of the smoke
  // test is to exercise the producer-auth boundary too. The Function App
  // CORS rule lets the SWA origin through and echoes requested headers on
  // preflight, so x-api-key works without further config.
  //
  // Project id round-trips through localStorage so the operator doesn't
  // re-type it on every visit. The key never touches storage.
  const ST_LS_KEY = "send-test.projectId";
  const stProjectId = $("st-project-id");
  const stKey = $("st-key");
  const stType = $("st-type");
  const stPriority = $("st-priority");
  const stTitle = $("st-title");
  const stBody = $("st-body");
  const stTags = $("st-tags");
  const stResult = $("st-result");

  stProjectId.value = localStorage.getItem(ST_LS_KEY) || "";
  stProjectId.addEventListener("change", () => {
    if (stProjectId.value) localStorage.setItem(ST_LS_KEY, stProjectId.value);
    else localStorage.removeItem(ST_LS_KEY);
  });

  $("st-send-btn").addEventListener("click", async (e) => {
    e.preventDefault();
    setStatus("st-status", "");
    stResult.hidden = true;
    stResult.classList.remove("error");

    const projectId = stProjectId.value.trim();
    const key = stKey.value.trim();
    const title = stTitle.value.trim();
    const body = stBody.value.trim();
    if (!projectId || !key || !title || !body) {
      setStatus("st-status", "all required fields must be set", true);
      return;
    }
    if (!key.startsWith("npk_")) {
      setStatus("st-status", "producer key must start with npk_", true);
      return;
    }

    const tags = stTags.value
      .split(",")
      .map((t) => t.trim())
      .filter(Boolean);

    // Wrap the notification payload in a CloudEvents 1.0 structured envelope.
    // `source` identifies the producer project (also used by the server for
    // the api-key project lookup); the payload itself goes inside `data`. The
    // server mints a fresh internal id, so the envelope `id` here is just for
    // CE compliance.
    const envelope = {
      specversion: "1.0",
      type: "notify.created.v1",
      source: projectId,
      id: crypto.randomUUID(),
      time: new Date().toISOString(),
      datacontenttype: "application/json",
      data: {
        type: stType.value,
        title,
        body,
        priority: stPriority.value,
        tags,
      },
    };

    $("st-send-btn").disabled = true;
    setStatus("st-status", "sending…");
    try {
      const r = await fetch(cfg.apiBase + "/v1/notifications", {
        method: "POST",
        headers: {
          "content-type": "application/cloudevents+json",
          "x-api-key": key,
        },
        body: JSON.stringify(envelope),
      });
      const text = await r.text();
      stResult.hidden = false;
      stResult.textContent = "HTTP " + r.status + "\n" + (text || "(empty body)");
      if (r.ok) {
        setStatus("st-status", "sent");
      } else {
        stResult.classList.add("error");
        setStatus("st-status", "HTTP " + r.status, true);
      }
    } catch (err) {
      setStatus("st-status", err.message, true);
    } finally {
      $("st-send-btn").disabled = false;
    }
  });

  // ── helpers ─────────────────────────────────────────────────────────
  function fmt(iso) {
    if (!iso) return "—";
    return new Date(iso).toLocaleString();
  }

  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, (c) => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
    })[c]);
  }

  init();
})();
