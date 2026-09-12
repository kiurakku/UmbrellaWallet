using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>One asset held on an exchange.</summary>
public sealed record ExchangeAsset(string Symbol, decimal Amount);

public sealed record ExchangeFetchResult(bool Ok, IReadOnlyList<ExchangeAsset> Assets, string? Error);

/// <summary>
/// Read-only exchange balance fetching.
///
/// These connectors only ever call account/balance endpoints. There is deliberately NO trading
/// and NO withdrawal anywhere in this codebase: a stolen read-only key leaks balances, whereas a
/// stolen withdrawal key loses the funds outright, so the app is built to never need one.
///
/// Requests go through <see cref="PublicHttp.Shared"/>, so they follow Tor when Tor is enabled.
/// </summary>
public static class ExchangeConnectors
{
    /// <summary>
    /// Exchanges the wallet can read balances from. Weighted towards what people actually use in
    /// the CIS, plus Telegram's CryptoBot, which is a wallet rather than an exchange but holds
    /// balances the same way.
    /// </summary>
    public static IReadOnlyList<string> Supported { get; } =
        ["Binance", "Bybit", "OKX", "Kraken", "KuCoin", "Gate.io", "MEXC", "Bitget", "Telegram CryptoBot"];

    /// <summary>Exchanges whose keys are issued together with a passphrase.</summary>
    public static bool RequiresPassphrase(string exchange) =>
        exchange.ToUpperInvariant() is "OKX" or "KUCOIN" or "BITGET";

    /// <summary>
    /// CryptoBot authenticates with a single token, so asking for a secret would just confuse.
    /// </summary>
    public static bool RequiresSecret(string exchange) =>
        !exchange.Contains("CryptoBot", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where to create a read-only key, shown next to the form.</summary>
    public static string KeyHint(string exchange) => exchange.ToUpperInvariant() switch
    {
        "BINANCE" => "Binance → API Management → create key → tick ONLY \"Enable Reading\".",
        "BYBIT" => "Bybit → API → new key → \"Read-only\" permission.",
        "OKX" => "OKX → API → create V5 key → permission \"Read\" → note the passphrase.",
        "KRAKEN" => "Kraken → Settings → API → new key → only \"Query Funds\".",
        "KUCOIN" => "KuCoin → API Management → create key → \"General\" (read) → note the passphrase.",
        "GATE.IO" => "Gate.io → API Keys → new key → \"Spot\" set to read-only.",
        "MEXC" => "MEXC → API → create key → enable only account reading.",
        "BITGET" => "Bitget → API → new key → \"Read-only\" → note the passphrase.",
        _ => "In Telegram open @CryptoBot → Crypto Pay → My Apps → create app → copy the API token.",
    };

    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.ExchangeAccount);

    public static Task<ExchangeFetchResult> FetchBalancesAsync(
        string exchange, string apiKey, string apiSecret, string? passphrase, CancellationToken ct = default)
        => exchange.ToUpperInvariant() switch
        {
            "BINANCE" => FetchBinanceAsync(apiKey, apiSecret, ct),
            "BYBIT" => FetchBybitAsync(apiKey, apiSecret, ct),
            "OKX" => FetchOkxAsync(apiKey, apiSecret, passphrase, ct),
            "KRAKEN" => FetchKrakenAsync(apiKey, apiSecret, ct),
            "KUCOIN" => FetchKuCoinAsync(apiKey, apiSecret, passphrase, ct),
            "GATE.IO" or "GATE" => FetchGateAsync(apiKey, apiSecret, ct),
            // MEXC mirrors Binance's signing scheme exactly.
            "MEXC" => FetchMexcAsync(apiKey, apiSecret, ct),
            "BITGET" => FetchBitgetAsync(apiKey, apiSecret, passphrase, ct),
            "TELEGRAM CRYPTOBOT" or "CRYPTOBOT" => FetchCryptoBotAsync(apiKey, ct),
            _ => Task.FromResult(new ExchangeFetchResult(false, [], $"Unsupported exchange: {exchange}")),
        };

