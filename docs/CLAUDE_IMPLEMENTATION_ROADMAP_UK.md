# Umbrella Wallet — повний roadmap реалізації для Claude

> **Мета продукту:** зробити Umbrella простим, красивим, приватним і надійним desktop self-custody гаманцем. Користувач за 10 секунд має розуміти: де його кошти, як безпечно отримати або відправити їх, через яку мережу це відбувається і що бачать зовнішні провайдери.
>
> **Головне правило:** безпека коштів, коректність балансу та чесність UI важливіші за кількість мереж, екранів, анімацій і маркетингових заяв.

## 0. Стан на старті цього roadmap

### Уже є і треба зберегти

- Native Avalonia/.NET 8 desktop app; seed зберігається локально у vault Argon2id + AES-256-GCM.
- Реальні send/receive сценарії для частини мереж, QR, backup/restore, auto-lock, Tor/proxy, watch-only адреси, ринкові дані, локальна activity/history.
- `v4.1.0` правильно прибрав з основної навігації NFT, Staking та псевдомобільний desktop mode.
- `v4.2.1` правильно відкотив небезпечну кнопку **Generate new address**: вона могла показати BTC/LTC/DOGE адресу, кошти на які UI не міг повністю бачити та витратити.
- GitHub Release workflow тепер генерує один `SHA256SUMS.txt` для Windows і Linux артефактів.

### Відомі факти, які не можна ігнорувати

1. `DeriveAccounts`, balance refresh, history та Bitcoin-like spend зараз працюють навколо адреси/ключа індексу `0`. Показувати іншу receive-адресу до завершення повного HD-сценарію **заборонено**.
2. Зміна в `desktop/src/Umbrella.Wallet.Core/Derivation/HdAddressDeriver.cs` зараз незакомічена. Не перезаписувати її без погодження; вона вже вводить `DeriveBitcoinLikeAt(change, index)` і може бути основою для фази 1.
3. `docs/01`, `02`, `03`, `08`, `09`, `10`, `11`, `TECH_OVERVIEW.md`, `ACTION_LOG.md` містять застарілий React/NestJS/P2P/Vercel продукт. Вони не є джерелом правди для shipped desktop wallet.
4. Buy і P2P & DEX — це здебільшого каталоги зовнішніх сайтів; вони не мають виглядати як внутрішні фінансові функції.
5. Tor і Monero helper binaries все ще завантажуються build scripts без криптографічної перевірки upstream підпису/хешу. SHA256 готового release цього не замінює.

## 1. Обов'язкові правила роботи для Claude

1. Працюй малими незалежними фазами. Один PR/коміт не повинен одночасно міняти криптографію, UI, документацію та релізний pipeline.
2. Перед зміною відтворюй проблему тестом; після зміни запускай доречні unit, integration та build перевірки.
3. Не оголошуй можливість у README, changelog або UI, доки її end-to-end сценарій не проходить.
4. Не додавай нових монет, P2P, staking, NFT, Android або нових тем, поки не завершені фази 1–5.
5. Не використовуй реальні кошти в automated tests. Для network інтеграцій використовуй mocks/recorded fixtures, а для ручного smoke — окремі testnet/мінімальні mainnet гаманці з нульовою цінністю.
6. Не видаляй чужі незакомічені зміни. Перед початком завжди перевіряй `git status` і ізолюй свій diff.
7. Будь-який новий network endpoint має працювати через спільний `PublicHttp` і успадковувати Tor/custom proxy налаштування, якщо це не loopback daemon.
8. Ніколи не логуй mnemonic, private key, API secret, повну seed phrase або raw signed transaction до її broadcast.
9. Кожен destructive action (reset wallet, restore backup, delete data, rotate secrets) потребує зрозумілого попередження, явного підтвердження та відновлюваного шляху, де це можливо.
10. Жодної мовчазної fallback/demo поведінки для балансу, send, backup або restore. Якщо дані недоступні — показати стан “невідомо / не синхронізовано”, а не `0`.

## 2. Release gates: що має бути правдою перед публічним релізом

Реліз блокується, якщо хоч один пункт нижче не виконано:

- `dotnet test desktop/Umbrella.Wallet.sln` проходить у CI.
- `dotnet build desktop/Umbrella.Wallet.sln -c Release` проходить у CI.
- Версія однакова у `VERSION`, csproj, installer, release назві, README links та changelog.
- Згенеровані Windows installer, portable zip, Linux tar.gz і `SHA256SUMS.txt` присутні в одному GitHub Release.
- Перевірка checksum в CI доводить, що кожен рядок у `SHA256SUMS.txt` відповідає прикріпленому байт-в-байт артефакту.
- Усі підтримувані send paths мають deterministic vectors і тест відмови на некоректній адресі/мережі.
- Відомі обмеження кожної мережі видимі в support matrix і в UI до отримання чи відправлення коштів.
- Release notes не містять слів “ready”, “full”, “private”, “supported”, якщо відповідна acceptance criteria нижче не виконана.

## 3. Фаза 1 — повний HD UTXO wallet (P0, блокер приватного receive)

### Ціль

Безпечно підтримати кілька BTC/LTC/DOGE receive-адрес, агрегувати їх баланс та історію, витрачати UTXO з будь-якої виданої адреси і надсилати change на нову внутрішню адресу.

### 3.1. Модель даних і деривація

1. Завершити/закомітити окремим PR поточний рефакторинг `HdAddressDeriver`.
2. Ввести явну модель, наприклад:

   ```csharp
   UtxoDerivationPath(ChainId Chain, uint Change, uint Index)
   DerivedUtxoAccount(UtxoDerivationPath Path, string Address, Key PrivateKey, Script ScriptPubKey)
   ```

3. Єдине джерело BIP параметрів для BTC/LTC/DOGE: purpose, coin type, network, script type. Address, key і scriptPubKey мають походити з одного методу.
4. `change = 0` — зовнішня receive chain; `change = 1` — внутрішня change chain. Не переобтяжувати `addressIndex` неявним змістом.
5. Оновити `AddressIndexStore` до versioned atomic JSON state на wallet+chain:

   - `LastIssuedExternalIndex`;
   - `LastIssuedInternalIndex`;
   - optional `LastSeenUsedExternalIndex` / `LastSeenUsedInternalIndex`;
   - schema version.

6. Збереження нового receive/change індексу має відбуватися **до** показу адреси або broadcast. При failure запису дія має скасуватися з помилкою, а не бути “best effort”.
7. Включити state у encrypted backup або забезпечити повний restore-by-seed discovery, який не залежить від локального state. Бажано обидва варіанти.

### 3.2. Discovery, sync, balance і history

1. Написати `UtxoAccountDiscoveryService` окремо від ViewModel.
2. Discovery алгоритм для кожної BTC/LTC/DOGE chain:

   - почати з `0`;
   - сканувати external chain щонайменше до `max(LastIssuedExternalIndex, last used) + GapLimit`;
   - сканувати internal chain так само;
   - `GapLimit = 20` як константа, яку можна змінити без формату storage;
   - “used” означає address має confirmed або unconfirmed history/UTXO;
   - не вважати address порожньою тільки через тимчасову мережеву помилку;
   - кешувати timestamp/status, не робити сотні паралельних HTTP запитів;
   - sync має бути cancelable, progress-aware і routed через Tor/proxy.

3. `Accounts`/`Holdings` повинні показувати агрегований баланс мережі, а details page — адреси та їх частки балансу.
4. History має зібрати транзакції всіх discovered адрес, дедуплікувати за txid та правильно визначити internal change, щоб не показувати його як новий receive.
5. При offline/API error баланс має бути `last synced value` з timestamp або `Unavailable`; ніколи не підміняти помилку нулем.
6. Перший unlock/restore може бути довгим. Показати “Discovering BTC addresses — 12/40” і дозволити працювати з уже синхронізованими іншими мережами.

### 3.3. Spending

1. Перепроєктувати `BitcoinTransactionSender` так, щоб він приймав набір discovered UTXO разом з їх source address/path/scriptPubKey, а не один `fromAddress` і один `Key`.
2. Group/select UTXO з усіх зовнішніх та внутрішніх адрес. Для кожного input деривувати точно відповідний private key.
3. Transaction builder повинен мати правильний Coin/scriptPubKey для кожного input і додати всі відповідні keys.
4. Під час create send:

   - резервувати наступний internal change index у durable state;
   - деривувати `m/.../1/N`;
   - додавати change лише якщо він перевищує dust threshold;
   - якщо change немає, не витрачати індекс або дозволити невикористаний gap — але задокументувати поведінку;
   - після broadcast sync має бачити pending change;
   - при broadcast failure не втрачати можливість повторити без плутанини індексів.

