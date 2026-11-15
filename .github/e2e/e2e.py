"""
Parcours de bout en bout dans un vrai navigateur : ce que fait l'administrateur se voit sur le site.

1. Une question ajoutée dans le back-office apparaît sur la page FAQ publique.
2. Le statut d'une demande se change et reste enregistré.
3. L'export des demandes (CSV) se télécharge.
4. Le menu du back-office se replie et s'ouvre sur téléphone.
5. Le formulaire de devis public refuse un envoi vide et affiche les erreurs.
6. Les anciennes adresses redirigent (301) ; une adresse inconnue reste en 404.
7. Une section ouverte depuis le menu n'est pas cachée sous l'en-tête collant.
8. Le plan marketing se crée, ses publications apparaissent dans le calendrier et s'approuvent d'un clic, se lance (le pilote passe au rythme du plan), relève les chiffres ; un plan en réserve ne se lance pas tant qu'un plan tourne.
9. Une carte-cadeau se demande sur le site, se marque payée (elle part), s'utilise et son solde se vérifie.

Usage : python3 e2e.py http://localhost:5080
"""
import sys
import time

from playwright.sync_api import sync_playwright, expect

from common import session

BASE = sys.argv[1].rstrip("/")
failures = []


def step(name):
    def wrap(fn):
        def run(*args):
            try:
                fn(*args)
                print(f"OK   {name}")
            except Exception as ex:
                failures.append(f"{name} : {str(ex).splitlines()[0][:300]}")
                print(f"ÉCHEC {name}\n{ex}")
        return run
    return wrap


@step("FAQ : ajoutée dans le back-office, visible sur le site")
def faq(page):
    question = f"Question de test {int(time.time())} ?"
    page.goto(BASE + "/admin/faq", wait_until="networkidle")
    page.wait_for_timeout(800)
    page.click("text=+ Ajouter")
    form = page.locator("form.admin-form")
    form.locator(".field:has(label:text-is('Question')) input").fill(question)
    form.locator(".field:has(label:text-is('Réponse')) textarea").fill("Réponse écrite par le test de bout en bout.")
    visible = form.locator("label.check input[type=checkbox]")
    if not visible.is_checked():
        visible.check()
    form.locator("button[type=submit]").click()
    expect(page.locator("table.admin-table")).to_contain_text(question, timeout=10000)
    page.goto(BASE + "/faq", wait_until="networkidle")
    expect(page.locator("main")).to_contain_text(question)


@step("Demande : le statut change et reste enregistré")
def lead_status(page):
    page.goto(BASE + "/admin/demandes", wait_until="networkidle")
    page.wait_for_timeout(800)
    page.locator("td a[href^='/admin/demandes/']").first.click()
    page.wait_for_selector("#status", timeout=10000)
    page.wait_for_timeout(800)
    page.select_option("#status", "Contacted")
    page.wait_for_timeout(1500)
    page.reload(wait_until="networkidle")
    page.wait_for_timeout(800)
    expect(page.locator("#status")).to_have_value("Contacted")


@step("Demandes : export CSV")
def csv_export(page):
    page.goto(BASE + "/admin/demandes", wait_until="networkidle")
    page.wait_for_timeout(800)
    with page.expect_download(timeout=15000) as d:
        page.click("text=Exporter (CSV)")
    path = d.value.path()
    content = open(path, encoding="utf-8-sig").read()
    assert "Test Fumée" in content, "la demande du test est absente de l'export"


@step("Téléphone : le menu du back-office se replie et s'ouvre")
def mobile_menu(browser, state):
    ctx = browser.new_context(viewport={"width": 390, "height": 844}, is_mobile=True, has_touch=True, locale="fr-CA", storage_state=state)
    page = ctx.new_page()
    page.goto(BASE + "/admin", wait_until="networkidle")
    nav = page.locator(".admin-sidebar nav")
    expect(nav).to_be_hidden()
    page.click("text=☰ Menu")
    expect(nav).to_be_visible()
    page.click(".admin-sidebar nav >> text=Demandes")
    page.wait_for_url("**/admin/demandes", timeout=10000)
    ctx.close()


@step("Résultats : la publication partie y figure avec ses chiffres")
def results(page):
    page.goto(BASE + "/admin/resultats", wait_until="networkidle")
    page.wait_for_selector("text=Le parcours", timeout=15000)
    expect(page.locator(".kpi").first).to_contain_text("Publications parties")
    expect(page.locator("text=Chaque publication")).to_be_visible()
    expect(page.locator("table.admin-table").last).to_contain_text("Facebook")
    page.click("text=7 jours")
    page.wait_for_timeout(800)
    expect(page.locator(".segmented .active")).to_have_text("7 jours")


