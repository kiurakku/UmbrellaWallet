# Umbrella Wallet — єдиний roadmap

**Версія продукту:** див. [`VERSION`](../VERSION) (зараз **4.7.0**).  
**Дата зведення:** 2026-09-15 (оновлено: чесність UI / fail-closed / verify-yourself з аудиту vs [`MANIFESTO.md`](../MANIFESTO.md)).  
**Призначення:** один документ «що ще зробити» — зібрано з README, CHANGELOG, `SECURE_ANON_ROADMAP`, `CLAUDE_IMPLEMENTATION_ROADMAP_UK`, `12-coins-and-chains`, `PRIVACY`, `THREAT_MODEL`, `security-model`, `BUILD_VERIFY`, і з зауважень щодо відповідності філософії манифесту.

Легенда: ✅ є · 🟡 частково · ⏳ наступне · 📅 заплановано · ❌ не планується / заблоковане.

Довгі технічні фази для реалізації лишаються в [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md).  
Деталі приватності — у [`SECURE_ANON_ROADMAP.md`](SECURE_ANON_ROADMAP.md).  
Матриця мереж — у [`12-coins-and-chains.md`](12-coins-and-chains.md).  
Як перевірити самому — заплановано як [`VERIFY_YOUR_WALLET.md`](VERIFY_YOUR_WALLET.md) (ще не написано; див. §9 і P1.20).

---

## 1. Головне правило

Безпека коштів, коректний баланс і **чесність UI** важливіші за кількість монет і екранів.  
Не анонсувати «ready / full / private», доки end-to-end сценарій не проходить і не покритий тестами.

Філософія ([`MANIFESTO.md`](../MANIFESTO.md)): користувач має **перевіряти**, а не **вірити**. Обіцянка без механізму самоперевірки і без тексту «що саме не захищено» — це маркетингова хмара, навіть якщо код хороший.

**Release gate (коротко):** зелений `dotnet test` + `Release` build · однакова версія всюди · артефакти Windows/Linux + `SHA256SUMS` · send-path має vectors · обмеження монети видимі в UI · fail-closed: мережева помилка ≠ нульовий баланс.

**Покриття філософії (орієнтир аудиту):** ~85% — прогалини саме в комунікації обмежень, fail-closed для balance/send і «перевір сам».

---

## 2. Що вже зроблено (не повторювати як «todo»)

| Область | Статус |
|---|---|
| Non-custodial vault (Argon2id + AES-256-GCM), BIP39, screenshot-guard seed | ✅ |
| Tor + kill-switch fail-closed, Verify Tor, custom SOCKS5 | ✅ |
| Вибір RPC/explorer на мережу, список «хто бачить адреси» | ✅ (4.7) |
| Monero full wallet; BTC/LTC/BCH/DOGE HD scan + fresh receive | ✅ |
| ETH + L2 (Arbitrum/Base/Optimism/Linea) send; zkSync — balance only | ✅ / 🟡 |
| THORChain swaps, Security Center, Privacy Radar, coin control (BTC/LTC/DOGE) | ✅ (Radar є; **пояснення меж — див. P1.11**) |
| Address poisoning / EIP-55 / reuse warnings | ✅ |
| Desktop Win/Linux, themes, 6 мов, encrypted backup | ✅ |
| SHA256SUMS на релізах | ✅ |

---

## 3. Пріоритетна черга (що робити далі)

### P0 — безпека коштів і чесність (fail-closed)