5. Confirm screen має показати: total input, user amount, network fee, service fee якщо вона реально є, change amount і факт “change returns to a fresh internal wallet address”.
6. Для swap deposit використати той самий multisource UTXO path, а не окремий legacy path.

### 3.4. UI receive

1. Повернути “Generate new address” **лише після виконання 3.1–3.3**.
2. Receive screen:

   - назва активу і мережі над QR;
   - address label `BTC receive address #N`;
   - Copy, QR, optional amount request URI;
   - “Generate new address” тільки BTC/LTC/DOGE;
   - коротка причина: “Use a fresh address to reduce transaction linking”;
   - посилання “Previous addresses” з балансом і статусом;
   - derivation path тільки в Advanced details, не головним текстом для звичайної людини.

3. Не пропонувати ротацію для account-based chains, доки не буде повного account management UX.

### 3.5. Обов'язкові тести фази 1

- BIP84 BTC, BIP84 LTC, BIP44 DOGE address/key/script vectors для external і internal chain.
- Address #0, #1, #2 відрізняються; повторна деривація стабільна.
- Store versioning, atomic failure, wallet/chain isolation, backup/restore compatibility.
- Discovery fixture: кошти на external #0, #2, internal #0; gap addresses порожні; агрегований баланс правильний.
- History fixture: external receive, outgoing send, internal change; без duplicate і без хибного “received” для change.
- Spend fixture: UTXOs на #0 і #2 в одній tx; кожен input підписаний потрібним ключем; change йде на `change=1`.
- Restore fixture без `addr-indexes.json`: gap scan знаходить funded address #N.
- Negative tests: wrong key, wrong network, partial explorer failure, stale quote, dust change, insufficient funds, reorg/unconfirmed UTXO.
- Manual smoke на окремому мінімальному testnet/mainnet wallet: receive two addresses → reload → restore from backup/seed → send → confirm change visible.

### Definition of Done фази 1

Користувач може отримати BTC/LTC/DOGE на три різні адреси, перезапустити або відновити гаманець, бачити повний баланс/історію і витратити ці кошти без ручної імпортації ключів. Після цього і лише після цього можна анонсувати address rotation як privacy feature.

## 4. Фаза 2 — supply chain і перевірювані релізи (P0)

### 4.1. Закріпити current checksum workflow

1. Додати CI test/script, який створює test artifacts, генерує checksum manifest і перевіряє кожен файл через `sha256sum -c`.
2. Після `gh release download` перевірити, що очікуються рівно installer, portable zip і Linux tarball; fail якщо файл відсутній або назва не відповідає версії.
3. `SHA256SUMS.txt` мусить містити лише release files, бути створений після завершення обох platform jobs і бути прикріплений до того самого tag.
4. Додати release verification checklist до GitHub issue/PR template.

### 4.2. Захист third-party binaries

1. Замінити “latest” Monero URL на pinned version URL.
2. Для Tor і Monero зберігати pinned version, expected SHA-256 та, де upstream це дозволяє, перевіряти офіційний PGP signature/manifest перед extraction.
3. Fail closed: mismatch хешу або відсутній підпис зупиняє release build.
4. Зберегти source URL, version, hash і verification method у `THIRD_PARTY_NOTICES.md` або SBOM.
5. Не комітити бінарники в repo, але зробити staging повністю deterministic.

### 4.3. Code signing і provenance

1. Додати optional signing stage, який активується лише коли secrets/certificate доступні; без сертифіката реліз не має брехати, що він підписаний.
2. Описати окремо, хто зберігає certificate, як працює timestamping і як робиться key rotation. Не зберігати PFX у repo.
3. Pin GitHub Actions до commit SHA або прийнятої організаційної policy, особливо actions, що мають `contents: write`.
4. Додати SBOM/provenance як release asset.
5. До code signing тримати Windows SmartScreen warning у README чесно і видимо.

### Definition of Done фази 2

