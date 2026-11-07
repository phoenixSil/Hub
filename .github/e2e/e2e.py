"""
Parcours de bout en bout dans un vrai navigateur : ce que fait l'administrateur se voit sur le site.

1. Une question ajoutée dans le back-office apparaît sur la page FAQ publique.
2. Le statut d'une demande se change et reste enregistré.
3. L'export des demandes (CSV) se télécharge.
4. Le menu du back-office se replie et s'ouvre sur téléphone.
5. Le formulaire de devis public refuse un envoi vide et affiche les erreurs.

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


@step("Devis public : un envoi vide affiche les erreurs")
def quote_validation(page):
    page.goto(BASE + "/devis", wait_until="networkidle")
    page.locator("form button[type=submit]").first.click()
    page.wait_for_timeout(1500)
    expect(page.locator(".field-error, .validation-message, .alert-error").first).to_be_visible()


with sync_playwright() as p:
    browser = p.chromium.launch()
    state = session(browser, BASE)
    ctx = browser.new_context(viewport={"width": 1366, "height": 900}, locale="fr-CA", storage_state=state, accept_downloads=True)
    page = ctx.new_page()
    faq(page)
    lead_status(page)
    csv_export(page)
    mobile_menu(browser, state)
    quote_validation(page)
    browser.close()

if failures:
    for f in failures:
        print(f"::error title=Test de bout en bout::{f}")
    sys.exit(1)
print("::notice::Tests de bout en bout : FAQ, statut des demandes, export CSV, menu mobile, validation du devis")
