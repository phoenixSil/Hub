"""Captures d'écran du site (bureau et mobile) et du back-office, pour relecture visuelle."""
import os
import sys

from playwright.sync_api import sync_playwright

BASE = sys.argv[1].rstrip("/")
OUT = sys.argv[2]
os.makedirs(OUT, exist_ok=True)

PUBLIC = [
    ("accueil", "/"),
    ("service", "/services/nettoyage-residentiel"),
    ("aines", "/aines/aide-a-domicile"),
    ("secteur", "/secteurs/sainte-foy"),
    ("devis", "/devis"),
    ("rendez-vous", "/rendez-vous"),
    ("faq", "/faq"),
    ("404", "/page-inconnue"),
]
ADMIN = [
    ("admin-tableau", "/admin"),
    ("admin-demandes", "/admin/demandes"),
    ("admin-services", "/admin/services"),
    ("admin-entreprise", "/admin/entreprise"),
    ("admin-securite", "/admin/securite"),
]

with sync_playwright() as p:
    browser = p.chromium.launch()

    for label, width, height, mobile in [("bureau", 1366, 900, False), ("mobile", 390, 844, True)]:
        ctx = browser.new_context(viewport={"width": width, "height": height}, is_mobile=mobile, device_scale_factor=1, locale="fr-CA")
        page = ctx.new_page()
        for name, path in PUBLIC:
            page.goto(BASE + path, wait_until="networkidle")
            page.wait_for_timeout(400)
            page.screenshot(path=f"{OUT}/{label}-{name}.png", full_page=True)
        ctx.close()

    ctx = browser.new_context(viewport={"width": 1366, "height": 900}, locale="fr-CA")
    page = ctx.new_page()
    page.goto(BASE + "/compte/connexion", wait_until="networkidle")
    page.screenshot(path=f"{OUT}/admin-connexion.png")
    page.fill("#email", "admin@smoke.test")
    page.fill("#password", "SmokeTest-Password-2026")
    page.click("button[type=submit]")
    page.wait_for_url("**/admin", timeout=20000)
    for name, path in ADMIN:
        page.goto(BASE + path, wait_until="networkidle")
        page.wait_for_timeout(1500)
        page.screenshot(path=f"{OUT}/{name}.png", full_page=True)

    # Une demande ouverte
    page.goto(BASE + "/admin/demandes", wait_until="networkidle")
    page.wait_for_timeout(1500)
    link = page.locator("a[href^='/admin/demandes/']").first
    if link.count() > 0:
        link.click()
        page.wait_for_timeout(2000)
        page.screenshot(path=f"{OUT}/admin-demande.png", full_page=True)
    browser.close()

print("Captures prêtes")