@step("Devis public : un envoi vide affiche les erreurs")
def quote_validation(page):
    page.goto(BASE + "/devis", wait_until="networkidle")
    page.locator("form button[type=submit]").first.click()
    page.wait_for_timeout(1500)
    expect(page.locator(".field-error, .validation-message, .alert-error").first).to_be_visible()


@step("Anciennes adresses : redirection permanente vers la bonne page")
def legacy_redirects(page):
    for old, new in [("/contactez-nous/", "/contact"), ("/Devis.html", "/devis"), ("/fr/faq", "/faq")]:
        r = page.request.get(BASE + old, max_redirects=0)
        assert r.status == 301, f"{old} : statut {r.status}"
        assert r.headers["location"].endswith(new), f"{old} → {r.headers['location']}"
    r = page.request.get(BASE + "/page-qui-nexiste-pas", max_redirects=0)
    assert r.status == 404, f"page inconnue : statut {r.status}"


@step("Menu du site : la section visée n'est pas cachée sous l'en-tête")
def anchor_offset(page):
    page.goto(BASE + "/", wait_until="networkidle")
    href = page.locator(".site-header a[href^='/#']").first.get_attribute("href")
    page.goto(BASE + href, wait_until="networkidle")
    page.wait_for_timeout(1200)
    header = page.locator(".site-header").bounding_box()
    title = page.locator(f"[id='{href[2:]}'] h2").first.bounding_box()
    assert title["y"] >= header["y"] + header["height"] - 1, f"titre à {title['y']:.0f}px, en-tête jusqu'à {header['y'] + header['height']:.0f}px"


@step("Plan marketing : créé, lancé, chiffres relevés")
def marketing_plan(page):
    page.goto(BASE + "/admin/plan-marketing", wait_until="networkidle")
    page.wait_for_timeout(800)
    if page.locator("text=Créer le plan").count() > 0:
        page.locator("button", has_text="Créer le plan").click()
        expect(page.locator("text=Plan créé en brouillon")).to_be_visible(timeout=15000)
    if page.locator("button", has_text="Lancer le plan").count() > 0:
        page.locator("button", has_text="Lancer le plan").click()
        expect(page.locator("text=Plan lancé")).to_be_visible(timeout=15000)
    expect(page.locator(".plan-status .status-active")).to_have_text("En cours")
    expect(page.locator(".plan-phase")).to_have_count(3)
    expect(page.locator(".plan-mix tbody tr")).to_have_count(4)
    page.locator("button", has_text="Relever les chiffres").click()
    expect(page.locator(".alert-success, .alert-error").first).to_be_visible(timeout=30000)

    # Plan en réserve : créé pendant le plan en cours, il ne peut pas être lancé, puis il est supprimé.
    page.locator("button", has_text="Nouveau plan").click()
    page.locator("button", has_text="Créer le plan").click()
    expect(page.locator("text=Plan créé en réserve")).to_be_visible(timeout=15000)
    expect(page.locator(".plan-status .status-approval")).to_have_text("En réserve")
    expect(page.locator(".plan-reserve")).to_be_visible()
    expect(page.locator("button", has_text="Lancer le plan")).to_have_count(0)
    expect(page.locator(".plan-list-table tbody tr")).to_have_count(2)
    page.locator(".plan-status-actions button", has_text="Supprimer").click()
    expect(page.locator("text=Plan supprimé")).to_be_visible(timeout=15000)
    expect(page.locator(".plan-status .status-active")).to_have_text("En cours")

    page.goto(BASE + "/admin/pilote-automatique", wait_until="networkidle")
    expect(page.locator("select").first).to_have_value("7")


@step("Calendrier : le lancement du plan prépare les publications, visibles jour par jour, approuvées d'un clic")
def marketing_calendar(page):
    page.goto(BASE + "/admin/marketing", wait_until="networkidle")
    expect(page.locator(".cal-health")).to_be_visible()
    expect(page.locator(".cal-day")).to_have_count(7)
    # Le lancement du plan a demandé la préparation : on attend qu'elle se termine (barre de progression).
    page.wait_for_function(
        "() => document.querySelector('.cal-progress.is-done, .cal-progress.is-failed') || document.querySelectorAll('.cal-card').length > 0",
        timeout=240000)
    page.wait_for_timeout(1500)
    expect(page.locator(".cal-progress.is-failed")).to_have_count(0)
    # Les créneaux commencent demain (le week-end, le calendrier s'ouvre déjà sur la semaine suivante).
    if page.locator(".cal-card").count() == 0:
        page.locator("button", has_text="Suivante").click()
        page.wait_for_timeout(1000)
    expect(page.locator(".cal-card").first).to_be_visible(timeout=60000)
    expect(page.locator(".cal-card .cal-thumb img").first).to_be_visible()
    if page.locator(".cal-approve").count() == 0:
        page.locator("button", has_text="Suivante").click()
        page.wait_for_timeout(1000)
    page.locator(".cal-approve button", has_text="Tout approuver").click()
    expect(page.locator(".alert-success")).to_contain_text("approuvée(s)", timeout=15000)
    expect(page.locator(".cal-approve")).to_have_count(0)
    expect(page.locator(".cal-card.cal-ready").first).to_be_visible()


