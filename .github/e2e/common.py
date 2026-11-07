"""Outils partagés des tests de bout en bout : connexion au back-office (avec double vérification)."""
import base64
import hashlib
import hmac
import os
import struct
import time

SESSION_FILE = "/tmp/hub-session.json"
EMAIL = "admin@smoke.test"
PASSWORD = "SmokeTest-Password-2026"


def totp(secret):
    key = base64.b32decode(secret.upper() + "=" * (-len(secret) % 8))
    digest = hmac.new(key, struct.pack(">Q", int(time.time() // 30)), hashlib.sha1).digest()
    offset = digest[-1] & 0x0F
    return f"{(struct.unpack('>I', digest[offset:offset + 4])[0] & 0x7FFFFFFF) % 1_000_000:06d}"


def session(browser, base):
    """Se connecte une seule fois et garde la session dans un fichier, réutilisé par tous les tests."""
    if os.path.exists(SESSION_FILE):
        return SESSION_FILE
    ctx = browser.new_context(locale="fr-CA")
    page = ctx.new_page()
    page.goto(base + "/compte/connexion", wait_until="networkidle")
    page.fill("#email", EMAIL)
    page.fill("#password", PASSWORD)
    page.click("button[type=submit]")
    page.wait_for_url(lambda u: "/admin" in u or "connexion-2fa" in u, timeout=20000)
    if "connexion-2fa" in page.url:
        with open("/tmp/hub-totp-secret") as f:
            secret = f.read().strip()
        time.sleep(31 - (time.time() % 30))  # un code neuf : le précédent a déjà servi
        page.fill("#code", totp(secret))
        page.click("button[type=submit]")
        page.wait_for_url("**/admin", timeout=20000)
    ctx.storage_state(path=SESSION_FILE)
    ctx.close()
    return SESSION_FILE
