# Restricted jurisdictions & geo policy

**Last updated:** 2026-09-15  
**Status:** Policy document for **store distribution and compliance planning**. Desktop GitHub Releases remain a side-load channel; **you** must still obey local law.

Umbrella does not provide legal advice. This list is a **starting point** for store geo requirements and must be reviewed by counsel before enforcing blocks in production.

---

## 1. Principle

- **Self-custody software** does not make illegal use legal.  
- Store platforms may require **availability restrictions** by country.  
- The honest product line (see [MANIFESTO.md](MANIFESTO.md)) still applies: we do not pretend Tor or non-custody erases legal obligations.

## 2. Restricted / high-risk jurisdictions (planned store blocklist)

Umbrella Wallet **store builds** are intended **not** to be offered in jurisdictions with comprehensive crypto bans or sanctions-heavy regimes, including (non-exhaustive):

| Region | Reason (summary) |
|---|---|
| People’s Republic of China | Broad prohibition on crypto trading / related services |
| Russian Federation | Significant crypto transaction / service restrictions |
| Islamic Republic of Iran | Sanctions / restricted financial environment |
| Democratic People’s Republic of Korea | Comprehensive sanctions |
| Cuba | Comprehensive sanctions |
| Syrian Arab Republic | Comprehensive sanctions |

Additional regions or U.S. states (e.g. regimes requiring money-transmitter or BitLicense-style registration for certain activities) may be added after legal review — see ROADMAP **L.5 / L.7**.

## 3. What “not available” means

| Channel | Expected behaviour (when L.7 is implemented) |
|---|---|
| Apple / Google / Microsoft store listings | Hidden or blocked in restricted storefronts |
| In-app (store builds) | May refuse first launch based on locale / IP / store region |
| GitHub Releases (side-load) | Still published globally as open-source (MIT) software; **user** must not use where unlawful |

IP-based blocking is imperfect and privacy-sensitive; prefer store-region controls where possible. GPS on desktop is not assumed.

## 4. User responsibility

Regardless of blocks:

> Check local laws before installing or using cryptocurrency software. Circumventing restrictions to break the law is solely your responsibility.

## 5. Implementation backlog

Tracked in [docs/ROADMAP.md](docs/ROADMAP.md) as **L.5** (jurisdictional blocklist) and **L.7** (geo-blocking). Do not ship silent “fake available” UX in banned storefronts.

## 6. Contact

Questions about distribution: [CONTACT.md](CONTACT.md) · [t.me/UmbrellaWallet](https://t.me/UmbrellaWallet)
