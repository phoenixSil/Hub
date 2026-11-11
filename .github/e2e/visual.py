"""
Tests visuels automatiques du site public et du back-office.

Chaque page est ouverte en bureau, tablette et mobile. Le script vérifie ce qu'un œil humain
remarquerait : page plus large que l'écran, bouton dont le texte passe sur deux lignes ou est coupé,
contrôles qui se chevauchent, image cassée, contenu qui sort de sa carte, erreurs JavaScript ou
ressources en erreur. Une capture de chaque page est enregistrée pour la relecture.

Usage : python3 visual.py http://localhost:5080 dossier-captures
"""
import json
import os
import sys
import traceback

from common import session

from playwright.sync_api import sync_playwright

BASE = sys.argv[1].rstrip("/")
OUT = os.path.join(sys.argv[2], "visuel")
os.makedirs(OUT, exist_ok=True)

VIEWPORTS = [
    ("bureau", {"width": 1366, "height": 900}, False),
    ("tablette", {"width": 820, "height": 1180}, True),
    ("mobile", {"width": 390, "height": 844}, True),
]

PUBLIC = [
    ("accueil", "/"),
    ("service", "/services/nettoyage-residentiel"),
    ("aines", "/aines/aide-a-domicile"),
    ("secteurs", "/secteurs"),
    ("secteur", "/secteurs/sainte-foy"),
    ("devis", "/devis"),
    ("rendez-vous", "/rendez-vous"),
    ("contact", "/contact"),
    ("faq", "/faq"),
    ("a-propos", "/a-propos"),
    ("confidentialite", "/confidentialite"),
    ("404", "/page-inconnue"),
    ("connexion", "/compte/connexion"),
    ("carte-cadeau", "/carte-cadeau"),
    ("accueil-noel", "/?theme=noel"),
    ("accueil-halloween", "/?theme=halloween"),
]

ADMIN = [
    ("admin-tableau", "/admin"),
    ("admin-demandes", "/admin/demandes"),
    ("admin-demande", "first:/admin/demandes|td a[href^='/admin/demandes/']"),
    ("admin-horaires", "/admin/horaires"),
    ("admin-publications", "/admin/publications"),
    ("admin-publication", "first:/admin/publications|td a[href^='/admin/publications/']"),
    ("admin-publication-nouvelle", "/admin/publications/nouvelle"),
    ("admin-pilote", "/admin/pilote-automatique"),
    ("admin-resultats", "/admin/resultats"),
    ("admin-plan", "/admin/plan-marketing"),
    ("admin-planche", "/admin/planche"),
    ("admin-cartes-cadeaux", "/admin/cartes-cadeaux"),
    ("admin-services", "/admin/services"),
    ("admin-secteurs", "/admin/secteurs"),
    ("admin-offres", "/admin/offres"),
    ("admin-faq", "/admin/faq"),
    ("admin-avis", "/admin/avis"),
    ("admin-textes", "/admin/textes"),
    ("admin-pages", "/admin/pages"),
    ("admin-images", "/admin/images"),
    ("admin-entreprise", "/admin/entreprise"),
    ("admin-canaux", "/admin/canaux"),
    ("admin-securite", "/admin/securite"),
]

