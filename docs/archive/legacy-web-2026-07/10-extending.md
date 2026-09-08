# 10 — Extending Umbra

## Add a new blockchain

### Requirement
- Must support BIP39/BIP44 (HD wallets) OR SLIP-0010 (ed25519 chains like Solana)
- Must have a way to derive address from seed without making RPC calls
- Must have public block explorer or indexer API for balance queries

### Example: Adding Polygon (EVM-compatible, easy)

**1. Update `walletCore.ts`:**
```typescript
// src/lib/wallet/walletCore.ts

export type WalletChain =
  | "ethereum"
  | "bitcoin"
  | "solana"
  | "tron"
  | "polygon";  // ← ADD THIS

export const DERIVABLE_CHAINS: WalletChain[] = [
  "ethereum",
  "bitcoin",
  "solana",
  "tron",
  "polygon",  // ← ADD THIS
];

export function deriveAddress(mnemonic: string, chain: WalletChain, index = 0): string {
  const seed = seedFromMnemonic(mnemonic);
  const root = HDKey.fromMasterSeed(seed);

  switch (chain) {
    case "polygon": {  // ← ADD THIS CASE
      // Polygon uses same derivation as Ethereum (EVM)
      const child = root.derive(`m/44'/60'/0'/0/${index}`);
      if (!child.publicKey) throw new Error("Failed to derive Polygon key");
      return ethAddressFromPublicKey(child.publicKey);
    }
    // ... rest of cases
  }
}
```

**2. Update backend `linked-wallets.service.ts`:**
```typescript
// backend/src/linked-wallets/linked-wallets.service.ts

const CHAIN_INDEXERS: Record<string, string> = {
  ethereum: "https://api.etherscan.io/api",
  polygon: "https://api.polygonscan.com/api",  // ← ADD THIS
  // ...
};

// Fetch balance method — reuse same RPC logic as Ethereum
async getBalance(address: string, chain: string): Promise<{ native: string; tokens: Token[] }> {
  if (chain === "polygon") {
    // Use ethers.js JsonRpcProvider with Polygon RPC
    const provider = new ethers.JsonRpcProvider("https://polygon-rpc.com");
    const balance = await provider.getBalance(address);
    return { native: ethers.formatEther(balance), tokens: [] };
  }
  // ...
}
```

**3. Update UI asset colors (optional):**
```typescript
// src/hooks/usePortfolio.ts — add color mapping

const ASSET_COLORS: Record<string, string> = {
  BTC: "oklch(0.75 0.18 60)",    // orange
  ETH: "oklch(0.68 0.14 265)",   // purple
  MATIC: "oklch(0.68 0.16 290)", // ← ADD THIS (purple-ish for Polygon)
  // ...
};
```

Done. Polygon addresses now derive locally, balances fetch from PolygonScan API, and appear in portfolio.

---

## Add Monero (different pattern — NOT BIP39)

Monero uses its own 25-word mnemonic (not BIP39). You cannot derive Monero addresses from the same seed as BIP39 wallets.

### Approach: Separate vault record

**1. Generate Monero wallet (separate flow):**
```typescript
// src/lib/wallet/moneroWallet.ts (new file)
import * as monerojs from "monero-javascript";

export async function generateMoneroWallet(): Promise<{
  mnemonic: string;      // 25 words
  address: string;       // primary address
  viewKey: string;       // for balance scanning
  spendKey: string;      // for sending (encrypt this!)
}> {
  const wallet = await monerojs.createWalletFull({
    networkType: "mainnet",
    seed: undefined,  // generates new
  });
  return {
    mnemonic: await wallet.getMnemonic(),
    address: await wallet.getPrimaryAddress(),
    viewKey: (await wallet.getPrivateViewKey()).toString("hex"),
    spendKey: (await wallet.getPrivateSpendKey()).toString("hex"),  // ENCRYPT!
  };
}
```

**2. Encrypt and store separately:**
```typescript
// IndexedDB key: "seed:monero:{userId}"
const blob = await vault.encryptSeed(
  JSON.stringify({ mnemonic, spendKey, viewKey }),
  password,
  userId
);
await idbPut(`seed:monero:${userId}`, blob);
```

**3. Balance scanning:**
Monero balance requires connecting to a Monero daemon and scanning with the ViewKey. Options:
- **Local:** Run `monerod` + `monero-wallet-rpc` on user's machine (desktop app only)
- **Remote:** Connect to public node (e.g., `xmr-node.cakewallet.com:18081`), but this leaks view key to that node (privacy trade-off)

**4. Backend:**
No backend storage — Monero wallet lives entirely client-side. Backend API is not involved.

---

## Add a new language

**1. Add translation file:**
```typescript
// src/lib/i18n.ts

export const SUPPORTED_LANGS = ["uk", "en", "ru", "pl"];  // ← ADD "pl"

export const translations: Record<Lang, Record<string, string>> = {
  uk: { greeting: "Привіт", ... },
  en: { greeting: "Hello", ... },
  ru: { greeting: "Привет", ... },
  pl: { greeting: "Cześć", ... },  // ← ADD THIS
};
```

**2. Update backend enum:**
```typescript
// backend/src/users/dto/update-user.dto.ts

@IsIn(["uk", "en", "ru", "pl"])  // ← ADD "pl"
@IsOptional()
lang?: string;
```

**3. Update UI selector:**
```typescript
// src/routes/settings.tsx — language sheet

