"""Test de fumée : pages publiques, formulaire de devis, connexion au back-office.
Les résultats sont écrits comme annotations GitHub (::notice / ::error)."""
import http.cookiejar
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

BASE = sys.argv[1].rstrip("/")
failures = []


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
raw = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar), NoRedirect)


def get(path, expect=200, contains=None, follow=True):
    try:
        res = (opener if follow else raw).open(BASE + path, timeout=30)
        code, body = res.status, res.read().decode("utf-8", "replace")
        location = res.headers.get("Location")
    except urllib.error.HTTPError as e:
        code, body, location = e.code, e.read().decode("utf-8", "replace"), e.headers.get("Location")
    ok = code == expect and (contains is None or contains in body)
    status = "OK" if ok else "ÉCHEC"
    print(f"{status} GET {path} -> {code}" + (f" (manque « {contains} »)" if contains and contains not in body else ""))
    if not ok:
        failures.append(f"GET {path} -> {code}, attendu {expect}" + (f" avec « {contains} »" if contains else ""))
    return body, location


def post_form(path, form_name, fields, expect_redirect):
    page, _ = get(path)
    token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page) or \
        re.search(r'value="([^"]+)"[^>]*name="__RequestVerificationToken"', page)
    if not token:
        failures.append(f"Jeton anti-falsification introuvable sur {path}")
        return None
    data = [("_handler", form_name), ("__RequestVerificationToken", token.group(1))] + fields
    req = urllib.request.Request(BASE + path, data=urllib.parse.urlencode(data).encode(), method="POST",
                                 headers={"Content-Type": "application/x-www-form-urlencoded"})
    try:
        res = raw.open(req, timeout=30)
        code, location, body = res.status, res.headers.get("Location"), res.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        code, location, body = e.code, e.headers.get("Location"), e.read().decode("utf-8", "replace")
    ok = code in (302, 303) and location and expect_redirect in location
    print(f"{'OK' if ok else 'ÉCHEC'} POST {path} -> {code} {location}")
    if not ok:
        errors = re.findall(r'class="(?:field-error|alert alert-error)"[^>]*>([^<]+)<', body)
        failures.append(f"POST {path} -> {code} {location} ; erreurs affichées : {errors[:5]}")
    return location


# Pages publiques
get("/", contains="ScintiNet")
get("/services/nettoyage-residentiel", contains="Nettoyage résidentiel")
get("/services/nettoyage-residentiel", contains="<li>Dépoussiérage")
get("/services/entretien-immeubles", contains="immeubles")
get("/aines/aide-a-domicile", contains="Aide à domicile")
get("/aines/perte-d-autonomie")
get("/services/nettoyage-residentiel", contains="à partir de 37 $ l'heure (minimum 3 h)")
get("/aines/aide-a-domicile", contains="Combien ça coûte vraiment")
get("/aines/aide-a-domicile?heures=10", contains="264,00")
get("/secteurs/limoilou", contains="ruelles vertes")
get("/secteurs", contains="Sainte-Foy")
get("/secteurs/sainte-foy", contains="Sainte-Foy")
get("/faq", contains="FAQPage")
get("/a-propos", contains="Notre mission")
get("/confidentialite", contains="Responsable")
get("/devis", contains="devis")
get("/rendez-vous", contains="Plage horaire")
get("/contact", contains="Nous joindre")
get("/merci?demande=devis", contains="bien reçu")
get("/sitemap.xml", contains="/secteurs/sainte-foy")
get("/robots.txt", contains="Sitemap")
get("/services/nexiste-pas", expect=404)
get("/page-inconnue", expect=404)

# Le back-office exige une connexion
_, location = get("/admin", expect=302, follow=False)
if not location or "/compte/connexion" not in location:
    failures.append(f"/admin devrait rediriger vers la connexion, a redirigé vers {location}")

# Envoi d'un devis
post_form("/devis", "devis", [
    ("services", "Nettoyage résidentiel"),
    ("services", "Lavage de vitres"),
    ("Model.PropertyType", "Maison"),
    ("Model.Frequency", "Chaque semaine"),
    ("Model.AreaSlug", "sainte-foy"),
    ("Model.FullName", "Test Fumée"),
    ("Model.Phone", "418 555-0199"),
    ("Model.Email", "fumee@example.com"),
    ("Model.Message", "Demande envoyée par le test de fumée."),
    ("Model.ConsentGiven", "true"),
], "/merci")

# Devis invalide : doit rester sur la page avec des erreurs
page, _ = get("/devis")
post_resp = None
token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page)
if token:
    data = urllib.parse.urlencode([("_handler", "devis"), ("__RequestVerificationToken", token.group(1)),
                                   ("Model.FullName", ""), ("Model.Email", "pas-un-courriel")]).encode()
    try:
        res = raw.open(urllib.request.Request(BASE + "/devis", data=data, method="POST"), timeout=30)
        body = res.read().decode("utf-8", "replace")
        if "Indiquez une adresse courriel valide" in body and "Choisissez au moins un service" in body:
            print("OK  devis invalide : erreurs affichées")
        else:
            failures.append("Le devis invalide n'affiche pas les erreurs attendues")
    except urllib.error.HTTPError as e:
        failures.append(f"Devis invalide -> {e.code}")

# Demande de contact
post_form("/contact", "contact", [
    ("Model.FullName", "Test Fumée"),
    ("Model.Email", "fumee@example.com"),
    ("Model.Message", "Bonjour, ceci est un message de test assez long."),
    ("Model.ConsentGiven", "true"),
], "/merci")

# Connexion au back-office
post_form("/compte/connexion", "login", [
    ("Input.Email", "admin@smoke.test"),
    ("Input.Password", "SmokeTest-Password-2026"),
], "/admin")
get("/admin", contains="Tableau de bord")
get("/admin/demandes", contains="Test Fumée")
for p in ["/admin/services", "/admin/secteurs", "/admin/offres", "/admin/faq", "/admin/avis",
          "/admin/textes", "/admin/pages", "/admin/images", "/admin/entreprise", "/admin/canaux", "/admin/horaires"]:
    get(p)

if failures:
    for f in failures:
        print(f"::error::{f}")
    sys.exit(1)
print("::notice::Test de fumée réussi : pages, devis, contact et back-office OK")
