// ScintiNet — site public : carrousel, consentement, mesure des clics, anti-pourriel.
(function () {
    "use strict";

    function cookie(name) {
        var m = document.cookie.match(new RegExp("(?:^|; )" + name + "=([^;]*)"));
        return m ? decodeURIComponent(m[1]) : null;
    }

    function beacon(type, detail) {
        try {
            var body = JSON.stringify({ type: type, path: location.pathname, detail: detail || null });
            if (navigator.sendBeacon) navigator.sendBeacon("/api/t", new Blob([body], { type: "application/json" }));
        } catch (e) { /* la mesure ne doit jamais bloquer le site */ }
    }

    // Carrousel d'accueil : défilement automatique, pause au survol, points de navigation.
    function initCarousel() {
        var root = document.querySelector("[data-carousel]");
        if (!root || root.dataset.ready) return;
        root.dataset.ready = "1";
        var track = root.querySelector(".hero-track");
        var slides = root.querySelectorAll(".hero-slide");
        var dots = root.querySelectorAll(".hero-dot");
        if (slides.length < 2) return;
        var index = 0, timer = null;
        var reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

        function go(i) {
            index = (i + slides.length) % slides.length;
            track.scrollTo({ left: track.clientWidth * index, behavior: reduced ? "auto" : "smooth" });
            dots.forEach(function (d, n) { d.setAttribute("aria-current", n === index ? "true" : "false"); });
        }
        function start() { if (!reduced) { stop(); timer = setInterval(function () { go(index + 1); }, 7000); } }
        function stop() { if (timer) clearInterval(timer); timer = null; }

        dots.forEach(function (d, n) { d.addEventListener("click", function () { go(n); start(); }); });
        track.addEventListener("scroll", function () {
            var i = Math.round(track.scrollLeft / track.clientWidth);
            if (i !== index) { index = i; dots.forEach(function (d, n) { d.setAttribute("aria-current", n === index ? "true" : "false"); }); }
        }, { passive: true });
        root.addEventListener("mouseenter", stop);
        root.addEventListener("mouseleave", start);
        root.addEventListener("focusin", stop);
        go(0);
        start();
    }

    // Consentement à la mesure (Loi 25) : aucun identifiant de visite sans « Accepter ».
    function initConsent() {
        var box = document.getElementById("consent");
        if (!box) return;
        if (!cookie("hub_consent")) box.hidden = false;
        box.querySelectorAll("[data-consent]").forEach(function (b) {
            b.addEventListener("click", function () {
                var value = b.getAttribute("data-consent");
                document.cookie = "hub_consent=" + value + "; path=/; max-age=" + (60 * 60 * 24 * 365) + "; samesite=lax" + (location.protocol === "https:" ? "; secure" : "");
                if (value === "no") document.cookie = "hub_vid=; path=/; max-age=0";
                box.hidden = true;
            });
        });
        document.querySelectorAll("[data-consent-open]").forEach(function (b) {
            b.addEventListener("click", function () { box.hidden = false; });
        });
    }

    // Turnstile n'est chargé que sur les pages qui ont un formulaire protégé.
    function initTurnstile() {
        if (!document.querySelector(".cf-turnstile") || window.turnstile) return;
        var s = document.createElement("script");
        s.src = "https://challenges.cloudflare.com/turnstile/v0/api.js";
        s.async = true;
        document.head.appendChild(s);
    }

    function initForms() {
        document.querySelectorAll("form[data-track-form]").forEach(function (form) {
            if (form.dataset.ready) return;
            form.dataset.ready = "1";
            form.addEventListener("focusin", function once() {
                form.removeEventListener("focusin", once);
                beacon("form", form.getAttribute("data-track-form"));
            });
            form.addEventListener("submit", function () {
                var btn = form.querySelector("button[type=submit]");
                if (btn) { btn.disabled = true; btn.textContent = "Envoi en cours…"; }
            });
        });
    }

    // Clics sur téléphone, courriel, WhatsApp : ce sont aussi des contacts.
    document.addEventListener("click", function (e) {
        var a = e.target.closest && e.target.closest("a[href]");
        if (!a) return;
        var href = a.getAttribute("href") || "";
        if (href.indexOf("tel:") === 0) beacon("phone");
        else if (href.indexOf("mailto:") === 0) beacon("email");
        else if (href.indexOf("wa.me") !== -1) beacon("whatsapp");
    });

    // Fermer le menu mobile après un choix.
    document.addEventListener("click", function (e) {
        var nav = document.querySelector(".mobile-nav[open]");
        if (nav && e.target.closest && e.target.closest(".mobile-panel a")) nav.removeAttribute("open");
    });

    function init() { initCarousel(); initConsent(); initTurnstile(); initForms(); }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
    else init();

    // Navigation améliorée de Blazor : réinitialiser après chaque changement de page.
    if (window.Blazor && window.Blazor.addEventListener) window.Blazor.addEventListener("enhancedload", init);
    else document.addEventListener("DOMContentLoaded", function () {
        if (window.Blazor && window.Blazor.addEventListener) window.Blazor.addEventListener("enhancedload", init);
    });
})();
