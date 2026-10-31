(() => {
  "use strict";

  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];

  const panes = { login: $("#pane-login"), sync: $("#pane-sync"), done: $("#pane-done") };
  const footerStatus = $("#footer-status");

  /* ---- Profil de démonstration (pré-remplissage, sans credential) ---- */
  const dc = $("#dc"), domain = $("#domain"), user = $("#user"), pass = $("#password");
  const userPrefix = $("#user-prefix");
  const syncTarget = $("#sync-target");

  const refreshPrefix = () => {
    const label = domain.value.split(".")[0].slice(0, 8).toUpperCase() || "DOMAINE";
    userPrefix.textContent = `${label}\\`;
    user.style.setProperty("--prefix-w", `${userPrefix.offsetWidth + 16}px`);
  };
  domain.addEventListener("input", refreshPrefix);
  refreshPrefix();

  /* ---- Afficher / masquer le mot de passe ---- */
  const reveal = $("#reveal");
  const slash = $(".reveal__slash", reveal);
  reveal.addEventListener("click", () => {
    const shown = pass.type === "text";
    pass.type = shown ? "password" : "text";
    reveal.setAttribute("aria-pressed", String(!shown));
    reveal.setAttribute("aria-label", shown ? "Afficher le mot de passe" : "Masquer le mot de passe");
    slash.toggleAttribute("hidden", shown);
  });

  /* ---- Validation ---- */
  const setError = (input, message) => {
    input.closest(".field").classList.toggle("field--invalid", !!message);
    const err = $("#" + input.id + "-error");
    err.textContent = message || "";
    err.hidden = !message;
    input.setAttribute("aria-invalid", message ? "true" : "false");
  };

  const looksLikeHost = (v) =>
    /^(\d{1,3}\.){3}\d{1,3}$/.test(v) || /^[a-z0-9][a-z0-9.-]*\.[a-z]{2,}$/i.test(v);

  const validators = {
    dc: (v) => (looksLikeHost(v) ? "" : "FQDN ou adresse IPv4 attendu."),
    domain: (v) => (looksLikeHost(v) ? "" : "Nom de domaine attendu (ex. demo.local)."),
    user: (v) => (v.trim() ? "" : "Nom d'utilisateur requis."),
    password: (v) => (v.length >= 4 ? "" : "Mot de passe requis (4 caractères minimum)."),
  };

  [dc, domain, user, pass].forEach((i) => {
    i.addEventListener("input", () => setError(i, ""));
  });

  /* ---- Séquence de health check + synchronisation ---- */
  const steps = $$(".step", $("#steps"));
  const bar = $("#progress-bar");
  const progress = $("#progress");

  const setPane = (name) => {
    Object.entries(panes).forEach(([k, el]) => (el.hidden = k !== name));
    footerStatus.textContent =
      name === "login" ? "Health check prêt — DNS · WinRM · AD DS" :
      name === "sync"  ? "Synchronisation en cours…" :
                         `Synchronisé le ${new Date().toLocaleTimeString("fr-FR")}`;
  };

  const runSync = async () => {
    setPane("sync");
    syncTarget.textContent = domain.value.trim();
    $("#sync-user").textContent = `${userPrefix.textContent.replace("\\", "")}\\${user.value.trim()}`;
    steps.forEach((s) => s.classList.remove("step--active", "step--done", "step--warn"));
    $(".step__meta", steps[0]).textContent = "";
    bar.style.width = "0%";

    const plan = [
      { key: "dns",    ms: 700,  meta: "192.168.50.10 · 12 ms" },
      { key: "winrm",  ms: 900,  meta: "OK · mode distant" },
      { key: "auth",   ms: 800,  meta: `${$("#sync-user").textContent} authentifié` },
      { key: "ou",     ms: 900,  meta: "13 OU importées" },
      { key: "users",  ms: 1300, meta: "248 users · 312 postes" },
      { key: "gpo",    ms: 1100, meta: "37 GPO liées" },
      { key: "cache",  ms: 600,  meta: "instantané à l'heure courante" },
    ];

    for (let i = 0; i < plan.length; i++) {
      const step = steps[i];
      const { key, ms, meta } = plan[i];
      step.classList.add("step--active");

      if (key === "dns") {
        const host = dc.value.trim();
        $(".step__label", step).innerHTML =
          `Résolution DNS du contrôleur <code>${escapeHtml(host)}</code>`;
      }

      await wait(ms);
      step.classList.remove("step--active");
      step.classList.add("step--done");
      $(".step__meta", step).textContent = meta;
      const pct = Math.round(((i + 1) / plan.length) * 100);
      bar.style.width = `${pct}%`;
      progress.setAttribute("aria-valuenow", String(pct));
    }

    await wait(450);
    showDone();
  };

  const escapeHtml = (s) => s.replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

  const wait = (ms) => new Promise((r) => setTimeout(r, ms));

  /* ---- Écran de succès : compteurs animés ---- */
  const showDone = () => {
    setPane("done");
    $$("#stats .stat strong").forEach((el) => {
      const target = +el.dataset.count;
      if (document.hidden) { el.textContent = target; return; }
      const t0 = performance.now();
      const tick = (t) => {
        const p = Math.min((t - t0) / 900, 1);
        el.textContent = Math.round(target * (1 - Math.pow(1 - p, 3)));
        if (p < 1) requestAnimationFrame(tick);
      };
      requestAnimationFrame(tick);
    });
  };

  /* ---- Submit ---- */
  $("#login-form").addEventListener("submit", (e) => {
    e.preventDefault();
    let first = null;
    for (const input of [dc, domain, user, pass]) {
      const msg = validators[input.id](input.value);
      setError(input, msg);
      if (msg && !first) first = input;
    }
    if (first) { first.focus(); return; }

    const submit = $("#submit");
    submit.classList.add("btn--loading");
    submit.setAttribute("aria-busy", "true");
    setTimeout(() => {
      submit.classList.remove("btn--loading");
      submit.removeAttribute("aria-busy");
      runSync();
    }, 600);
  });

  $("#goto-dash").addEventListener("click", () => {
    footerStatus.textContent = "Tableau de bord — démonstration du prototype";
  });

  $("#logout").addEventListener("click", () => setPane("login"));

  /* ---- Micro-interactions SSO ---- */
  $$("[data-provider]").forEach((btn) => {
    btn.addEventListener("click", () => {
      btn.classList.add("btn--loading");
      setTimeout(() => btn.classList.remove("btn--loading"), 1800);
    });
  });

  /* ---- Léger effet de tilt 3D sur la carte ---- */
  const tilt = $("#tilt");
  const reduceMotion = matchMedia("(prefers-reduced-motion: reduce)");
  if (!reduceMotion.matches && matchMedia("(pointer: fine)").matches) {
    const stage = $(".stage");
    stage.addEventListener("pointermove", (e) => {
      const r = tilt.getBoundingClientRect();
      const dx = (e.clientX - (r.left + r.width / 2)) / r.width;
      const dy = (e.clientY - (r.top + r.height / 2)) / r.height;
      tilt.style.transform = `rotateY(${dx * 3.5}deg) rotateX(${-dy * 3.5}deg)`;
    });
    stage.addEventListener("pointerleave", () => (tilt.style.transform = ""));
  }
})();