Будь-який користувач може завантажити всі артефакти, перевірити checksum, побачити склад third-party binaries, а CI доводить, що upload не може пропустити або підмінити checksum manifest.

## 5. Фаза 3 — чесний support matrix і документація (P0/P1)

### 5.1. Єдине джерело можливостей

1. Розширити chain capability metadata, щоб для кожної монети було незалежно визначено:

   - address derivation;
   - receive;
   - balance sync;
   - send;
   - on-chain history;
   - swap input/output;
   - token support;
   - privacy caveats;
   - hardware wallet support;
   - current maturity: stable / beta / experimental / unavailable.

2. UI, README, Market, Receive, Send picker і tests мають читати цю модель, а не дублювати strings.
3. Якщо historical sync відсутній, не писати “full wallet”; показати “Send/receive supported; history not yet synced”.
4. Не змішувати “можна деривувати адресу” з “всі операції підтримані”.

### 5.2. Документація

1. Позначити старі web/backend docs як `archive/legacy-web-2026-07/` або видалити з активного індексу після підтвердження, що вони не потрібні юридично.
2. Створити нові desktop docs:

   - `docs/01-product-scope.md`;
   - `docs/02-architecture-desktop.md`;
   - `docs/03-security-and-privacy.md`;
   - `docs/04-supported-networks.md`;
   - `docs/05-build-release-verify.md`;
   - `docs/06-backup-and-recovery.md`;
   - `docs/07-troubleshooting.md`.

3. Оновити `docs/README.md` і `desktop/README.md`; прибрати стару версію 1.7.0, IndexedDB, JWT, KYC, React/NestJS, Vercel/Render із документації поточного продукту.
4. Зберегти legacy історію лише як історичну, не як інструкцію до запуску.
5. Додати table-driven test або CI lint, що версія та supported-network matrix однакові в машинному manifest і generated docs.

### Definition of Done фази 3

Новий розробник або користувач не може випадково прочитати веб-документацію й подумати, що це current product; перелік функцій на сайті, в app і в тестах збігається.

## 6. Фаза 4 — core UX: Receive, Send, Activity, Backup (P1)

### 6.1. Нова інформаційна архітектура

Основне меню має містити максимум п'ять сутностей:

1. **Wallet** — portfolio, активи, Receive, Send.
2. **Activity** — транзакції, filter, status, explorer copy.
3. **Swap** — лише реальний in-wallet swap; якщо network/asset недоступний, пояснити чому.
4. **Discover** — Market і явно external Buy/P2P/DEX links.
5. **Settings** — wallet, backup, security, privacy, appearance, advanced.

Правила:

- Buy/P2P не можуть називатися “вбудованою біржею”. Кнопка має казати `Open external provider`.
- News не повинні конкурувати з фінансовими діями в primary nav; перенести до About/Updates або зовнішнього каналу.
- Не повертати NFT/Staking, доки там немає завершеної корисної дії та security model.

### 6.2. Receive

- Asset-first flow: обрати монету → побачити конкретну network → QR/address → copy/share.
- Network warning повинен бути візуально сильним: `USDT only on TRON / TRC-20`; не лише в дрібному описі.
- Додати optional payment URI / requested amount там, де стандарт мережі це дозволяє.
- Не показувати “primary address” без asset/network selector.
- Після copy показати short toast і auto-clear clipboard згідно з налаштуванням.

### 6.3. Send

1. Вибір активу показує:

   - доступний баланс;
   - locked/pending balance;
   - еквівалент у display currency;
   - конкретну мережу;
   - підтримувані типи адрес.

2. Додати `Max`, але сума має лишати кошти на network fee. Пояснити, якщо неможливо відправити весь баланс.
3. Додати локальну address book:

   - label, network, address, optional note;
   - перевірка address format при збереженні;
   - confirmation перед першим send на новий контакт;
   - no cloud sync без окремого opt-in;
   - encrypted/local backup behavior documented.

4. Під час paste адреси:

   - нормалізувати whitespace;
   - одразу перевірити format/network;
   - показати checksum/network result;
   - попереджати, якщо адреса відрізняється від щойно вставленої/збереженої;
   - не обіцяти захист від malware, але рекомендувати звірити перші/останні символи.

