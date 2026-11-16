"""
Parcours de bout en bout dans un vrai navigateur : ce que fait l'administrateur se voit sur le site.

1. Une question ajoutée dans le back-office apparaît sur la page FAQ publique.
2. Le statut d'une demande se change et reste enregistré.
3. L'export des demandes (CSV) se télécharge.
4. Le menu du back-office se replie et s'ouvre sur téléphone.
5. Le formulaire de devis public refuse un envoi vide et affiche les erreurs.
6. Les anciennes adresses redirigent (301) ; une adresse inconnue reste en 404.
7. Une section ouverte depuis le menu n'est pas cachée sous l'en-tête collant.
8. Marketing : assistant de campagne avec aperçu, calendrier (remplacer, retirer, tout approuver), campagne en réserve, pause, arrêt, campagne intensive.
9. Une carte-cadeau se demande sur le site, se marque payée (elle part), s'utilise et son solde se vérifie.
11. Journal des décisions : les publications y sont, le gardien clôture leurs résultats, l'export CSV se télécharge.
10. Google Business : connexion en un clic, avis relevés, réponse proposée par l'IA puis publiée, chiffres de la fiche.

Usage : python3 e2e.py http://localhost:5080
"""
import re
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
    expect(page.get_by_role("heading", name="Chaque publication")).to_be_visible()
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


def choice(page, title):
    return page.locator(f".wiz-choice:has(strong:text-is('{title}'))")


def run_wizard(page, objective, duration, intensity):
    page.goto(BASE + "/admin/marketing/nouvelle-campagne", wait_until="networkidle")
    choice(page, objective).click()
    choice(page, duration).click()
    page.locator("button", has_text="Continuer").click()
    choice(page, intensity).click()


@step("Nouvelle campagne : assistant en 3 écrans, aperçu de la première semaine, démarrage")
def marketing_plan(page):
    run_wizard(page, "Être plus visible", "Trois mois", "Normal")
    page.locator("button", has_text="Voir la première semaine").click()
    # L'aperçu se prépare avec une barre de progression, puis les boutons de démarrage apparaissent.
    expect(page.locator("button", has_text="Démarrer (je validerai plus tard)")).to_be_visible(timeout=300000)
    expect(page.locator(".wiz-preview .cal-card").first).to_be_visible()
    page.locator("button", has_text="Démarrer (je validerai plus tard)").click()
    page.wait_for_url("**/admin/marketing?demarree=1", timeout=30000)
    expect(page.locator(".alert-success")).to_contain_text("Campagne démarrée")

    page.goto(BASE + "/admin/plan-marketing", wait_until="networkidle")
    expect(page.locator(".plan-status .status-active")).to_have_text("En cours")
    expect(page.locator(".plan-phase")).to_have_count(3)
    expect(page.locator(".plan-mix tbody tr")).to_have_count(4)
    page.locator("button", has_text="Relever les chiffres").click()
    expect(page.locator(".alert-success, .alert-error").first).to_be_visible(timeout=30000)


@step("Calendrier : la semaine jour par jour, Remplacer, Retirer, Tout approuver")
def marketing_calendar(page):
    page.goto(BASE + "/admin/marketing", wait_until="networkidle")
    expect(page.locator(".mk-tabs a.active")).to_have_text("Calendrier")
    expect(page.locator(".cal-health")).to_be_visible()
    expect(page.locator(".cal-day")).to_have_count(7)
    if page.locator(".cal-approve").count() == 0:
        page.locator("button", has_text="Suivante").click()
        page.wait_for_timeout(1000)
    expect(page.locator(".cal-card.cal-pending").first).to_be_visible(timeout=60000)
    expect(page.locator(".cal-card .cal-thumb img").first).to_be_visible()

    # Remplacer : une nouvelle publication prend la place de l'ancienne, au même créneau.
    before = page.locator(".cal-card").count()
    page.locator(".cal-card.cal-pending button", has_text="Remplacer").first.click()
    expect(page.locator(".alert-success")).to_contain_text("Publication remplacée", timeout=120000)
    expect(page.locator(".cal-card")).to_have_count(before)

    # Retirer, avec confirmation.
    page.locator(".cal-card.cal-pending button", has_text="Retirer").first.click()
    page.locator("button", has_text="Oui, retirer").click()
    expect(page.locator(".alert-success")).to_contain_text("Publication retirée", timeout=15000)
    expect(page.locator(".cal-card")).to_have_count(before - 1)

    page.locator(".cal-approve button", has_text="Tout approuver").click()
    expect(page.locator(".alert-success")).to_contain_text("approuvée(s)", timeout=15000)
    expect(page.locator(".cal-approve")).to_have_count(0)
    expect(page.locator(".cal-card.cal-ready").first).to_be_visible()


