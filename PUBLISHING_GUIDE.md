# Guida passo-passo: pubblicare MASTHING su GitHub e ottenere un DOI Zenodo

Questa guida copre tutto il flusso, in italiano, dal repository locale fino al DOI permanente. Segui i blocchi nell'ordine.

---

## 0. Prerequisiti

- Git installato (`git --version`).
- Account GitHub con accesso all'organizzazione [`GeoModelLab`](https://github.com/orgs/GeoModelLab/).
- Account Zenodo (puoi fare login con GitHub su <https://zenodo.org>).
- Hai il codice pronto nella cartella locale `MASTHING/` (la cartella in cui si trova questo file).

Facoltativo ma raccomandato:

- **ORCID** per ciascun autore → inserirlo dentro `CITATION.cff` al campo `orcid:` (commentato attualmente).
- **GitHub CLI** (`gh`) per creare il repo da terminale senza passare dal browser.

---

## 1. Inizializzare il repository locale e il primo commit

Apri un terminale nella cartella `MASTHING/` (dove c'è `MasThing.sln`):

```bash
# 1.1 Inizializza il repository e scegli main come branch di default
git init -b main

# 1.2 Aggiungi tutti i file (il .gitignore escluderà bin/, obj/, .vs/, ecc.)
git add .

# 1.3 Verifica cosa sarà committato
git status

# 1.4 Primo commit
git commit -m "Initial public release of MASTHING v1.0.0

Process-based individual-tree model of mast seeding in European beech.
Reference: Bregaglio et al. (2026), MASTHING paper."
```

---

## 2. Creare il repository su GitHub

### Opzione A — da browser (più semplice)

1. Vai su <https://github.com/organizations/GeoModelLab/repositories/new>.
2. Compila:
   - **Repository name**: `MASTHING`
   - **Description**: `MASTHING: a process-based model of mast seeding in European beech (Fagus sylvatica)`
   - **Public** ✅
   - Lascia deselezionate tutte le opzioni "initialize with …" (README, .gitignore, LICENSE) perché li abbiamo già in locale.
3. Clicca **Create repository**.
4. Collega il remote e fai push:

```bash
git remote add origin https://github.com/GeoModelLab/MASTHING.git
git push -u origin main
```

### Opzione B — con GitHub CLI

```bash
gh repo create GeoModelLab/MASTHING \
  --public \
  --source=. \
  --description "MASTHING: a process-based model of mast seeding in European beech (Fagus sylvatica)" \
  --push
```

---

## 3. Rifinire la pagina GitHub

Dopo il push, sulla pagina del repo:

- **Topics** (rotellina ⚙ accanto a "About"): aggiungi
  `masting`, `ecology`, `forest-modeling`, `fagus-sylvatica`, `beech`, `process-based-model`, `seed-production`, `climate-change`, `csharp`, `dotnet`.
- **Website**: lascia vuoto o inserisci il DOI Zenodo (dopo step 5).
- **Include in the home page**: spunta **Releases** e **Packages** (Packages no, Releases sì).
- Controlla che sia apparso il pulsante **"Cite this repository"** accanto a "About" — è generato automaticamente leggendo `CITATION.cff`. Se non appare entro qualche minuto, valida il file con <https://citation-file-format.github.io/cff-initializer-javascript/> oppure:

  ```bash
  pip install cffconvert
  cffconvert --validate
  ```

---

## 4. Collegare Zenodo al repository GitHub

1. Vai su <https://zenodo.org/account/settings/github/>.
2. Se è la prima volta, clicca **Connect with GitHub** e autorizza Zenodo ad accedere ai tuoi repository.
3. Nella lista dei repository, trova `GeoModelLab/MASTHING` e sposta l'interruttore su **ON**.
   - Se il repo appartiene all'organizzazione e non lo vedi: clicca **Sync now** in alto; se ancora non appare, un owner dell'organizzazione deve autorizzare Zenodo nell'organizzazione (Settings → Third-party access → OAuth app policy → Approve Zenodo).

Da questo momento in poi, **ogni release GitHub** del repository attiverà un deposito automatico su Zenodo.

---

## 5. Creare la prima release → genera il DOI

Il DOI viene creato solo quando pubblichi una *GitHub Release* (non basta un tag).

### Da browser

1. Vai su `https://github.com/GeoModelLab/MASTHING/releases/new`.
2. **Choose a tag** → scrivi `v1.0.0` e clicca *Create new tag on publish*.
3. **Release title**: `MASTHING v1.0.0 — initial public release`.
4. **Describe this release**: incolla un breve changelog, es.:

   ```markdown
   ## MASTHING v1.0.0

   First public release accompanying Bregaglio et al. (2026).

   - Core model library (`source/`): tree allometry, SWELL phenology, carbon balance, reproductive allocation with three alternative formulations (RB, RB+WC, RBxWC).
   - Runner (`runner/`): console application for forward simulation and calibration.
   - Multi-start downhill simplex optimiser (`optimizer/`).
   - Documentation, MIT licence, CITATION.cff.
   ```
5. **Publish release**.

### Da CLI

```bash
git tag -a v1.0.0 -m "MASTHING v1.0.0 — initial public release"
git push origin v1.0.0

gh release create v1.0.0 \
  --title "MASTHING v1.0.0 — initial public release" \
  --notes-file RELEASE_NOTES.md   # opzionale
```

Dopo 1–2 minuti vai su <https://zenodo.org/account/settings/github/> → vedrai `GeoModelLab/MASTHING` con un **DOI** associato alla release. Copialo: la forma è `10.5281/zenodo.<numero>`.

---

## 6. Aggiornare README e CITATION.cff con il DOI

Torna al repo locale:

```bash
# Sostituisci XXXXXXX con il numero Zenodo (es. 14712345)
# in README.md (badge + blocco BibTeX) e in CITATION.cff (campo doi del preferred-citation,
# se vuoi anche citare il software con DOI)
```

Puoi farlo con `sed`:

```bash
# macOS/BSD:
sed -i '' 's/zenodo.XXXXXXX/zenodo.14712345/g' README.md CITATION.cff
# Linux/GNU:
sed -i 's/zenodo.XXXXXXX/zenodo.14712345/g' README.md CITATION.cff
```

Poi committa e pusha:

```bash
git add README.md CITATION.cff
git commit -m "Add Zenodo DOI badge and citation metadata"
git push
```

---

## 7. Release successive

Per ogni versione successiva (bug-fix, nuove feature, nuovi articoli):

```bash
# 7.1 Aggiorna la versione dentro CITATION.cff  (es. version: 1.1.0; date-released)
# 7.2 Commit e tag
git add CITATION.cff
git commit -m "Release v1.1.0: <breve descrizione>"
git tag -a v1.1.0 -m "MASTHING v1.1.0"
git push && git push origin v1.1.0

# 7.3 Crea la release su GitHub (via UI o gh release create)
#     Zenodo creerà un nuovo DOI "child" aggregato sotto il Concept DOI
```

**Concept DOI vs Version DOI** — Zenodo genera anche un *Concept DOI* che punta sempre all'ultima versione. Usa quello nei lavori che vogliono "sempre l'ultima MASTHING"; usa il Version DOI specifico quando devi essere riproducibile.

---

## 8. Checklist finale

- [ ] Il repo è pubblico su `github.com/GeoModelLab/MASTHING`.
- [ ] `LICENSE` visibile in homepage.
- [ ] Pulsante **Cite this repository** visibile (CITATION.cff valido).
- [ ] Topics impostati.
- [ ] Connessione Zenodo attiva.
- [ ] Release `v1.0.0` creata.
- [ ] DOI comparso in Zenodo.
- [ ] README aggiornato con badge DOI.
- [ ] ORCID di tutti gli autori inseriti in `CITATION.cff` (consigliato).
- [ ] Dataset osservativo depositato come *separato* record Zenodo (opzionale ma molto utile; il DOI del dataset va linkato nella sezione "Data availability" dell'articolo).

---

## 9. Troubleshooting

- **Il repo non appare in Zenodo**. Clicca *Sync now*. Se è in un'organizzazione, deve essere autorizzata l'app OAuth di Zenodo a livello di organizzazione.
- **Il pulsante "Cite this repository" non appare**. Il `CITATION.cff` è probabilmente non valido. Usa il validator online o `cffconvert --validate`.
- **Push rifiutato per file troppo grandi**. Evita di committare `bin/`, `obj/`, `.vs/`, o dataset grandi: sono già in `.gitignore`. Se hai superato 100 MB, rimuovili dalla storia con `git filter-repo`.
- **DOI non generato anche dopo la release**. Attendi qualche minuto e refresha la pagina Zenodo. Se dopo 10–15 minuti non compare, apri la release e controlla che non sia in stato *draft*.
- **Voglio archiviare anche i dati**. Crea un record Zenodo separato dal tuo account (`New upload`), carica i CSV e riferisci il DOI del software con il campo *Related identifiers* → `isSupplementTo`.

---

Ultima revisione: 2026-04-18.