5. Fee UX:

   - показати total debit, receiver amount, network fee, service fee окремо;
   - використовувати “estimated” лише там, де це справді estimate;
   - дати normal/fast/custom fee тільки для мереж, де реалізація може це гарантувати;
   - не додавати красиві disabled controls без робочої поведінки.

6. Final review має містити asset, network, full destination, label, amount, total debit, fees, change, irreversible warning і explicit confirm.

### 6.4. Activity і history

- Один Activity screen замість дублювання “Transactions” і “Activity”, якщо різниця не очевидна.
- Filters: all / sent / received / swaps / system; asset; date.
- Status: pending, confirmed, failed, unknown; last refresh timestamp; retry button.
- Не відкривати explorer автоматично, якщо це обходить Tor. Copy link + пояснення — правильний default.
- Історія має бути синхронізована з фактично підтримуваними адресами/мережами, а не виглядати повною там, де вона часткова.

### 6.5. Backup і recovery

- Додати `Verify backup`: прочитати щойно експортований encrypted backup, перевірити manifest і показати success без розкриття seed.
- Додати guided restore test на порожньому профілі або dry-run, який не перезаписує active vault.
- Перед reset/delete показувати, чи backup verified і коли phrase востаннє підтверджували.
- Пояснити різницю між vault password і 12/24-word recovery phrase.

### Definition of Done фази 4

Новий користувач без крипто-досвіду може створити гаманець, підтвердити backup, отримати правильний asset на правильну мережу, відправити малу суму та перевірити статус без пошуку інструкцій у Telegram.

## 7. Фаза 5 — приватність, безпека і довіра в UI (P1)

1. На видимому рівні показати connection state: `Tor connected`, `Direct connection`, `Custom SOCKS5`, `Offline`; не ховати це лише в Settings.
2. Для кожного network request класу задокументувати: RPC/explorer/market/exchange; чи бачить він адресу, IP, API key; чи проходить він через Tor.
3. На first run пояснити: direct public RPC може бачити IP + public address; Tor зменшує зв'язування, але не робить blockchain приватним.
4. Auto-lock має реагувати на всі реальні user activity events (keyboard, pointer, scroll/touch) і не блокувати користувача посеред активної форми без прогнозованого повідомлення.
5. Screenshot guard чесно позначити Windows-only, доки еквівалент не реалізовано та не протестовано на Linux.
6. Додати privacy regression tests: public endpoint у network layer не обходить proxy, loopback Monero RPC ніколи не йде через proxy.
7. Зробити external exchange API keys advanced/opt-in flow із чітким read-only permission checklist, encrypted local storage та швидким disconnect.

## 8. Фаза 6 — дизайн-система, доступність і maintainability (P1/P2)

### 8.1. Design system

- Визначити spacing scale, typography scale, button variants, status colors, cards, form errors і loading skeletons у shared resources.
- Default theme має бути стриманою, контрастною та фінансово зрозумілою. Rain/aurora/stickers — opt-in appearance, не заважають critical data.
- Пріоритет візуальної ієрархії: balance → primary actions → asset list → pending/safety notices → decoration.
- У таблицях на вузьких вікнах лишати asset, amount, fiat value, 24h; другорядне переносити до details.
- Усі error, empty, loading та offline states мають бути спроєктовані, а не випадкові TextBlock messages.

### 8.2. Accessibility

- Keyboard navigation для всіх critical дій.
- Видимий focus state.
- Мінімальний контраст WCAG AA для тексту, status colors не як єдиний носій сенсу.
- Локалізувати всі critical send/receive/backup/security strings; жодних hardcoded English рядків у фінансовому flow.
- Не обрізати адреси в єдиному місці, де користувач має перевірити повний destination.

### 8.3. Кодова структура

1. Поступово розбити `MainWindow.axaml` і `MainViewModel.cs` на feature views/viewmodels:

   - `PortfolioView`;
   - `ReceiveView`;
   - `SendView`;
   - `ActivityView`;
   - `SwapView`;
   - `DiscoverView`;
   - `SettingsView`;
   - shared wallet/session state.

2. Винести network, address discovery, transaction preparation і storage з ViewModel у testable services.
3. Не робити великий “rewrite PR”. Переносити один екран за раз без зміни behavior, покривати snapshot/UI test, потім покращувати UX.
4. Додати UI automation smoke suite: create/import → unlock → receive → send validation → backup export/verify → lock.