@step("Campagne : en réserve pendant une autre, pause, reprise, arrêt")
def campaign_page(page):
    # Une deuxième campagne créée pendant la première attend en réserve.
    run_wizard(page, "Obtenir des demandes de devis", "Un mois et demi", "Léger")
    page.locator("button", has_text="Continuer").click()
    expect(page.locator("text=Campagne prête, en attente")).to_be_visible(timeout=15000)

    page.goto(BASE + "/admin/plan-marketing", wait_until="networkidle")
    expect(page.locator(".mk-tabs a.active")).to_have_text("Campagne")
    expect(page.locator(".plan-list-table tbody tr")).to_have_count(2)
    page.locator(".plan-list-table tr", has_text="En réserve").locator("button", has_text="Ouvrir").click()
    expect(page.locator(".plan-reserve")).to_be_visible()
    expect(page.locator("button", has_text="Démarrer la campagne")).to_have_count(0)
    page.locator(".plan-status-actions button", has_text="Supprimer").click()
    expect(page.locator("text=Campagne supprimée")).to_be_visible(timeout=15000)
    expect(page.locator(".plan-status .status-active")).to_have_text("En cours")

    page.locator(".plan-status-actions button", has_text="Mettre en pause").click()
    expect(page.locator(".alert-success")).to_contain_text("Campagne en pause", timeout=15000)
    page.locator(".plan-status-actions button", has_text="Reprendre").click()
    expect(page.locator(".alert-success")).to_contain_text("Campagne reprise", timeout=15000)

    page.locator(".plan-list-table button", has_text="Arrêter la campagne").click()
    page.locator("button", has_text="Oui, arrêter la campagne").click()
    expect(page.locator("text=Campagne arrêtée")).to_be_visible(timeout=15000)
    expect(page.locator(".plan-status .status-lost")).to_have_text("Arrêtée")


@step("Nouvelle campagne intensive : aperçu de 14 publications, démarrage avec tout approuvé")
def campaign_wizard(page):
    run_wizard(page, "Obtenir des demandes de devis", "Six mois", "Intensif")
    page.locator("button", has_text="Voir la première semaine").click()
    expect(page.locator("button", has_text="Démarrer et tout approuver")).to_be_visible(timeout=300000)
    assert page.locator(".wiz-preview .cal-card").count() >= 7, "l'aperçu d'une semaine intensive doit compter au moins 7 publications"
    page.locator("button", has_text="Démarrer et tout approuver").click()
    page.wait_for_url("**/admin/marketing?demarree=1", timeout=30000)
    expect(page.locator(".alert-success")).to_contain_text("Campagne démarrée")
    expect(page.locator(".cal-approve")).to_have_count(0)

    page.goto(BASE + "/admin/plan-marketing", wait_until="networkidle")
    expect(page.locator(".plan-status .status-active")).to_have_text("En cours")
    expect(page.locator(".plan-status")).to_contain_text("Demandes de devis")
    expect(page.locator(".plan-status")).to_contain_text("14 publications par semaine")
    page.goto(BASE + "/admin/pilote-automatique", wait_until="networkidle")
    expect(page.locator("select").first).to_have_value("14")