| # | Завдання | Джерело | Статус |
|---|---|---|---|
| **P0.0** | **Full restore proof (критично).** Тест: новий wallet → ≥20 receive-адрес → кошти на адресу **#15** → видалити локальний state → відновити **лише з seed** + BIP44/gap limit → повний баланс знайдено. Без state — тільки повний HD-скан. Без цього self-custody — фікція. | Аудит vs MANIFESTO §6 | 🏆 ⏳ |
| P0.1 | Закрити прогалини HD UTXO edge-case (gap scan, change не губиться в UI) — після / разом із P0.0 | Claude §3 | 🟡 частково в 4.5–4.7 |
| P0.2 | Pin + перевірка SHA/PGP для **Tor** і **monero-wallet-rpc** у fetch-скриптах (fail-closed) | Claude §4.2, BUILD_VERIFY | ⏳ |
| P0.3 | CI: перевірка `SHA256SUMS` ↔ прикріплені артефакти байт-в-байт | Claude §4.1 | ⏳ |
| P0.4 | Єдиний machine-readable **capability matrix** → UI + README + тести (без розбіжностей) | Claude §5.1, coins doc | ⏳ |
| P0.5 | zkSync Era **send** або чесно лишити Receive-only з gas-поясненням (не «Ready») | coins / CHANGELOG 4.7 | 🟡 |
| **P0.6** | **Fail-closed balance.** Якщо всі RPC/ноди недоступні (напр. 3+ спроби) → UI: «Balance unavailable (network error)» + Refresh. **Не** показувати кеш як актуальний баланс; кеш лише з явною міткою «last synced …» / «unknown», ніколи як живий `0.0000` через помилку. Офлайн-тест обовʼязковий. | MANIFESTO §4, аудит | ⏳ |
| **P0.7** | **Send-path transport gate.** Перед Send/Review перевірити, що фактичний шлях (Tor / Clearnet / Custom) = налаштування користувача (+ kill-switch). Порушення → **FAIL**, відправка заборонена; жодного тихого fallback на clearnet. | MANIFESTO §3–4, аудит | ⏳ |
| **P0.8** | **Network isolation CI.** Запуск у пісочниці / з firewall без clearnet: процес гаманця з увімкненим Tor-only **не** намагається встановити clearnet-зʼєднання. Без цього kill-switch у проді не доведений. | Аудит | ⏳ |

### P1 — приватність, довіра, UX ядра (чесність UI)

| # | Завдання | Джерело | Статус |
|---|---|---|---|
| P1.1 | **Duress / decoy password** (другий пароль → decoy vault) | README, SECURE 4.4–4.5, MANIFESTO | ⏳ код wipe/passphrase частковий |
| P1.2 | Повернути/доробити **hidden wallet** unlock UI (BIP39 passphrase), якщо продукт це обіцяє | SECURE 4.4 | 🟡 |
| P1.3 | **Panic / duress wipe** з явним тригером | SECURE 4.5 | 🟡 DataWiper без UX |
| P1.4 | Transaction **simulation** перед Confirm (що саме зміниться on-chain) | README Next | ⏳ |
| P1.5 | One-switch private send — довести UX до «один тумблер = повний checklist» на всіх UTXO-ланцюгах | README / CHANGELOG 4.7 | 🟡 |
| P1.6 | Connection status у primary UI: Tor / Direct / Custom / Offline | Claude §7 | ⏳ |
| P1.7 | Screenshot-guard чесно позначити **Windows-only**, поки немає Linux | Claude §7 | ⏳ |
| P1.8 | Verify backup (без розкриття seed) + guided restore dry-run | Claude §6.5 | 🟡 verify є частково |
| P1.9 | Address book: локально, з format-check, confirm на перший send | Claude §6.3 | 🟡 / перевірити стан |
| P1.10 | Звести Activity в один екран + фільтри + чесні partial-history labels | Claude §6.4 | ⏳ |
| **P1.11** | **Privacy Radar — межі під кожним статусом.** Обовʼязковий текст: *що захищено* і *що ні* (напр. «Tor ховає IP, але вибраний explorer бачить ваші адреси»). Різниця «Tor працює» ≠ «IP приховано від того, кому вже відправили адресу». Без цього Radar порушує MANIFESTO §1–2. | MANIFESTO, аудит | ⏳ |
| **P1.12** | **«What leaked?» після send.** Короткий звіт: IP приховано так/ні · адреси бачили Node X · broadcast через Tor/Direct · coin control / свіжий change увімкнено чи ні. Користувач бачить ціну приватності цієї операції. | MANIFESTO §1–2, аудит | ⏳ |
| **P1.13** | **Duress test scenario.** Сценарій QA: «інспектор змушує» → decoy vault відкривається, справжні кошти лишаються недоступними з decoy-паролем. Без цього P1.1–P1.3 — фічі, а не перевірені рішення. | MANIFESTO intro, аудит | ⏳ |
| **P1.20** | **Self-verify mode** (довго, але по філософії обовʼязково). CLI або Debug-панель: перевірка відсутності clearnet у зʼєднаннях гаманця; експорт xpub → баланс у сторонньому сканері; Tor через `curl --socks5-hostname`; звірка з `VERIFY_YOUR_WALLET.md`. | MANIFESTO, аудит | 📅 Long |