@step("Nouvelle campagne : arrêt de l'ancienne, assistant en 3 écrans, aperçu de la semaine, démarrage avec tout approuvé")
def campaign_wizard(page):
    # Arrêter la campagne en cours (pilote en pause).
    page.goto(BASE + "/admin/plan-marketing", wait_until="networkidle")
    page.locator(".plan-list-table button", has_text="Arrêter le plan").click()
    page.locator("button", has_text="Oui, arrêter le plan").click()
    expect(page.locator("text=Plan arrêté")).to_be_visible(timeout=15000)

    page.goto(BASE + "/admin/marketing/nouvelle-campagne", wait_until="networkidle")
    page.locator(".wiz-choice", has_text="Obtenir des demandes de devis").click()
    page.locator(".wiz-choice", has_text="Six mois").click()
    page.locator("button", has_text="Continuer").click()
    page.locator(".wiz-choice", has_text="Intensif").click()
    page.locator("button", has_text="Voir la première semaine").click()
    # L'aperçu se prépare avec une barre de progression, puis les boutons de démarrage apparaissent.
    expect(page.locator("button", has_text="Démarrer et tout approuver")).to_be_visible(timeout=300000)
    expect(page.locator(".wiz-preview .cal-card").first).to_be_visible()
    assert page.locator(".wiz-preview .cal-card").count() >= 7, "l'aperçu d'une semaine intensive doit compter au moins 7 publications"
    page.locator("button", has_text="Démarrer et tout approuver").click()
    page.wait_for_url("**/admin/marketing?demarree=1", timeout=30000)
    expect(page.locator(".alert-success")).to_contain_text("Campagne démarrée")
    expect(page.locator(".cal-approve")).to_have_count(0)

    page.goto(BASE + "/admin/plan-marketing", wait_until="networkidle")
    expect(page.locator(".plan-status .status-active")).to_have_text("En cours")
    expect(page.locator(".plan-status")).to_contain_text("Demandes de devis")
    expect(page.locator(".plan-status")).to_contain_text("14 publications par semaine")


@step("Carte-cadeau : demandée sur le site, payée, envoyée puis utilisée")
def gift_card(page):
    page.goto(BASE + "/carte-cadeau", wait_until="networkidle")
    page.locator("label.amount-choice", has_text="75").click()
    page.fill("#rname", "Jeanne Essai")
    page.fill("#remail", "jeanne@example.com")
    page.fill("#msg", "Joyeux Noël !")
    page.fill("#bname", "Marie Essai")
    page.fill("#bemail", "marie@example.com")
    page.locator(".consent-field input[type=checkbox]").check()
    page.locator("form.lead-form button[type=submit]").click()
    page.wait_for_url("**/merci?demande=carte-cadeau", timeout=15000)

    page.goto(BASE + "/admin/cartes-cadeaux", wait_until="networkidle")
    page.wait_for_timeout(1000)
    row = page.locator("tr", has_text="Marie Essai").first
    code = row.locator("code").inner_text().strip()
    row.locator("button", has_text="Paiement reçu").click()
    expect(page.locator(".alert-success")).to_be_visible(timeout=15000)
    expect(page.locator("tr", has_text="Marie Essai").first.locator(".status")).to_have_text("Payée")

    page.fill("#rcode", code.lower().replace("-", " "))
    page.fill("#ramount", "25")
    page.locator("button", has_text="Déduire du solde").click()
    expect(page.locator(".alert-success")).to_contain_text("50 $", timeout=15000)

    page.goto(BASE + f"/carte-cadeau?code={code}", wait_until="networkidle")
    expect(page.locator(".giftcard-aside .alert-success")).to_contain_text("50 $")


with sync_playwright() as p:
    browser = p.chromium.launch()
    state = session(browser, BASE)
    ctx = browser.new_context(viewport={"width": 1366, "height": 900}, locale="fr-CA", storage_state=state, accept_downloads=True)
    page = ctx.new_page()
    faq(page)
    lead_status(page)
    csv_export(page)
    results(page)
    mobile_menu(browser, state)
    quote_validation(page)
    legacy_redirects(page)
    anchor_offset(page)
    marketing_plan(page)
    marketing_calendar(page)
    campaign_wizard(page)
    gift_card(page)
    browser.close()

if failures:
    for f in failures:
        print(f"::error title=Test de bout en bout::{f}")
    sys.exit(1)
print("::notice::Tests de bout en bout : FAQ, statut des demandes, export CSV, menu mobile, validation du devis, redirections, ancres du menu, plan marketing, calendrier, nouvelle campagne, carte-cadeau")
