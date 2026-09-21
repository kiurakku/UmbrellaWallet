using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Payjoin;
using Umbrella.Wallet.Core.Payments;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// Talks to a PayJoin receiver's endpoint (BIP-78, version 1).
///
/// One POST: the original PSBT in, the receiver's proposal out. It goes over the wallet's own
/// Tor-aware client on a circuit of its own, so with Tor on the receiver learns nothing about where the
/// payment came from beyond what the payment itself says. Output substitution is always refused in the
/// request, and the answer is only parsed here — whether it may be signed is
/// <see cref="PayjoinProposalChecker"/>'s decision, not this class's.
/// </summary>
public static class PayjoinClient
{
    /// <summary>BIP-78 suggests a sender waits up to about a minute before giving up and broadcasting
    /// the original.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>A proposal is a PSBT of a handful of inputs. Anything this large is not one.</summary>
    private const int MaxResponseChars = 1_000_000;

    public static Uri BuildRequestUri(Uri endpoint, PayjoinParameters p)
    {
        var query = new List<string> { "v=1", "disableoutputsubstitution=true" };

        if (p.FeeOutputIndex is { } index && p.MaxFeeContributionSat > 0)
        {
            query.Add("additionalfeeoutputindex=" + index.ToString(CultureInfo.InvariantCulture));
            query.Add("maxadditionalfeecontribution=" + p.MaxFeeContributionSat.ToString(CultureInfo.InvariantCulture));
        }

        query.Add("minfeerate=" + p.MinFeeRateSatPerVByte.ToString(CultureInfo.InvariantCulture));

        var separator = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
        return new Uri(endpoint.AbsoluteUri + separator + string.Join("&", query));
    }

    public static async Task<(PSBT? Proposal, string? Error)> RequestAsync(
        Uri endpoint, PSBT original, PayjoinParameters parameters, CancellationToken ct)
    {
        if (!Bip21Uri.IsAcceptableEndpoint(endpoint))
            return (null, "The receiver's PayJoin address is neither HTTPS nor a .onion.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        try
        {
            using var content = new StringContent(original.ToBase64(), Encoding.UTF8, "text/plain");
            using var response = await PublicHttp.For(PublicHttp.NetworkPurpose.Payjoin)
                .PostAsync(BuildRequestUri(endpoint, parameters), content, timeout.Token);

            var body = (await response.Content.ReadAsStringAsync(timeout.Token)).Trim();

            if (!response.IsSuccessStatusCode)
                return (null, DescribeRefusal(response.StatusCode, body));

            if (body.Length == 0 || body.Length > MaxResponseChars)
                return (null, "The receiver's answer was not a PayJoin proposal.");

            try
            {
                return (PSBT.Parse(body, original.Network), null);
            }
            catch
            {
                return (null, "The receiver's answer was not a PayJoin proposal.");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, "The receiver did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return (null, $"The receiver could not be reached: {ex.Message}");
        }
    }

    /// <summary>BIP-78 errors come back as <c>{"errorCode": "...", "message": "..."}</c>. The code is
    /// shown; the free-text message is the receiver's and only ever displayed, never acted on.</summary>
    private static string DescribeRefusal(HttpStatusCode status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errorCode", out var code) && code.GetString() is { } c)
                return $"The receiver declined the PayJoin ({c}).";
        }
        catch
        {
            // Not JSON: fall through to the status code.
        }

        return $"The receiver declined the PayJoin (HTTP {(int)status}).";
    }
}