### P2 — on-chain privacy «важка артилерія»

| # | Завдання | Джерело | Статус |
|---|---|---|---|
| P2.1 | **Taproot (BIP-341/86)** — scanner `m/86'` + key-path spend, не лише derivation | PRIVACY, coins | 📅 |
| P2.2 | **PayJoin (BIP-78)** | PRIVACY, threat model | 📅 |
| P2.3 | **CoinJoin** (після PayJoin) | PRIVACY | 📅 |
| P2.4 | **Dandelion++** (broadcast timing privacy) | coins roadmap | 📅 |
| P2.5 | **Silent Payments** (BTC) | coins long-term | 📅 |
| P2.6 | Zcash **shielded** send (зараз лише transparent t-addr) | coins / CHANGELOG | 📅 |

### P2 — токени й мережі (повний цикл або не додавати)

Правило: нова мережа/токен = derive + validate + balance + send + fee + history/status + tests + рядок у matrix.

| # | Завдання | Статус |
|---|---|---|
| N.1 | Send **будь-який ERC-20** (не лише native ETH / окремий USDT) | 📅 near |
| N.2 | Send **будь-який TRC-20** (не лише USDT) | 📅 near |
| N.3 | Send **будь-який SPL** / повні Jetton send (баланси Jetton уже є) | 📅 near / 🟡 |
| N.4 | **XRP** | 📅 |
| N.5 | **Stellar (XLM)** | 📅 |
| N.6 | **Cosmos (ATOM)** / IBC | 📅 |
| N.7 | **NEAR** | 📅 |
| N.8 | **Polkadot (DOT)** | 📅 |
| N.9 | Розширення THORChain / swap reliability (expiry, slippage, refund, failed broadcast) | 📅 після core |

### P2 — hardware, платформи, релізна довіра

| # | Завдання | Статус |
|---|---|---|
| H.1 | Bitcoin **PSBT** export/import + watch-only xpub | 📅 |
| H.2 | **Ledger / Trezor** (підпис на пристрої, без seed у Umbrella) | 📅 |
| H.3 | **Multisig** 2-of-3 | 📅 long |
| H.4 | **Android** (окремий mobile threat model + UX, не копія desktop) | 📅 Planned |
| R.1 | **Reproducible builds** + published attestations (честно: installer .NET single-file не bit-identical) | 🟡 docs / ⏳ attestations |
| R.2 | **Code signing OV/EV (SmartScreen)** | ⏳ **почати за ≥60 днів до store-релізу**; потрібна юрособа; ~$300–1000/рік (див. R.6) |
| R.3 | Підпис релізів **GPG / Sigstore** | 📅 |
| R.4 | SBOM / provenance як release asset | 📅 |
| R.5 | Зовнішній **security audit** — статус у [`../AUDIT_STATUS.md`](../AUDIT_STATUS.md) | 📅 Planned |
| **R.6** | **EV Code Signing** (~$500–800/рік) або OV для Windows build — без цього SmartScreen / Store блокують unsigned exe | ⏳ |
| **R.7** | **Microsoft Store** (опційно): окрема збірка під Store / підпис Microsoft | 📅 |