## 9. Фаза 7 — лише після core: hardware, нові мережі та розширення (P2)

### Hardware / cold storage

1. Спочатку Bitcoin PSBT export/import і watch-only xpub wallet.
2. Потім Ledger/Trezor через офіційно підтримувані бібліотеки/bridge, із явним confirmation на пристрої.
3. Hardware signing не повинен вимагати введення seed у Umbrella.
4. Потрібні attack-model, device disconnect/error states, integration vectors і ручна QA матриця до “supported” badge.

### Нові мережі та токени

- Додавати мережу тільки разом із receive, balance, send, address validation, fee quote, transaction history/status, recovery matrix, test vectors і support matrix entry.
- Не додавати токен лише в Market, якщо користувач не може зрозуміти, що він не holdable/sendable.

### Swap / Discover

- Розширювати Swap лише після reliability tests для quote expiry, slippage, refund, vault rotation, tracking і failed broadcast.
- Buy/P2P лишаються зовнішніми каталогами, доки немає окремого юридичного, security та UX рішення. Не інтегрувати KYC/payment data у core wallet випадково.

### NFT / Staking

- Не повертати до основного меню без конкретної користі.
- NFT: ownership sync, privacy policy для metadata/images, spam NFT policy, send flow, network support.
- Staking: validator selection, APY/APR source/time, lockup/slashing risks, transaction builder, unbonding state, tax/risk disclosure.

### Android

- Не переносити desktop layout буквально.
- Перед початком створити окремий mobile-first UX spec, threat model для mobile keystore/biometrics, background/network lifecycle і manual device QA matrix.

## 10. QA матриця, яку Claude має вести в кожному PR

| Рівень | Мінімум |
|---|---|
| Unit | validation, derivation, encoding, storage migrations, fee calculations, no-secret logging |
| Integration | mocked RPC/explorer responses, retry/timeouts, Tor proxy routing, transaction preparation/signature verification |
| UI | onboarding, language switch, receive, send validation/review, backup/restore, lock, error/empty/loading states |
| Security | malformed vault/backup, wrong password, address/network mismatch, transaction quote changes, KDF limits, dependency scanning |
| Release | version consistency, artifacts, checksum verification, helper-binary integrity, fresh-install smoke |
| Manual | Windows + Linux, normal display + small display, offline, Tor unavailable, slow RPC, corrupted cache, existing wallet migration |

## 11. Заборонений backlog до закриття P0/P1

- Нові декоративні теми, rain, stickers, gamification.
- Нові “coming soon” вкладки.
- Заяви про Android readiness.
- Розширення P2P/on-ramp у внутрішні платіжні сценарії.
- Нові мережі без повного capability matrix.
- Повернення кнопки fresh UTXO address до завершення фази 1.
- Формулювання “anonymous” без показу direct/Tor privacy caveat.

## 12. Рекомендований порядок PR

1. `docs: declare desktop product source of truth and archive legacy web docs`
2. `test: add release manifest verification and version consistency checks`
3. `security: verify pinned Tor and Monero upstream artifacts`
4. `core: model explicit UTXO derivation paths and versioned address state`
5. `core: discover UTXO address chains and aggregate balances/history`
6. `core: sign UTXOs from all discovered paths and use internal change`
7. `ui: restore safe fresh receive address flow with address history`
8. `ux: simplify navigation and move external services into Discover`
9. `ux: redesign receive/send and add local address book`
10. `ux: unify activity/history and implement backup verification`
11. `privacy: connection-status UI and privacy disclosure`
12. `refactor: split MainWindow/MainViewModel feature by feature with UI smoke coverage`
13. `hardware: PSBT watch-only MVP`

## 13. Final product bar

Umbrella можна називати комфортним і надійним private desktop wallet лише коли:

- він ніколи не показує адресу, баланс якої не здатен знайти та витратити;
- він не плутає зовнішнє посилання з внутрішньою функцією;
- критичні фінансові дії локалізовані, зрозумілі й перевіряються без інструкцій;
- користувач бачить стан приватності з першого екрана;
- backup/recovery і release verification мають перевірюваний сценарій;
- документація, UI, release notes і фактичний код говорять однакову правду.