@step("Gardien : une publication en échec passager est relancée toute seule, et notée au journal")
def guardian(page):
    page.goto(BASE + "/admin/publications/nouvelle", wait_until="networkidle")
    page.fill("#post-title", "Test des relances")
    page.fill("#post-message", "Test des relances [échec passager] : un ménage impeccable, sans effort.")
    page.locator("#post-message").blur()
    for name, wanted in (("Facebook", True), ("Instagram", False)):
        box = page.locator("label.check", has_text=name).locator("input[type=checkbox]")
        if box.count() and box.is_checked() != wanted:
            box.click()
    page.locator("button", has_text="Publier maintenant").click()
    # Premier envoi : échec passager (simulé).
    expect(page.locator("text=Service temporairement indisponible").first).to_be_visible(timeout=20000)

    page.goto(BASE + "/admin/marketing", wait_until="networkidle")
    page.locator("button", has_text="Vérifier maintenant").click()
    expect(page.locator(".alert-success")).to_contain_text("Vérification faite", timeout=120000)
    page.locator(".cal-journal summary").click()
    expect(page.locator(".cal-journal")).to_contain_text("relancée sur Facebook, partie")
    expect(page.locator(".cal-guard")).to_contain_text("Dernière vérification")


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


@step("Google Business : connecté en un clic, ajouté aux publications prévues, avis relevés, réponse proposée puis publiée")
def google_business(page):
    page.goto(BASE + "/admin/canaux", wait_until="networkidle")
    page.locator("a", has_text="Connecter avec Google").first.click()
    page.wait_for_url("**/admin/canaux?connecte=**", timeout=20000)
    expect(page.locator("text=Google Business connecté").first).to_be_visible(timeout=15000)

    # Le gardien ajoute Google aux publications déjà prévues.
    page.goto(BASE + "/admin/marketing", wait_until="networkidle")
    page.locator("button", has_text="Vérifier maintenant").click()
    expect(page.locator(".alert-success")).to_contain_text("Vérification faite", timeout=120000)
    page.locator(".cal-journal summary").click()
    expect(page.locator(".cal-journal")).to_contain_text("aussi sur votre fiche Google")
    expect(page.locator(".cal-net-googlebusiness").first).to_be_visible()

    page.goto(BASE + "/admin/avis-google", wait_until="networkidle")
    card = page.locator(".rv-card", has_text="Jean-François").first
    expect(card).to_be_visible(timeout=20000)
    card.locator("button", has_text="Proposer une réponse").click()
    expect(page.locator(".alert-success")).to_contain_text("Réponse proposée", timeout=120000)
    box = card.locator("textarea")
    expect(box).not_to_have_value("")
    box.fill("Merci Jean-François pour votre avis ! L'équipe Scintinet")
    card.locator("button", has_text="Publier la réponse").click()
    expect(page.locator(".alert-success")).to_contain_text("Réponse publiée", timeout=20000)
    expect(page.locator(".rv-card", has_text="Jean-François").first.locator(".rv-reply")).to_contain_text("Merci Jean-François")

    page.goto(BASE + "/admin/resultats", wait_until="networkidle")
    expect(page.locator("#google-metrics")).to_contain_text("Vues de la fiche", timeout=20000)


@step("Journal des décisions : publications journalisées, résultats clôturés par le gardien, export CSV")
def decision_journal(page):
    page.goto(BASE + "/admin/marketing", wait_until="networkidle")
    page.locator("button", has_text="Vérifier maintenant").click()
    expect(page.locator(".alert-success")).to_contain_text("Vérification faite", timeout=120000)
    page.goto(BASE + "/admin/resultats", wait_until="networkidle")
    card = page.locator("#decision-journal")
    expect(card).to_be_visible(timeout=20000)
    total = int(card.locator(".kpi", has_text="Décisions").locator("strong").inner_text())
    closed = int(card.locator(".kpi", has_text="Clôturées").locator("strong").inner_text())
    assert total >= 3, f"{total} décision(s) seulement dans le journal"
    assert closed >= 1, "aucune décision clôturée par le gardien"
    expect(card.locator(".dj-table tbody tr").first).to_be_visible()
    with page.expect_download(timeout=15000) as d:
        card.locator("a", has_text="Exporter (CSV)").click()
    content = open(d.value.path(), encoding="utf-8-sig").read()
    assert content.startswith("decision_id;domaine;publication_id"), content[:80]
    assert content.count("\n") >= total + 1, "lignes manquantes dans l'export"