### P2 / Legal — комплаєнс магазинів (див. §9 + кореневі юр. файли)

| # | Завдання | Статус |
|---|---|---|
| **L.0** | Юр. docs у репо: [`TERMS_OF_SERVICE.md`](../TERMS_OF_SERVICE.md), [`PRIVACY_POLICY.md`](../PRIVACY_POLICY.md), [`APP_STORE_NOTES.md`](../APP_STORE_NOTES.md), [`GEO_BLOCKING.md`](../GEO_BLOCKING.md), [`CONTACT.md`](../CONTACT.md) | ✅ docs (2026-09-15) |
| **L.1** | **Apple / first-run:** екран — «не зберігаємо ключі; ви відповідаєте за backup» | ⏳ код |
| **L.2** | **No «Fully Private» claims** у UI/listing — керуватися APP_STORE_NOTES | ⏳ audit UI strings |
| **L.3** | **Age gate 18+** при першому запуску | ⏳ код |
| **L.4** | **Google Play / store copy:** «Not a financial service…» | ⏳ при подачі |
| **L.5** | **Jurisdictional blocklist** — політика в GEO_BLOCKING.md | 📅 enforce |
| **L.6** | **Linux package signing** (PGP для Flatpak/Snap / distro repos) | ⏳ |
| **L.7** | **Geo-blocking** у store-builds (IP/locale) після юр. консультації | 📅 |
| **L.8** | **ToS / Privacy Policy acceptance** при першому запуску | ⏳ код |

### P3 — продукт / maintainability

| # | Завдання | Статус |
|---|---|---|
| M.1 | Розбити `MainWindow` / `MainViewModel` на feature views (по одному екрану) | 🟡 partial classes вже є |
| M.2 | UI automation smoke: create → unlock → receive → send validate → backup → lock | ⏳ |
| M.3 | Design system + WCAG AA на критичних екранах | ⏳ |
| M.4 | Price alerts | 📅 |
| M.5 | Більше exchange connectors (read-only) за запитом | 📅 |
| M.6 | NFT / Staking — **не повертати** в primary nav без повної моделі + spam policy | ❌ / пізніше |
| **M.7** | **Documentation audit (щорічно):** незалежний перегляд README, PRIVACY, THREAT_MODEL, MANIFESTO, цього ROADMAP на узгодженість з кодом. Документи старіють швидше за код. | Аудит | 📅 recurring |

---

## 4. Матриця монет (зведення)

| Символ | Receive | Balance | Send | History | Примітка |
|---|:---:|:---:|:---:|:---:|---|
| BTC | ✅ | ✅ | ✅ | ✅ | coin control; Taproot spend ще ні |
| LTC | ✅ | ✅ | ✅ | ✅ | |
| BCH | ✅ | ✅ | ✅ | ✅ | HD scan з 4.7 |
| DOGE | ✅ | ✅ | ✅ | ✅ | HD scan з 4.7 |
| ETH | ✅ | ✅ | ✅ | ✅ | ERC-20 send — загальний ще 📅 |
| Arb / Base / OP / Linea | ✅ | ✅ | ✅ | 🟡 | |
| zkSync Era | ✅ | ✅ | ❌ | 🟡 | Receive only (gas) |
| TRX + USDT TRC-20 | ✅ | ✅ | ✅ | ✅ | інші TRC-20 — 📅 |
| SOL | ✅ | ✅ | ✅ | ✅ | довільні SPL send — 📅 |
| TON | ✅ | ✅ | ✅ | ✅ | Jetton balance ✅; Jetton send — 📅 |
| ADA | ✅ | ✅ | ✅ | ✅ | |
| XMR | ✅ | ✅ | ✅ | ✅ | full private |
| AVAX / BNB / MATIC / FTM / CRO | ✅ | ✅ | ✅ | 🟡 | EVM family |
| ZEC | ✅ | ✅ | ❌ | 🟡 | лише transparent `t1…` |
| XRP / XLM / ATOM / NEAR / DOT | — | — | — | — | 📅 Planned |

