// Admin SPA logic. Three concerns:
//   1. MSAL.js redirect-flow sign-in against the Entra app registration in
//      window.__ADMIN_CONFIG__ (rendered from config.template.js at deploy).
//   2. Calling /admin/* on the Function App with a Bearer access token.
//   3. Rendering the allowlist + handling approve/revoke buttons.
//
// Vanilla JS on purpose — no build step, no framework. The whole UI is one
// table; framework overhead would dwarf the actual logic.

(function () {
  const cfg = window.__ADMIN_CONFIG__;
  if (!cfg || !cfg.tenantId || !cfg.clientId || !cfg.apiBase) {
    document.body.innerHTML =
      "<p style='padding:2rem'>config.js missing or unrendered — cd-deploy " +
      "didn't substitute the placeholders. Check the publish-admin-swa job.</p>";
    return;
  }

  const msal = new msalBrowser.PublicClientApplication({
    auth: {
      clientId: cfg.clientId,
      authority: "https://login.microsoftonline.com/" + cfg.tenantId,
      redirectUri: window.location.origin,
    },
    cache: {
      // Session storage — the token survives a page refresh but not a tab
      // close. Localstorage would cache MFA across tabs but persists
      // longer than we want for an admin surface.
      cacheLocation: "sessionStorage",
    },
  });

  // App-id-uri scope. The Entra app registration must expose an API scope
  // (the FORK-SETUP bootstrap mints `access_as_user`); requesting
  // `${api://<clientId>}/.default` returns the union of all granted scopes
  // for this app, which includes the role-bearing access token.
  const scopes = ["api://" + cfg.clientId + "/.default"];

  // ── elements
  const signinSection = document.getElementById("signin-section");
  const contentSection = document.getElementById("content-section");
  const signinBtn = document.getElementById("signin-btn");
  const signoutBtn = document.getElementById("signout-btn");
  const refreshBtn = document.getElementById("refresh-btn");
  const userLabel = document.getElementById("user-label");
  const statusEl = document.getElementById("status");
  const tbody = document.querySelector("#allowlist-table tbody");

  let account = null;

  // ── sign-in flow
  async function init() {
    await msal.initialize();
    // handleRedirectPromise resolves the redirect-back leg of the auth flow.
    const result = await msal.handleRedirectPromise();
    if (result && result.account) {
      account = result.account;
    } else {
      const all = msal.getAllAccounts();
      account = all[0] || null;
    }
    render();
    if (account) await load();
  }

  function render() {
    if (account) {
      userLabel.textContent = account.username;
      signoutBtn.hidden = false;
      signinSection.hidden = true;
      contentSection.hidden = false;
    } else {
      userLabel.textContent = "";
      signoutBtn.hidden = true;
      signinSection.hidden = false;
      contentSection.hidden = true;
    }
  }

  signinBtn.addEventListener("click", () => {
    msal.loginRedirect({ scopes });
  });

  signoutBtn.addEventListener("click", () => {
    msal.logoutRedirect({ postLogoutRedirectUri: window.location.origin });
  });

  // ── token acquisition
  async function getToken() {
    try {
      const r = await msal.acquireTokenSilent({ account, scopes });
      return r.accessToken;
    } catch (e) {
      // Silent acquire fails on cache miss, consent change, or expired
      // refresh token; fall back to interactive so the user re-consents.
      await msal.acquireTokenRedirect({ scopes });
      return null;
    }
  }

  // ── API calls
  async function api(path, method = "GET") {
    const token = await getToken();
    if (!token) return null;
    const r = await fetch(cfg.apiBase + path, {
      method,
      headers: { Authorization: "Bearer " + token },
    });
    if (!r.ok) throw new Error("HTTP " + r.status + " on " + path);
    return r.json();
  }

  // ── rendering
  refreshBtn.addEventListener("click", load);

  async function load() {
    setStatus("loading…");
    try {
      const data = await api("/admin/allowlist");
      renderRows(data.items || []);
      setStatus(data.items.length + " rows");
    } catch (e) {
      setStatus(e.message, true);
    }
  }

  function setStatus(msg, error = false) {
    statusEl.textContent = msg;
    statusEl.classList.toggle("error", error);
  }

  function renderRows(rows) {
    tbody.innerHTML = "";
    if (rows.length === 0) {
      const tr = document.createElement("tr");
      tr.innerHTML = "<td colspan='5' class='muted'>no testers have signed in yet</td>";
      tbody.appendChild(tr);
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
      const action = tr.querySelector("td:last-child");
      const btn = document.createElement("button");
      if (row.approved) {
        btn.textContent = "revoke";
        btn.className = "danger";
        btn.onclick = () => mutate(row.sub, "revoke", btn);
      } else {
        btn.textContent = "approve";
        btn.onclick = () => mutate(row.sub, "approve", btn);
      }
      action.appendChild(btn);
      tbody.appendChild(tr);
    }
  }

  async function mutate(sub, op, btn) {
    btn.disabled = true;
    setStatus(op + " " + sub + "…");
    try {
      await api("/admin/allowlist/" + encodeURIComponent(sub) + "/" + op, "POST");
      await load();
    } catch (e) {
      setStatus(e.message, true);
      btn.disabled = false;
    }
  }

  function fmt(iso) {
    if (!iso) return "—";
    const d = new Date(iso);
    return d.toLocaleString();
  }

  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, c => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
    })[c]);
  }

  init();
})();
