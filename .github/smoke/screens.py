"""Captures d'écran du site (bureau et mobile) et du back-office, pour relecture visuelle."""
import base64
import hashlib
import hmac
import os
import struct
import sys
import time

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
    # Vérification en deux étapes, de bout en bout : activation puis reconnexion avec un code.
    def totp(secret, at=None):
        key = base64.b32decode(secret.upper() + "=" * (-len(secret) % 8))
        counter = int((at or time.time()) // 30)
        digest = hmac.new(key, struct.pack(">Q", counter), hashlib.sha1).digest()
        offset = digest[-1] & 0x0F
        value = struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF
        return f"{value % 1_000_000:06d}"

    page.goto(BASE + "/admin/securite", wait_until="networkidle")
    page.wait_for_timeout(1500)
    secret = page.locator(".manual-key").inner_text().replace(" ", "")
    page.fill(".setup-steps input", totp(secret))
    page.click("text=Activer")
    page.wait_for_selector(".recovery-codes", timeout=10000)
    page.screenshot(path=f"{OUT}/admin-securite-active.png", full_page=True)

    fresh = browser.new_context(viewport={"width": 1366, "height": 900}, locale="fr-CA")
    page2 = fresh.new_page()
    page2.goto(BASE + "/compte/connexion", wait_until="networkidle")
    page2.fill("#email", "admin@smoke.test")
    page2.fill("#password", "SmokeTest-Password-2026")
    page2.click("button[type=submit]")
    page2.wait_for_url("**/compte/connexion-2fa**", timeout=20000)
    page2.screenshot(path=f"{OUT}/admin-connexion-2fa.png")
    # Code de la fenêtre suivante, pour ne pas réutiliser celui de l'activation.
    time.sleep(31 - (time.time() % 30))
    page2.fill("#code", totp(secret))
    page2.click("button[type=submit]")
    page2.wait_for_url("**/admin", timeout=20000)
    print("::notice::Connexion en deux étapes vérifiée de bout en bout")
    browser.close()

print("Captures prêtes")