---

## 5. Відкриті діри threat / privacy (не замовчувати)

| Діра | Що зробити |
|---|---|
| Malware на ПК користувача | Hardware wallet (H.1–H.2) |
| Explorer бачить набір адрес сесії | Вже: вибір ноди + Tor; далі: менше адрес на запит, Silent Payments; **Radar має це казати (P1.11)** |
| UTXO linkage | PayJoin → CoinJoin; Silent Payments; звіт після send (P1.12) |
| Broadcast timing ↔ IP | Dandelion++ |
| Підміна релізу на GitHub | GPG/Sigstore + reproducible attestations |
| $5 wrench | Duress / decoy (P1.1) + сценарій тесту (P1.13) |
| Користувач змушений *вірити* клієнту | Self-verify (P1.20) + `VERIFY_YOUR_WALLET.md` (§10) |
| Немає зовнішнього аудиту | Замовити audit (R.5); доти не писати «audited» |
| Docs розʼїхались із кодом | Щорічний documentation audit (M.7) |
| Відхилення App Store / Play | §9 комплаєнс (L.1–L.8, формулювання) |
| SmartScreen / unsigned Windows | R.6 EV code signing |

---

## 6. Порядок виконання (з точки зору користувача + філософії)

1. **P0.0** — Full restore proof (без цього вся безпека self-custody під питанням).  
2. **P0.6–P0.8** — fail-closed balance + send transport gate + network isolation CI.  
3. **P0.2–P0.4** — supply chain helpers + checksum CI + capability matrix.  
4. **P1.11 / P1.12** — Privacy Radar межі + «What leaked?» (чесність UI).  
5. **P1.1 / P1.13** (+ P1.2–P1.3) — duress/decoy + перевірений сценарій «під тиском».  
6. **P1.4–P1.6** — simulation + connection status + private-send polish.  
7. **P1.20** + §10 — self-verify mode і публічний гайд «як перевірити, що мене не обманюють».  
8. **L.1 / L.3 / L.4 / L.8** (+ L.2 формулювання) — дисклеймери / age / ToS **перед** будь-якою подачею в store.  
9. **R.6 / L.6** — EV signing Windows + PGP Linux packages.  
10. **N.1–N.3** — універсальні токени, лише з повним циклом.  
11. **P2.1–P2.2** — Taproot spend + PayJoin.  
12. **H.1 → H.2** — PSBT, потім Ledger/Trezor.  
13. **H.4** + L.5/L.7 — Android лише після mobile spec **і** store/geo комплаєнсу.  
14. Нові L1 (XRP…) — тільки після стабільного core + matrix.  
15. **R.7 / L.5** — Microsoft Store / geo-blocklist за потреби.

Детальний розклад PR — §12 у [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md).

---

## 7. Що свідомо не планується

| | |
|---|---|
| ❌ | Реклама, телеметрія, analytics |
| ❌ | Custody коштів користувача |
| ❌ | Venture / «growth» фічі замість безпеки |
| ❌ | KYC / on-ramp всередині core wallet без окремого юр. рішення |
| ❌ | Повернення web/backend як частини продукту |
| ❌ | Форки / ребранди без дозволу (див. LICENSE) |
| ❌ | Заяви «anonymous / fully private / untraceable» без тексту меж (і для store — див. §9.4) |
| ❌ | KYC-less **on-ramp** (картка → крипта) всередині app без ліцензії (Play / Apple / MSB) |

---

## 8. Звідки зібрано (джерела правди)