# Vérifications exécutées dans la page.
CHECKS = r"""
() => {
  const issues = [];
  const doc = document.documentElement;
  const vw = doc.clientWidth;
  if (doc.scrollWidth > vw + 1) {
    const wide = [...document.querySelectorAll('body *')]
      .filter(e => e.getBoundingClientRect().right > vw + 1 && getComputedStyle(e).position !== 'fixed')
      .slice(-1).map(e => e.tagName.toLowerCase() + (e.className && typeof e.className === 'string' ? '.' + e.className.trim().split(/\s+/).join('.') : ''));
    issues.push(`page plus large que l'écran (${doc.scrollWidth}px pour ${vw}px) : ${wide.join(', ')}`);
  }

  const shown = e => {
    const r = e.getBoundingClientRect(), s = getComputedStyle(e);
    return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none' && s.opacity !== '0';
  };
  const pinned = e => { for (let x = e; x; x = x.parentElement) { const p = getComputedStyle(x).position; if (p === 'sticky' || p === 'fixed') return true; } return false; };
  const scrolls = e => { for (let x = e.parentElement; x; x = x.parentElement) { const o = getComputedStyle(x).overflowX; if (o === 'auto' || o === 'scroll' || o === 'hidden') return true; } return false; };
  const name = e => (e.innerText || e.value || e.getAttribute('aria-label') || e.getAttribute('placeholder') || e.tagName).trim().replace(/\s+/g, ' ').slice(0, 50);

  for (const e of document.querySelectorAll('.btn, button, .status, .tag, .badge, .chip')) {
    if (!shown(e) || e.closest('[hidden]')) continue;
    const s = getComputedStyle(e), r = e.getBoundingClientRect();
    const text = (e.innerText || '').trim();
    if (!text) continue;
    const line = parseFloat(s.lineHeight) || parseFloat(s.fontSize) * 1.3;
    const inner = r.height - parseFloat(s.paddingTop) - parseFloat(s.paddingBottom) - parseFloat(s.borderTopWidth) - parseFloat(s.borderBottomWidth);
    if (inner > line * 1.6 && text.length <= 32 && !text.includes('\n')) issues.push(`bouton sur deux lignes : « ${name(e)} »`);
    if (e.scrollWidth > e.clientWidth + 2 && s.overflow !== 'visible') issues.push(`texte coupé : « ${name(e)} »`);
    if ((r.right > vw + 1 || r.left < -1) && !scrolls(e)) issues.push(`hors de l'écran : « ${name(e)} »`);
  }

  const controls = [...document.querySelectorAll('a.btn, button, input:not([type=hidden]), select, textarea')]
    .filter(e => shown(e) && !pinned(e) && !e.closest('[hidden]'));
  const boxes = controls.map(e => e.getBoundingClientRect());
  for (let i = 0; i < controls.length; i++) for (let j = i + 1; j < controls.length; j++) {
    const a = boxes[i], b = boxes[j];
    if (controls[i].contains(controls[j]) || controls[j].contains(controls[i])) continue;
    if (controls[i].closest('label') && controls[i].closest('label') === controls[j].closest('label')) continue;
    const w = Math.min(a.right, b.right) - Math.max(a.left, b.left), h = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
    if (w > 4 && h > 4 && w * h > 0.2 * Math.min(a.width * a.height, b.width * b.height))
      issues.push(`éléments superposés : « ${name(controls[i])} » et « ${name(controls[j])} »`);
  }

  // Thèmes saisonniers : chaque texte reste lisible (contraste AA) et le « S » déguisé ne cache aucun texte.
  const rgb = c => (c.match(/[\d.]+/g) || []).map(Number);
  const lum = ([r, g, b]) => [r, g, b].map(v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); })
    .reduce((s, v, i) => s + v * [0.2126, 0.7152, 0.0722][i], 0);
  const backOf = e => { for (let x = e; x; x = x.parentElement) { const c = rgb(getComputedStyle(x).backgroundColor); if (c.length >= 3 && (c.length < 4 || c[3] > 0.9)) return c; } return [255, 255, 255]; };
  for (const e of document.querySelectorAll('.season-bar, .season-slide .hero-title, .season-slide .hero-lead, .season-slide .eyebrow, .season-slide .btn, .snow-toggle')) {
    if (!shown(e)) continue;
    const s = getComputedStyle(e), fg = rgb(s.color), bg = backOf(e);
    const [l1, l2] = [lum(fg), lum(bg)].sort((a, b) => b - a), ratio = (l1 + 0.05) / (l2 + 0.05);
    const large = parseFloat(s.fontSize) >= 24 || (parseFloat(s.fontSize) >= 18.6 && parseInt(s.fontWeight) >= 700);
    if (ratio < (large ? 3 : 4.5)) issues.push(`texte peu lisible (${ratio.toFixed(1)}:1) : « ${name(e)} »`);
  }
  const mascot = document.querySelector('.season-mascot');
  if (mascot && shown(mascot)) {
    const m = mascot.getBoundingClientRect();
    for (const t of document.querySelectorAll('.season-slide .hero-title, .season-slide .hero-lead, .season-slide .hero-actions .btn, .season-slide .eyebrow')) {
      const r = t.getBoundingClientRect();
      const w = Math.min(m.right, r.right) - Math.max(m.left, r.left), h = Math.min(m.bottom, r.bottom) - Math.max(m.top, r.top);
      if (w > 4 && h > 4) issues.push(`le logo déguisé cache un texte : « ${name(t)} »`);
    }
  }

  const raw = document.body.innerText.match(/\{\{[^}]{1,40}\}\}/);
  if (raw && !location.pathname.startsWith("/admin")) issues.push(`champ non remplacé affiché : ${raw[0]}`);
  if (/\b(undefined|NaN)\b/.test(document.body.innerText)) issues.push('texte « undefined » ou « NaN » affiché');

  for (const img of document.images) if (shown(img) && img.complete && img.naturalWidth === 0) issues.push(`image cassée : ${img.getAttribute('src')}`);
  // Image écrasée ou étirée (ex. logo comprimé par un menu trop large) : proportions affichées ≠ proportions réelles.
  for (const img of document.images) {
    if (!shown(img) || !img.complete || !img.naturalWidth || getComputedStyle(img).objectFit !== 'fill') continue;
    const r = img.getBoundingClientRect(), st = getComputedStyle(img), px = v => parseFloat(v) || 0;
    // Proportions de l'image elle-même : sans les marges intérieures ni la bordure (ex. logo sur pastille blanche).
    const w = r.width - px(st.paddingLeft) - px(st.paddingRight) - px(st.borderLeftWidth) - px(st.borderRightWidth);
    const h = r.height - px(st.paddingTop) - px(st.paddingBottom) - px(st.borderTopWidth) - px(st.borderBottomWidth);
    const ratio = (w / h) / (img.naturalWidth / img.naturalHeight);
    if (w > 24 && h > 0 && (ratio < 0.9 || ratio > 1.1)) issues.push(`image déformée (${Math.round(ratio * 100)} %) : ${img.getAttribute('src')}`);
  }

  for (const card of document.querySelectorAll('.admin-card, .card, .admin-head, .field, .auth-card')) {
    if (!shown(card)) continue;
    if (card.scrollWidth > card.clientWidth + 2 && getComputedStyle(card).overflowX === 'visible')
      issues.push(`contenu qui sort de son cadre : ${card.className} « ${name(card).slice(0, 30)} »`);
  }
  return [...new Set(issues)];
}
"""