{["uk", "en", "ru", "pl"].map((l) => (  // ← ADD "pl"
  <button key={l} onClick={() => setProfile({ lang: l })}>
    {langLabel(l)}
  </button>
))}
```

**4. Add flag/label:**
```typescript
// src/lib/i18n.ts

export function langLabel(l: Lang): string {
  const labels: Record<Lang, string> = {
    uk: "🇺🇦 Українська",
    en: "🇬🇧 English",
    ru: "🇷🇺 Русский",
    pl: "🇵🇱 Polski",  // ← ADD THIS
  };
  return labels[l];
}
```

Done. User can now select Polish in Settings → Language.

---

## Add a new theme (custom color scheme)

**1. Define new theme in `styles.css`:**
```css
/* src/styles.css */

:root[data-theme="cyberpunk"] {
  --primary: 310 100% 50%;           /* magenta */
  --secondary: 280 80% 30%;          /* deep purple */
  --accent: 180 100% 50%;            /* cyan */
  --background: 280 20% 8%;          /* dark purple */
  --foreground: 280 5% 95%;          /* light grey */
  /* ... rest of tokens */
}
```

**2. Add theme switcher:**
```typescript
// src/components/ThemeSwitcher.tsx (new)

const THEMES = ["light", "dark", "cyberpunk"];

export function ThemeSwitcher() {
  const [theme, setTheme] = useState(localStorage.getItem("theme") || "dark");

  const applyTheme = (t: string) => {
    document.documentElement.dataset.theme = t;
    localStorage.setItem("theme", t);
    setTheme(t);
  };

  return (
    <select value={theme} onChange={(e) => applyTheme(e.target.value)}>
      {THEMES.map((t) => <option key={t} value={t}>{t}</option>)}
    </select>
  );
}
```

**3. Load on app start:**
```typescript
// src/routes/__root.tsx — add in useEffect

useEffect(() => {
  const theme = localStorage.getItem("theme") || "dark";
  document.documentElement.dataset.theme = theme;
}, []);
```

---

## Add a new exchange integration (e.g., Binance API for real swaps)

Currently, swap shows quote only — user signs tx manually. To execute trades through an exchange:

**1. Add Binance client:**
```typescript
// backend/src/exchange/binance.service.ts (new)

import { Spot } from "@binance/connector";

export class BinanceService {
  private client: Spot;

  constructor() {
    this.client = new Spot(
      process.env.BINANCE_API_KEY,
      process.env.BINANCE_API_SECRET
    );
  }

  async marketBuy(symbol: string, amount: number): Promise<{ orderId: string }> {
    const { data } = await this.client.newOrder(symbol, "BUY", "MARKET", {
      quoteOrderQty: amount,
    });
    return { orderId: data.orderId };
  }
}
```

**2. Frontend: POST /exchange/execute instead of manual tx signing.**

**⚠️ Legal warning:** Executing trades on behalf of users makes Umbra a **custodian** (you hold their API keys). This voids the non-custodial model and triggers MSB/VASP licensing. Only do this if you're willing to get licensed.

---

## Add WebAuthn / Passkeys for biometric unlock

```typescript
// src/lib/wallet/webauthn.ts (new)

export async function createPasskey(userId: string): Promise<void> {
  const challenge = crypto.getRandomValues(new Uint8Array(32));
  const credential = await navigator.credentials.create({
    publicKey: {
      challenge,
      rp: { name: "Umbra Wallet" },
      user: { id: Uint8Array.from(userId, c => c.charCodeAt(0)), name: userId, displayName: "User" },
      pubKeyCredParams: [{ type: "public-key", alg: -7 }],  // ES256
      authenticatorSelection: { userVerification: "required" },
    },
  });
  // Store credential.id in localStorage: webauthnCredId:{userId}
  localStorage.setItem(`webauthnCredId:${userId}`, (credential as any).id);
}

export async function unlockWithPasskey(userId: string): Promise<boolean> {
  const credId = localStorage.getItem(`webauthnCredId:${userId}`);
  if (!credId) return false;

  const challenge = crypto.getRandomValues(new Uint8Array(32));
  try {
    await navigator.credentials.get({
      publicKey: {
        challenge,
        allowCredentials: [{ type: "public-key", id: Uint8Array.from(atob(credId), c => c.charCodeAt(0)) }],
      },
    });
    return true;  // Face ID / Touch ID succeeded
  } catch {
    return false;
  }
}
```

Use in unlock flow as alternative to password.

---

## Add KYC enforcement on P2P

```typescript
// backend/src/p2p/p2p.guard.ts (new)

import { CanActivate, ExecutionContext, Injectable } from "@nestjs/common";
import { PrismaService } from "../prisma/prisma.service";

@Injectable()
export class KycRequiredGuard implements CanActivate {
  constructor(private prisma: PrismaService) {}

  async canActivate(context: ExecutionContext): Promise<boolean> {
    const req = context.switchToHttp().getRequest();
    const userId = req.user.sub;

    const kyc = await this.prisma.kycRecord.findUnique({ where: { userId } });
    if (kyc?.status !== "approved") {
      throw new Error("KYC verification required for P2P deals above $500");
    }
    return true;
  }
}
```

Apply to `POST /p2p/orders` and `POST /p2p/offers`.