| Документ | Роль |
|---|---|
| Цей файл — **`docs/ROADMAP.md`** | Єдиний backlog «що ще» |
| [`../MANIFESTO.md`](../MANIFESTO.md) | Правила, під які підганяється backlog |
| [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md) | Детальні фази + DoD для імплементації |
| [`SECURE_ANON_ROADMAP.md`](SECURE_ANON_ROADMAP.md) | Pillars безпеки/анонімності + статуси |
| [`12-coins-and-chains.md`](12-coins-and-chains.md) | Повна матриця мереж і «coming soon» |
| [`../README.md`](../README.md) | Публічний короткий roadmap |
| [`../CHANGELOG.md`](../CHANGELOG.md) | Що вже вийшло |
| [`../PRIVACY.md`](../PRIVACY.md), [`../THREAT_MODEL.md`](../THREAT_MODEL.md) | Відкриті gaps |
| [`BUILD_VERIFY.md`](BUILD_VERIFY.md) | Reproducible / verify builds |
| [`../TERMS_OF_SERVICE.md`](../TERMS_OF_SERVICE.md) | Store Terms |
| [`../PRIVACY_POLICY.md`](../PRIVACY_POLICY.md) | Formal privacy policy |
| [`../APP_STORE_NOTES.md`](../APP_STORE_NOTES.md) | Approved store wording |
| [`../GEO_BLOCKING.md`](../GEO_BLOCKING.md) | Restricted jurisdictions |
| [`../AUDIT_STATUS.md`](../AUDIT_STATUS.md) | External audit: none yet |
| [`../CONTACT.md`](../CONTACT.md) | Public contacts |

Оновлюй **цей** файл при зміні пріоритетів; деталі фаз — у спеціалізованих docs, без дублювання суперечливих статусів.

---

## 9. Комплаєнс та юридична безпека (магазини додатків)

Різниця: **як гаманець працює технічно** (Tor, duress, non-custodial) ≠ **як це можна презентувати** в App Store / Play / Microsoft Store. Desktop side-load (GitHub Releases) — низький ризик модерації; мобільні магазини — високий.

| Платформа | Ризик модерації | Примітка |
|---|---|---|
| Apple App Store | **High** | §3.1.5 / payment apps; суворі формулювання |
| Google Play | **Medium** | Crypto / Financial Services Policy з 2022 |
| Microsoft Store | **Medium** | потрібен підпис; без EV — переважно side-load |
| Windows/Linux side-load (GitHub) | **Low** | основний канал зараз |
| Flatpak / Snap / AUR | **Low** | потрібен PGP (L.6) |

### 9.1. Правила платформ (коротко)

| Платформа | Можна | Не можна |
|---|---|---|
| **Apple** | Non-custodial (ключі на пристрої), доступ до власних коштів | Custody без ліцензії; «гарячі» інвестиційні обіцянки; оманливе «fully private» при clearnet leakage адрес |
| **Google Play** | Non-custodial + чіткий дисклеймер; бізнес-верифікація видавця | KYC-less on-ramp без ліцензії; реклама «anonymous transactions» на прозорих чейнах |
| **Microsoft Store** | Підписаний пакет | Unsigned exe як «офіційний» Store listing |
| **Linux repos** | Open-source + PGP | Непідписані пакети в офіційних репо |

### 9.2. Магазини — backlog

| Вимога | Статус | Коментар |
|---|---|---|
| Formal TOS + Privacy Policy + App Store notes + Geo + Contact | ✅ | Кореневі `.md` (2026-09-15) |
| App Store: non-custodial confirmation + L.1/L.3 in **UI** | ⏳ | Документи готові; потрібен first-run екран |
| Google Play: crypto publisher rules + L.4 | ⏳ | Підтвердження бізнесу; **без** KYC-less on-ramp у core |
| Microsoft Store: code signing | ⏳ R.2/R.6 | Без EV — лишаємо GitHub side-load |
| Linux repos: PGP (L.6) | ⏳ | Fedora / Debian / Flathub |

### 9.3. Дисплеймери (обовʼязкові для store / публічного релізу)