def resolve(page, path):
    """« first:/liste|sélecteur » : ouvre la liste puis le premier lien correspondant."""
    if not path.startswith("first:"):
        return BASE + path
    listing, selector = path[6:].split("|")
    page.goto(BASE + listing, wait_until="networkidle")
    page.wait_for_timeout(800)
    link = page.locator(selector).first
    if link.count() == 0:
        return None
    return BASE + link.get_attribute("href")


def audit(page, state, label, name, url, problems):
    state["errors"].clear()
    state["bad"].clear()
    page.goto(url, wait_until="networkidle")
    page.wait_for_timeout(1200 if "/admin" in url else 500)
    found = page.evaluate(CHECKS)
    # La page 404 annonce volontairement son code 404 : ce n'est pas une erreur.
    found += [f"erreur JavaScript : {e[:160]}" for e in state["errors"] if not (name == "404" and "404" in e)]
    found += [f"ressource en erreur : {b}" for b in state["bad"]]
    page.screenshot(path=f"{OUT}/{label}-{name}.png", full_page=True)
    for f in found:
        problems.append((label, name, f))
    print(("OK   " if not found else "!!   ") + f"{label:8} {name}" + ("" if not found else f" ({len(found)})"))


def watch(page):
    state = {"errors": [], "bad": []}
    page.on("console", lambda m: state["errors"].append(m.text) if m.type == "error" else None)
    page.on("response", lambda r: state["bad"].append(f"{r.status} {r.url}")
            if r.status >= 400 and "page-inconnue" not in r.url else None)
    return state


problems = []
with sync_playwright() as p:
    browser = p.chromium.launch()
    state_file = session(browser, BASE)
    for label, viewport, mobile in VIEWPORTS:
        ctx = browser.new_context(viewport=viewport, is_mobile=mobile, has_touch=mobile, device_scale_factor=1,
                                  locale="fr-CA", storage_state=state_file)
        page = ctx.new_page()
        state = watch(page)
        # Le bandeau de consentement est vérifié une fois, puis refusé : les captures montrent les pages telles quelles.
        audit(page, state, label, "consentement", BASE + "/", problems)
        page.locator("[data-consent='no']").first.click()
        for name, path in PUBLIC + ADMIN:
            try:
                url = resolve(page, path)
                if url:
                    audit(page, state, label, name, url, problems)
            except Exception as ex:  # une page qui plante ne doit pas masquer les autres
                traceback.print_exc()
                problems.append((label, name, f"la page n'a pas pu être testée : {str(ex).splitlines()[0][:200]}"))
        ctx.close()
    browser.close()

with open(os.path.join(OUT, "rapport.json"), "w", encoding="utf-8") as f:
    json.dump([{"ecran": l, "page": n, "probleme": m} for l, n, m in problems], f, ensure_ascii=False, indent=2)

if problems:
    for l, n, m in problems:
        print(f"::error title=Test visuel ({l}, {n})::{m}")
    print(f"{len(problems)} problème(s) visuel(s)")
    sys.exit(1)
print("::notice::Tests visuels : aucune anomalie sur bureau, tablette et mobile")