    // ---------------------------------------------------------------- Kraken
    private static async Task<ExchangeFetchResult> FetchKrakenAsync(
        string apiKey, string apiSecret, CancellationToken ct)
    {
        try
        {
            const string path = "/0/private/Balance";
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var postData = $"nonce={nonce}";

            // Kraken: HMAC-SHA512(base64-decoded secret, path ‖ SHA256(nonce ‖ postData)).
            var sha256 = SHA256.HashData(Encoding.UTF8.GetBytes(nonce + postData));
            var payload = Encoding.UTF8.GetBytes(path).Concat(sha256).ToArray();
            using var hmac = new HMACSHA512(Convert.FromBase64String(apiSecret));
            var signature = Convert.ToBase64String(hmac.ComputeHash(payload));

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.kraken.com" + path)
            {
                Content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded"),
            };
            request.Headers.Add("API-Key", apiKey);
            request.Headers.Add("API-Sign", signature);

            using var res = await Http.SendAsync(request, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("error", out var errors) && errors.GetArrayLength() > 0)
            {
                return new ExchangeFetchResult(false, [], $"Kraken: {errors[0].GetString()}");
            }

            var assets = new List<ExchangeAsset>();
            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                foreach (var entry in result.EnumerateObject())
                {
                    if (decimal.TryParse(entry.Value.GetString(), NumberStyles.Any,
                            CultureInfo.InvariantCulture, out var amount) && amount > 0)
                    {
                        assets.Add(new ExchangeAsset(NormaliseKrakenAsset(entry.Name), amount));
                    }
                }
            }

            return new ExchangeFetchResult(true, assets, null);
        }
        catch (Exception ex)
        {
            return new ExchangeFetchResult(false, [], $"Kraken: {ex.Message}");
        }
    }

    /// <summary>Kraken prefixes many tickers (XXBT, ZUSD); strip it so prices resolve.</summary>
    private static string NormaliseKrakenAsset(string asset)
    {
        var name = asset.Split('.')[0];
        if (name.Length == 4 && name[0] is 'X' or 'Z') name = name[1..];
        return name switch { "XBT" => "BTC", "XDG" => "DOGE", _ => name };
    }

    // ---------------------------------------------------------------- KuCoin
    private static async Task<ExchangeFetchResult> FetchKuCoinAsync(
        string apiKey, string apiSecret, string? passphrase, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return new ExchangeFetchResult(false, [], "KuCoin also needs the API passphrase.");
        }

        try
        {
            const string path = "/api/v1/accounts";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var signature = HmacBase64(apiSecret, timestamp + "GET" + path);
            // Key version 2 sends the passphrase signed rather than in the clear.
            var signedPassphrase = HmacBase64(apiSecret, passphrase);

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.kucoin.com" + path);
            request.Headers.Add("KC-API-KEY", apiKey);
            request.Headers.Add("KC-API-SIGN", signature);
            request.Headers.Add("KC-API-TIMESTAMP", timestamp);
            request.Headers.Add("KC-API-PASSPHRASE", signedPassphrase);
            request.Headers.Add("KC-API-KEY-VERSION", "2");

            using var res = await Http.SendAsync(request, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return Fail(body, "KuCoin");

            using var doc = JsonDocument.Parse(body);
            var assets = new List<ExchangeAsset>();
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var account in data.EnumerateArray())
                {
                    var symbol = account.GetProperty("currency").GetString();
                    var total = ParseDecimal(account, "balance");
                    if (symbol is not null && total > 0) assets.Add(new ExchangeAsset(symbol, total));
                }
            }

            return new ExchangeFetchResult(true, Merge(assets), null);
        }
        catch (Exception ex)
        {
            return new ExchangeFetchResult(false, [], $"KuCoin: {ex.Message}");
        }
    }

    // --------------------------------------------------------------- Gate.io
    private static async Task<ExchangeFetchResult> FetchGateAsync(
        string apiKey, string apiSecret, CancellationToken ct)
    {
        try
        {
            const string path = "/api/v4/spot/accounts";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            // Gate signs METHOD\nPATH\nQUERY\nSHA512(body)\ntimestamp with HMAC-SHA512.
            var bodyHash = Convert.ToHexString(SHA512.HashData([])).ToLowerInvariant();
            var payload = $"GET\n{path}\n\n{bodyHash}\n{timestamp}";
            using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(apiSecret));
            var signature = Convert.ToHexString(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.gateio.ws" + path);
            request.Headers.Add("KEY", apiKey);
            request.Headers.Add("SIGN", signature);
            request.Headers.Add("Timestamp", timestamp);

            using var res = await Http.SendAsync(request, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return Fail(body, "Gate.io");

            using var doc = JsonDocument.Parse(body);
            var assets = new List<ExchangeAsset>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var symbol = entry.GetProperty("currency").GetString();
                var total = ParseDecimal(entry, "available") + ParseDecimal(entry, "locked");
                if (symbol is not null && total > 0) assets.Add(new ExchangeAsset(symbol, total));
            }

            return new ExchangeFetchResult(true, assets, null);
        }
        catch (Exception ex)
        {
            return new ExchangeFetchResult(false, [], $"Gate.io: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ MEXC
    private static async Task<ExchangeFetchResult> FetchMexcAsync(
        string apiKey, string apiSecret, CancellationToken ct)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var query = $"timestamp={timestamp}&recvWindow=10000";
            var signature = HmacHex(apiSecret, query);

            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.mexc.com/api/v3/account?{query}&signature={signature}");
            request.Headers.Add("X-MEXC-APIKEY", apiKey);

            using var res = await Http.SendAsync(request, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return Fail(body, "MEXC");

            using var doc = JsonDocument.Parse(body);
            var assets = new List<ExchangeAsset>();
            if (doc.RootElement.TryGetProperty("balances", out var balances))
            {
                foreach (var entry in balances.EnumerateArray())
                {
                    var symbol = entry.GetProperty("asset").GetString();
                    var total = ParseDecimal(entry, "free") + ParseDecimal(entry, "locked");
                    if (symbol is not null && total > 0) assets.Add(new ExchangeAsset(symbol, total));
                }
            }

            return new ExchangeFetchResult(true, assets, null);
        }
        catch (Exception ex)
        {
            return new ExchangeFetchResult(false, [], $"MEXC: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- Bitget
    private static async Task<ExchangeFetchResult> FetchBitgetAsync(
        string apiKey, string apiSecret, string? passphrase, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return new ExchangeFetchResult(false, [], "Bitget also needs the API passphrase.");
        }

        try
        {
            const string path = "/api/v2/spot/account/assets";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var signature = HmacBase64(apiSecret, timestamp + "GET" + path);

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bitget.com" + path);
            request.Headers.Add("ACCESS-KEY", apiKey);
            request.Headers.Add("ACCESS-SIGN", signature);
            request.Headers.Add("ACCESS-TIMESTAMP", timestamp);
            request.Headers.Add("ACCESS-PASSPHRASE", passphrase);
            request.Headers.Add("locale", "en-US");

            using var res = await Http.SendAsync(request, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return Fail(body, "Bitget");

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("code", out var code) && code.GetString() != "00000")
            {
                var message = doc.RootElement.TryGetProperty("msg", out var m) ? m.GetString() : "error";
                return new ExchangeFetchResult(false, [], $"Bitget: {message}");
            }

            var assets = new List<ExchangeAsset>();
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var entry in data.EnumerateArray())
                {
                    var symbol = entry.GetProperty("coin").GetString();
                    var total = ParseDecimal(entry, "available") + ParseDecimal(entry, "frozen");
                    if (symbol is not null && total > 0) assets.Add(new ExchangeAsset(symbol, total));
                }
            }

            return new ExchangeFetchResult(true, assets, null);
        }
        catch (Exception ex)
        {
            return new ExchangeFetchResult(false, [], $"Bitget: {ex.Message}");
        }
    }

    // ---------------------------------------------------- Telegram CryptoBot
    private static async Task<ExchangeFetchResult> FetchCryptoBotAsync(
        string token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, "https://pay.crypt.bot/api/getBalance");
            request.Headers.Add("Crypto-Pay-API-Token", token);

            using var res = await Http.SendAsync(request, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var error = doc.RootElement.TryGetProperty("error", out var e) ? e.ToString() : body;
                return new ExchangeFetchResult(false, [], $"CryptoBot: {error}");
            }

            var assets = new List<ExchangeAsset>();
            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                foreach (var entry in result.EnumerateArray())
                {
                    var symbol = entry.GetProperty("currency_code").GetString();
                    var total = ParseDecimal(entry, "available") + ParseDecimal(entry, "onhold");
                    if (symbol is not null && total > 0) assets.Add(new ExchangeAsset(symbol, total));
                }
            }

            return new ExchangeFetchResult(true, assets, null);
        }
        catch (Exception ex)
        {
            return new ExchangeFetchResult(false, [], $"CryptoBot: {ex.Message}");
        }
    }

    /// <summary>Collapses per-account rows (KuCoin reports main/trade separately) into one per asset.</summary>
    private static List<ExchangeAsset> Merge(IEnumerable<ExchangeAsset> assets) =>
        assets.GroupBy(a => a.Symbol)
            .Select(g => new ExchangeAsset(g.Key, g.Sum(a => a.Amount)))
            .ToList();

    // ---------------------------------------------------------------- Binance
    private static async Task<ExchangeFetchResult> FetchBinanceAsync(
        string apiKey, string apiSecret, CancellationToken ct)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var query = $"timestamp={timestamp}&recvWindow=10000";
            var signature = HmacHex(apiSecret, query);

            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.binance.com/api/v3/account?{query}&signature={signature}");
            request.Headers.Add("X-MBX-APIKEY", apiKey);

            using var res = await Http.SendAsync(request, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return Fail(body, "Binance");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("balances", out var balances))
            {
                return new ExchangeFetchResult(false, [], "Binance returned no balances.");
            }

            var assets = new List<ExchangeAsset>();
            foreach (var entry in balances.EnumerateArray())
            {
                var symbol = entry.GetProperty("asset").GetString();
                var free = ParseDecimal(entry, "free");
                var locked = ParseDecimal(entry, "locked");
                var total = free + locked;
                if (symbol is not null && total > 0) assets.Add(new ExchangeAsset(symbol, total));
            }

            return new ExchangeFetchResult(true, assets, null);
        }
        catch (Exception ex)
        {
            return new ExchangeFetchResult(false, [], $"Binance: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ Bybit
    private static async Task<ExchangeFetchResult> FetchBybitAsync(
        string apiKey, string apiSecret, CancellationToken ct)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            const string recvWindow = "10000";
            const string query = "accountType=UNIFIED";
            // Bybit v5 signs timestamp + apiKey + recvWindow + queryString.
            var signature = HmacHex(apiSecret, timestamp + apiKey + recvWindow + query);

            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.bybit.com/v5/account/wallet-balance?{query}");
            request.Headers.Add("X-BAPI-API-KEY", apiKey);
            request.Headers.Add("X-BAPI-TIMESTAMP", timestamp);
            request.Headers.Add("X-BAPI-RECV-WINDOW", recvWindow);
            request.Headers.Add("X-BAPI-SIGN", signature);

            using var res = await Http.SendAsync(request, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return Fail(body, "Bybit");

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("retCode", out var code) && code.GetInt32() != 0)
            {
                var message = doc.RootElement.TryGetProperty("retMsg", out var m) ? m.GetString() : "error";
                return new ExchangeFetchResult(false, [], $"Bybit: {message}");
            }

            var assets = new List<ExchangeAsset>();
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("list", out var list))
            {
                foreach (var account in list.EnumerateArray())
                {
                    if (!account.TryGetProperty("coin", out var coins)) continue;
                    foreach (var coin in coins.EnumerateArray())
                    {
                        var symbol = coin.GetProperty("coin").GetString();
                        var total = ParseDecimal(coin, "walletBalance");
                        if (symbol is not null && total > 0) assets.Add(new ExchangeAsset(symbol, total));
                    }
                }
            }

            return new ExchangeFetchResult(true, assets, null);
        }
        catch (Exception ex)
        {
            return new ExchangeFetchResult(false, [], $"Bybit: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------- OKX
    private static async Task<ExchangeFetchResult> FetchOkxAsync(
        string apiKey, string apiSecret, string? passphrase, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return new ExchangeFetchResult(false, [], "OKX also needs the API passphrase you set when creating the key.");
        }

        try
        {
            const string path = "/api/v5/account/balance";
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            // OKX signs timestamp + method + path + body, base64 of the HMAC.
            var signature = HmacBase64(apiSecret, timestamp + "GET" + path);

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.okx.com" + path);
            request.Headers.Add("OK-ACCESS-KEY", apiKey);
            request.Headers.Add("OK-ACCESS-SIGN", signature);
            request.Headers.Add("OK-ACCESS-TIMESTAMP", timestamp);
            request.Headers.Add("OK-ACCESS-PASSPHRASE", passphrase);

            using var res = await Http.SendAsync(request, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return Fail(body, "OKX");

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("code", out var code) && code.GetString() != "0")
            {
                var message = doc.RootElement.TryGetProperty("msg", out var m) ? m.GetString() : "error";
                return new ExchangeFetchResult(false, [], $"OKX: {message}");
            }

            var assets = new List<ExchangeAsset>();
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var account in data.EnumerateArray())
                {
                    if (!account.TryGetProperty("details", out var details)) continue;
                    foreach (var detail in details.EnumerateArray())
                    {
                        var symbol = detail.GetProperty("ccy").GetString();
                        var total = ParseDecimal(detail, "eq");
                        if (symbol is not null && total > 0) assets.Add(new ExchangeAsset(symbol, total));
                    }
                }
            }

            return new ExchangeFetchResult(true, assets, null);
        }
        catch (Exception ex)
        {
            return new ExchangeFetchResult(false, [], $"OKX: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ utils
    private static ExchangeFetchResult Fail(string body, string exchange)
    {
        // Surface the exchange's own message — "Invalid API-key, IP, or permissions" is the
        // single most useful thing we can show when a key is wrong or IP-restricted.
        var message = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("msg", out var m)) message = m.GetString() ?? body;
            else if (doc.RootElement.TryGetProperty("retMsg", out var r)) message = r.GetString() ?? body;
        }
        catch
        {
            // not JSON — use the raw body
        }

        if (message.Length > 200) message = message[..200];
        return new ExchangeFetchResult(false, [], $"{exchange}: {message}");
    }

    private static decimal ParseDecimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    private static string HmacHex(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string HmacBase64(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }
}