@step("Pourriel : un message de robot est classé tout seul, sans courriel, et peut être rétabli")
def spam_filter(page):
    page.goto(BASE + "/contact", wait_until="networkidle")
    page.fill("#name", "RobertBuimi")
    page.fill("#email", "robert.buimi.e2e@example.com")
    page.fill("#subject", "Hi, i am writing about the price")
    page.fill("#message", "Hi, i am writing about your the price for reseller")
    page.locator("input[name='Model.ConsentGiven']").check()
    page.locator("form button[type=submit]").first.click()
    page.wait_for_url("**/merci**", timeout=20000)

    page.goto(BASE + "/admin/demandes", wait_until="networkidle")
    expect(page.locator("text=RobertBuimi")).to_have_count(0)
    page.locator(".filters select").first.select_option("Spam")
    expect(page.locator("a", has_text="RobertBuimi").first).to_be_visible(timeout=15000)
    page.locator("a", has_text="RobertBuimi").first.click()
    expect(page.locator(".spam-banner")).to_be_visible(timeout=15000)
    expect(page.locator("text=formule connue des robots").first).to_be_visible()
    page.locator(".spam-banner button", has_text="Ce n'est pas un pourriel").click()
    expect(page.locator(".alert-success")).to_contain_text("remise dans les nouvelles demandes", timeout=15000)

    # Le nettoyage en un clic le reclasse aussitôt.
    page.goto(BASE + "/admin/demandes", wait_until="networkidle")
    page.locator("button", has_text="Nettoyer les pourriels").click()
    expect(page.locator(".alert-success")).to_contain_text("classée(s) en pourriel", timeout=15000)
    expect(page.locator("a", has_text="RobertBuimi")).to_have_count(0)


def save_video(page, video, name):
    """Garde la vidéo produite avec les captures (branche « apercus »), pour la regarder."""
    import os
    src = video.get_attribute("src")
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "apercus")
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, name), "wb") as f:
        f.write(page.request.get(src if src.startswith("http") else BASE + src).body())


@step("Reels : avant/après téléversé, monté, refait par l'IA (simulée) puis publié en Reel")
def reels(page):
    import os
    clips = os.path.join(os.path.dirname(os.path.abspath(__file__)), "clips")
    page.goto(BASE + "/admin/publications/reel-avant-apres", wait_until="networkidle")
    page.set_input_files("#reel-avant", os.path.join(clips, "avant.mp4"))
    expect(page.locator(".reel-drop", has_text="AVANT")).to_contain_text("avant.mp4", timeout=60000)
    page.set_input_files("#reel-apres", os.path.join(clips, "apres.mp4"))
    expect(page.locator(".reel-drop", has_text="APRÈS")).to_contain_text("apres.mp4", timeout=60000)
    page.fill("#reel-title", "Reel e2e avant/après")
    page.fill("#reel-message", "Une cuisine qui retrouve tout son éclat. Demandez votre devis gratuit !")
    page.locator("button", has_text="Créer le Reel").click()
    page.wait_for_url("**/admin/publications/*", timeout=180000)
    video = page.locator("#reel video.reel-video")
    expect(video).to_have_attribute("src", re.compile(r"\.mp4$"), timeout=30000)
    expect(page.locator("#reel")).to_contain_text("Reel avant/après")
    save_video(page, video, "reel-avant-apres.mp4")

    # L'IA vidéo (simulée) refait le Reel : le budget est compté.
    page.locator("#reel button", has_text="Créer un Reel IA").click()
    expect(page.locator(".alert-success")).to_contain_text("Reel prêt", timeout=300000)
    expect(page.locator("#reel")).to_contain_text("Reel IA")
    save_video(page, page.locator("#reel video.reel-video"), "reel-ia-simule.mp4")

    page.locator("button", has_text="Publier maintenant").click()
    expect(page.locator(".alert-success")).to_contain_text("Publication envoyée", timeout=120000)

    page.goto(BASE + "/admin/resultats", wait_until="networkidle")
    expect(page.locator(".kpi", has_text="Vidéos IA ce mois").locator("strong")).not_to_have_text("0", timeout=20000)


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
    campaign_page(page)
    campaign_wizard(page)
    guardian(page)
    gift_card(page)
    google_business(page)
    decision_journal(page)
    spam_filter(page)
    reels(page)
    browser.close()

if failures:
    for f in failures:
        print(f"::error title=Test de bout en bout::{f}")
    sys.exit(1)
print("::notice::Tests de bout en bout : FAQ, statut des demandes, export CSV, menu mobile, validation du devis, redirections, ancres du menu, plan marketing, calendrier, nouvelle campagne, gardien des relances, carte-cadeau")
