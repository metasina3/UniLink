using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UniLink.Core;

public sealed class UsageSignInException() : Exception("The usage portal rejected this account's sign-in.");

public sealed class UsagePortalClient(Func<NetworkLink, bool>? verifyInterface = null)
{
    private const string HomePath = "userportal/webpages/myaccount/index.jsp";
    private const string Ajax = "X-Requested-With";
    private const string Csrf = "X-CSRF-Token";
    private static readonly string[] TokenHeaders = [Csrf, "X-XSRF-Token", "CSRF-Token"];
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex ScriptToken = new(
        """(?:c\$rFt0k3n|(?:x[-_])?(?:csrf|xsrf)[\w-]*)['"]?\s*[:=]\s*['"](?<token>[^'"]+)['"]""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex TokenElement = new(
        """<(?:input|meta)\b(?=[^>]*\bname\s*=\s*['"][\w-]*(?:csrf|xsrf)[\w-]*['"])[^>]*\b(?:content|value)\s*=\s*['"](?<token>[^'"]+)['"]""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    public async Task<AccountUsage> ReadAsync(UsageTarget target, CancellationToken ct)
    {
        using var transport = new PortalTransport(target.Portal, target.Link, verifyInterface, maxResponseBytes: 2_000_000);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        var signedIn = false;
        try
        {
            var start = await GetAsync(transport, "userportal/webpages/myaccount/login.jsp", token);
            using var login = new HttpRequestMessage(HttpMethod.Post, "userportal/Controller")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["mode"] = "451",
                    ["json"] = JsonSerializer.Serialize(new { username = target.Username, password = target.Password, languageid = "1" }),
                    ["__RequestType"] = "ajax",
                    ["t"] = Timestamp()
                })
            };
            login.Headers.Add(Ajax, "XMLHttpRequest");
            login.Headers.TryAddWithoutValidation("Origin", transport.BaseAddress.GetLeftPart(UriPartial.Authority));
            var reply = await transport.SendAsync(login, token);
            using var json = JsonDocument.Parse(reply.Body, new JsonDocumentOptions { MaxDepth = 16 });
            var result = json.RootElement;
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("status", out var status) ||
                !int.TryParse(status.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var code))
                throw new PortalTransportException("The usage portal returned an unrecognized sign-in reply.");
            if (code != 200) throw new UsageSignInException();
            signedIn = true;

            var home = await GetAsync(transport, HomePath, token);
            if (UsagePage.TryRead(home.Body, DateTimeOffset.UtcNow) is { } data) return data;

            var csrf = ReadToken(home) ?? ReadToken(reply) ?? ReadToken(result) ?? ReadToken(start);
            using var request = new HttpRequestMessage(HttpMethod.Get, "userportal/webpages/myaccount/AccountStatus.jsp?popup=0&t=" + Timestamp());
            request.Headers.Add(Ajax, "XMLHttpRequest");
            request.Headers.Referrer = new Uri(transport.BaseAddress, HomePath);
            if (csrf is not null) request.Headers.TryAddWithoutValidation(Csrf, csrf);
            var details = await transport.SendAsync(request, token);
            return UsagePage.TryRead(details.Body, DateTimeOffset.UtcNow)
                ?? throw new PortalTransportException("The usage portal did not return a data allowance.");
        }
        catch (JsonException) { throw new PortalTransportException("The usage portal returned an unrecognized sign-in reply."); }
        catch (RegexMatchTimeoutException) { throw new PortalTransportException("The usage page could not be read."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new PortalTransportException("Reading data usage timed out."); }
        finally
        {
            if (signedIn && !ct.IsCancellationRequested)
            {
                // End only this user-portal session. The captive-portal keepalive has its own cookies.
                using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cleanup.CancelAfter(TimeSpan.FromSeconds(2));
                try { await GetAsync(transport, "userportal/webpages/logout.jsp?webclient=myaccount", cleanup.Token); }
                catch (Exception ex) when (ex is PortalTransportException or CertificateApprovalException or OperationCanceledException) { }
            }
        }
    }

    private static async Task<PortalDocument> GetAsync(PortalTransport transport, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await transport.SendAsync(request, ct);
    }

    private static string Timestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    private static string? ReadToken(PortalDocument page)
    {
        foreach (var name in TokenHeaders)
            if (page.Headers.TryGetValues(name, out var values) && Token(values.FirstOrDefault()) is { } token)
                return token;
        return Token(WebUtility.HtmlDecode(ScriptToken.Match(page.Body).Groups["token"].Value))
            ?? Token(WebUtility.HtmlDecode(TokenElement.Match(page.Body).Groups["token"].Value));
    }

    private static string? ReadToken(JsonElement json)
    {
        foreach (var property in json.EnumerateObject())
            if ((property.Name.Contains("csrf", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("xsrf", StringComparison.OrdinalIgnoreCase))
                && property.Value.ValueKind == JsonValueKind.String && Token(property.Value.GetString()) is { } token)
                return token;
        return null;
    }

    private static string? Token(string? value) => value is { Length: >= 8 and <= 512 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || "-_+/=.".Contains(c)) ? value : null;
}

internal static class UsagePage
{
    private const RegexOptions HtmlOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    // Match innermost rows, since the portal puts its tables inside outer layout tables.
    private static readonly Regex Row = new("""<tr\b[^>]*>([^<]*(?:<(?!/?tr\b)[^<]*)*)</tr\s*>""", HtmlOptions, Timeout);
    private static readonly Regex Cell = new("""<td\b[^>]*>(.*?)</td\s*>""", HtmlOptions | RegexOptions.Singleline, Timeout);
    private static readonly Regex Label = new("""\bid\s*=\s*['"]Language\.(?<name>[a-z][a-z0-9]*)['"]""", HtmlOptions, Timeout);
    private static readonly Regex Markup = new("<[^>]+>", HtmlOptions, Timeout);
    private static readonly Regex Size = new("""^(?<number>-?[\d,.]+)\s*(?<unit>[KMGT]i?B|B)?$""", HtmlOptions, Timeout);
    private static readonly string[] RowNames = ["CycleDataTrasfer", "CycleDownloadedData", "DataTrasfer", "DownloadedData"];

    public static AccountUsage? TryRead(string html, DateTimeOffset checkedAt)
    {
        var candidates = new AccountUsage?[RowNames.Length];
        foreach (Match row in Row.Matches(html))
        {
            var cells = Cell.Matches(row.Groups[1].Value);
            if (cells.Count < 6) continue;
            var name = Label.Match(cells[0].Groups[1].Value).Groups["name"].Value;
            var index = Array.FindIndex(RowNames, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || candidates[index] is not null) continue;
            if (Megabytes(cells[4].Groups[1].Value) is not decimal used || used < 0) continue;

            var quota = cells[1].Groups[1].Value;
            if (Megabytes(quota) is decimal total && total >= 0)
            {
                var remaining = Megabytes(cells[5].Groups[1].Value) ?? total - used;
                candidates[index] = new(total, used, Math.Max(0, remaining), checkedAt);
            }
            else if (Label.Match(quota).Groups["name"].Value.Equals("NAWithDot", StringComparison.OrdinalIgnoreCase) ||
                     Clean(quota).ToUpperInvariant() is "N.A." or "N/A" or "UNLIMITED")
                candidates[index] = new(null, used, null, checkedAt);
        }

        return candidates.FirstOrDefault(value => value?.TotalMb is not null) ?? candidates.FirstOrDefault(value => value is not null);
    }

    private static string Clean(string html) => WebUtility.HtmlDecode(Markup.Replace(html, " ")).Trim();

    private static decimal? Megabytes(string html)
    {
        var match = Size.Match(Clean(html));
        if (!match.Success || !decimal.TryParse(match.Groups["number"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return null;
        var unit = match.Groups["unit"].Value;
        if (unit.Length == 0) unit = Label.Match(html).Groups["name"].Value;
        return unit.ToUpperInvariant() switch
        {
            "B" => number / (1024 * 1024),
            "KB" or "KIB" => number / 1024,
            "GB" or "GIB" => number <= decimal.MaxValue / 1024 && number >= decimal.MinValue / 1024 ? number * 1024 : null,
            "TB" or "TIB" => number <= decimal.MaxValue / (1024 * 1024) && number >= decimal.MinValue / (1024 * 1024) ? number * (1024 * 1024) : null,
            "" or "MB" or "MIB" => number,
            _ => null
        };
    }
}