| Елемент | ID | Статус |
|---|---|---|
| On-start: not a financial institution + 18+ + non-custodial | L.1, L.3 | ⏳ |
| On-start: no guarantee of anonymity (прозорі чейни) | L.2, текст нижче | ⏳ |
| Privacy warning перед Send (адреса назавжди в ланцюгу; RPC може бачити метадані) | — / P1.12 | 📅 |
| Geo-blocked list | L.5, L.7 | 📅 |
| ToS / Privacy Policy acceptance | L.8 | ⏳ |

**Чернетка on-start (EN, для імплементації L.1/L.2/L.3):**

> **NOT A FINANCIAL INSTITUTION**  
> Umbrella Wallet is non-custodial software. We do not hold your funds, manage your keys, or provide banking services. You are solely responsible for your recovery phrase and for complying with local law.  
>  
> **NO GUARANTEE OF ANONYMITY**  
> Privacy features (Tor, encrypted local storage) do not make a public ledger private. Blockchain analysis can still link activity on transparent chains.  
>  
> **USE AT YOUR OWN RISK**  
> Lost recovery phrases cannot be recovered by anyone. We are not liable for loss due to user error, malware, or regulatory action.

**Чернетка перед Send (EN):**

> **TRANSACTION EXPOSES DATA**  
> Confirming means: your address is permanently on the chain; servers you query may learn metadata even over Tor; coin-control choices can link UTXOs publicly.

### 9.4. Формулювання (store / маркетинг)

| Не використовувати | Використовувати |
|---|---|
| Fully private / anonymous / untraceable | Privacy-enhanced (non-custodial) |
| Anonymous transactions | Non-KYC self-custody; on-chain privacy varies by coin |
| Secure from government | Local encryption; user-held keys |
| No logging (як абсолют) | No cloud account; local-only storage (no telemetry) — див. PRIVACY.md |

Monero можна описувати як privacy coin **чесно**; Bitcoin/ETH тощо — **ніколи** як «anonymous».

### 9.5. Ліцензії та сертифікати

| Сертифікат / крок | Орієнтовна вартість | Навіщо |
|---|---|---|
| EV Code Signing (R.6) | ~$500–800/рік | Windows trust / Store |
| Apple Developer / Google Play Console | щорічні fees | Мобільний listing |
| Business verification | залежить від юрисдикції | Play / Apple publisher |
| Money Transmitter / VASP | дорого | **Лише** якщо зʼявиться custodial on-ramp — зараз **не планується** |
| BitLicense (NY тощо) | — | Non-custodial часто exempt, але **юрист** має підтвердити; L.5/L.7 |

### 9.6. Практичні кроки перед подачею в store

1. Юрособа / developer account (ЄС, Швейцарія чи інша юрисдикція — з юристом).  
2. EV code signing (R.6).  
3. Terms of Service + Privacy Policy + L.8 у додатку.  
4. Скріншоти для ревʼюерів: дисклеймери; доказ що seed/ключі не йдуть на сервер.  
5. Уникати слів зі §9.4 «Не використовувати».  
6. Завжди тримати **side-load** канал: GitHub Releases, FlatHub, AUR — незалежно від store.

**Geo-blocking (L.7)** — політично й технічно чутливо (КНР тощо; окремі штати США). Реалізовувати лише після юридичної консультації; не плутати з цензурою Tor.

---

## 10. Як перевірити, що мене не обманюють (план документа)

Окремий файл **`docs/VERIFY_YOUR_WALLET.md`** (створити разом із P1.20 / R.1–R.3):

1. Команди перевірки Tor-зʼєднання (`curl --socks5-hostname`, відсутність clearnet у netstat для процесу гаманця).  
2. Експорт **xpub** (або watch-only) і звірка балансу в незалежному сканері — без довіри до UI Umbrella.  
3. Верифікація download: `SHA256SUMS` + (коли зʼявиться) GPG/Sigstore; збірка з source за [`BUILD_VERIFY.md`](BUILD_VERIFY.md).  
4. Посилання на Settings → Privacy («хто бачить адреси») і на текст меж Privacy Radar (P1.11).

Доки файла немає — цей розділ є placeholder і пунктом backlog, не готовою інструкцією.
