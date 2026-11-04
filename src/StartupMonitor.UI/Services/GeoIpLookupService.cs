using System.Collections.Concurrent;
using System.Net;

namespace StartupMonitor.UI.Services;

/// <summary>
/// Лёгкий эвристический GeoIP-резолвер для интерактивного монитора соединений.
/// Определяет страну, флаг (эмодзи) и приватность IP без внешних запросов —
/// подходит для резолва при обновлении сокетов каждую секунду.
/// Потокобезопасен, никогда не бросает исключений (Zero-Crash).
/// ВНИМАНИЕ: определение страны по префиксам — эвристика, а не authoritative GeoIP.
/// </summary>
public static class GeoIpLookupService
{
    private const string PrivateName = "Локальная сеть / ПК";
    private const string PrivateFlag = "🏠";
    private const string ListeningName = "Прослушивание";
    private const string ListeningFlag = "👂";
    private const string FallbackName = "Интернет";
    private const string FallbackFlag = "🌐";

    private static readonly (string CountryName, string CountryFlag, bool IsPrivate) PrivateResult =
        (PrivateName, PrivateFlag, true);

    private static readonly (string CountryName, string CountryFlag, bool IsPrivate) ListeningResult =
        (ListeningName, ListeningFlag, true);

    private static readonly (string CountryName, string CountryFlag, bool IsPrivate) FallbackResult =
        (FallbackName, FallbackFlag, false);

    /// <summary>Кэш результатов для мгновенного резолва при ежесекундном обновлении.</summary>
    private static readonly ConcurrentDictionary<string, (string CountryName, string CountryFlag, bool IsPrivate)> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Число закэшированных записей.</summary>
    public static int CacheCount => Cache.Count;

    /// <summary>Очищает кэш (для тестов).</summary>
    internal static void ClearCache() => Cache.Clear();

    /// <summary>Точные совпадения известных публичных DNS/сервисов.</summary>
    private static readonly Dictionary<string, (string CountryName, string CountryFlag)> KnownHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["8.8.8.8"] = ("США (Google DNS)", "🇺🇸"),
            ["8.8.4.4"] = ("США (Google DNS)", "🇺🇸"),
            ["2001:4860:4860::8888"] = ("США (Google DNS)", "🇺🇸"),
            ["2001:4860:4860::8844"] = ("США (Google DNS)", "🇺🇸"),
            ["1.1.1.1"] = ("США (Cloudflare DNS)", "🇺🇸"),
            ["1.0.0.1"] = ("США (Cloudflare DNS)", "🇺🇸"),
            ["2606:4700:4700::1111"] = ("США (Cloudflare DNS)", "🇺🇸"),
            ["2606:4700:4700::1001"] = ("США (Cloudflare DNS)", "🇺🇸"),
            ["77.88.8.8"] = ("Россия (Yandex DNS)", "🇷🇺"),
            ["77.88.8.1"] = ("Россия (Yandex DNS)", "🇷🇺"),
            ["2a02:6b8::feed:0ff"] = ("Россия (Yandex DNS)", "🇷🇺"),
            ["2a02:6b8::feed:bad"] = ("Россия (Yandex DNS)", "🇷🇺"),
            ["9.9.9.9"] = ("США (Quad9)", "🇺🇸"),
            ["149.112.112.112"] = ("США (Quad9)", "🇺🇸"),
            ["2620:fe::fe"] = ("США (Quad9)", "🇺🇸"),
            ["2620:fe::9"] = ("США (Quad9)", "🇺🇸"),
            ["94.140.14.14"] = ("Кипр (AdGuard DNS)", "🇨🇾"),
            ["94.140.15.15"] = ("Кипр (AdGuard DNS)", "🇨🇾"),
        };

    /// <summary>
    /// Известные префиксы "A.B." популярных сетей: сначала самые специфичные именованные,
    /// затем страновые. Проверяются через StartsWith по нормализованному (lowercase) ключу.
    /// </summary>
    private static readonly (string Prefix, string CountryName, string CountryFlag)[] Prefixes =
    {
        // Россия: Яндекс / Mail.ru / Selectel.
        ("77.88.", "Россия (Яндекс)", "🇷🇺"),
        ("87.250.", "Россия (Яндекс)", "🇷🇺"),
        ("93.158.", "Россия (Яндекс)", "🇷🇺"),
        ("95.108.", "Россия (Яндекс)", "🇷🇺"),
        ("178.154.", "Россия (Яндекс)", "🇷🇺"),
        ("213.180.", "Россия (Яндекс)", "🇷🇺"),
        ("94.100.", "Россия (Mail.ru)", "🇷🇺"),
        ("217.69.", "Россия (Mail.ru)", "🇷🇺"),
        ("217.118.", "Россия (Mail.ru)", "🇷🇺"),
        ("194.87.", "Россия (Selectel)", "🇷🇺"),
        ("185.15.", "Россия", "🇷🇺"),
        ("185.70.", "Россия", "🇷🇺"),
        ("185.71.", "Россия", "🇷🇺"),
        ("188.93.", "Россия", "🇷🇺"),
        ("188.225.", "Россия", "🇷🇺"),
        // Германия: Hetzner / DTAG.
        ("46.4.", "Германия (Hetzner)", "🇩🇪"),
        ("49.12.", "Германия (Hetzner)", "🇩🇪"),
        ("49.13.", "Германия (Hetzner)", "🇩🇪"),
        ("78.46.", "Германия (Hetzner)", "🇩🇪"),
        ("78.47.", "Германия (Hetzner)", "🇩🇪"),
        ("88.99.", "Германия (Hetzner)", "🇩🇪"),
        ("88.198.", "Германия (Hetzner)", "🇩🇪"),
        ("95.216.", "Германия (Hetzner)", "🇩🇪"),
        ("116.202.", "Германия (Hetzner)", "🇩🇪"),
        ("116.203.", "Германия (Hetzner)", "🇩🇪"),
        ("128.140.", "Германия (Hetzner)", "🇩🇪"),
        ("135.181.", "Германия (Hetzner)", "🇩🇪"),
        ("136.243.", "Германия (Hetzner)", "🇩🇪"),
        ("138.201.", "Германия (Hetzner)", "🇩🇪"),
        ("144.76.", "Германия (Hetzner)", "🇩🇪"),
        ("148.251.", "Германия (Hetzner)", "🇩🇪"),
        ("159.69.", "Германия (Hetzner)", "🇩🇪"),
        ("167.233.", "Германия (Hetzner)", "🇩🇪"),
        ("168.119.", "Германия (Hetzner)", "🇩🇪"),
        ("176.9.", "Германия (Hetzner)", "🇩🇪"),
        ("195.201.", "Германия (Hetzner)", "🇩🇪"),
        ("213.133.", "Германия (Hetzner)", "🇩🇪"),
        ("213.239.", "Германия (Hetzner)", "🇩🇪"),
        ("81.169.", "Германия", "🇩🇪"),
        ("85.214.", "Германия", "🇩🇪"),
        // Финляндия: Hetzner FI / UpCloud / Elisa.
        ("37.27.", "Финляндия (Hetzner)", "🇫🇮"),
        ("65.21.", "Финляндия (Hetzner)", "🇫🇮"),
        ("95.217.", "Финляндия (Hetzner)", "🇫🇮"),
        ("94.237.", "Финляндия (UpCloud)", "🇫🇮"),
        ("84.17.", "Финляндия", "🇫🇮"),
        ("85.76.", "Финляндия", "🇫🇮"),
        ("86.50.", "Финляндия", "🇫🇮"),
        ("88.112.", "Финляндия", "🇫🇮"),
        ("91.152.", "Финляндия", "🇫🇮"),
        ("91.153.", "Финляндия", "🇫🇮"),
        // Нидерланды: LeaseWeb / WorldStream / DigitalOcean AMS.
        ("85.17.", "Нидерланды (LeaseWeb)", "🇳🇱"),
        ("84.53.", "Нидерланды (LeaseWeb)", "🇳🇱"),
        ("95.211.", "Нидерланды (LeaseWeb)", "🇳🇱"),
        ("94.102.", "Нидерланды", "🇳🇱"),
        ("46.101.", "Нидерланды (DigitalOcean)", "🇳🇱"),
        ("64.227.", "Нидерланды (DigitalOcean)", "🇳🇱"),
        ("68.183.", "Нидерланды (DigitalOcean)", "🇳🇱"),
        ("104.248.", "Нидерланды (DigitalOcean)", "🇳🇱"),
        ("134.209.", "Нидерланды (DigitalOcean)", "🇳🇱"),
        ("138.68.", "Нидерланды (DigitalOcean)", "🇳🇱"),
        ("142.93.", "Нидерланды (DigitalOcean)", "🇳🇱"),
        ("159.65.", "Нидерланды (DigitalOcean)", "🇳🇱"),
        ("159.89.", "Нидерланды (DigitalOcean)", "🇳🇱"),
        ("62.212.", "Нидерланды", "🇳🇱"),
        ("77.184.", "Нидерланды", "🇳🇱"),
        ("78.152.", "Нидерланды", "🇳🇱"),
        ("82.92.", "Нидерланды", "🇳🇱"),
        ("83.96.", "Нидерланды", "🇳🇱"),
        ("85.158.", "Нидерланды", "🇳🇱"),
        ("87.233.", "Нидерланды", "🇳🇱"),
        // Великобритания.
        ("77.68.", "Великобритания", "🇬🇧"),
        ("78.129.", "Великобритания", "🇬🇧"),
        ("79.135.", "Великобритания", "🇬🇧"),
        ("81.5.", "Великобритания", "🇬🇧"),
        ("82.132.", "Великобритания", "🇬🇧"),
        ("84.16.", "Великобритания", "🇬🇧"),
        ("85.13.", "Великобритания", "🇬🇧"),
        ("86.54.", "Великобритания", "🇬🇧"),
        ("87.224.", "Великобритания", "🇬🇧"),
        ("88.150.", "Великобритания", "🇬🇧"),
        ("89.19.", "Великобритания", "🇬🇧"),
        ("90.240.", "Великобритания", "🇬🇧"),
        ("92.60.", "Великобритания", "🇬🇧"),
        ("93.40.", "Великобритания", "🇬🇧"),
        ("94.76.", "Великобритания", "🇬🇧"),
        ("95.144.", "Великобритания", "🇬🇧"),
        ("146.66.", "Великобритания", "🇬🇧"),
        ("149.22.", "Великобритания", "🇬🇧"),
        ("151.216.", "Великобритания", "🇬🇧"),
        ("164.39.", "Великобритания", "🇬🇧"),
        ("171.33.", "Великобритания", "🇬🇧"),
        ("185.11.", "Великобритания", "🇬🇧"),
        ("185.20.", "Великобритания", "🇬🇧"),
        ("193.16.", "Великобритания", "🇬🇧"),
        ("194.72.", "Великобритания", "🇬🇧"),
        ("195.8.", "Великобритания", "🇬🇧"),
        // Франция: OVH / Scaleway.
        ("5.135.", "Франция (OVH)", "🇫🇷"),
        ("5.196.", "Франция (OVH)", "🇫🇷"),
        ("37.59.", "Франция (OVH)", "🇫🇷"),
        ("37.187.", "Франция (OVH)", "🇫🇷"),
        ("46.105.", "Франция (OVH)", "🇫🇷"),
        ("51.38.", "Франция (OVH)", "🇫🇷"),
        ("51.75.", "Франция (OVH)", "🇫🇷"),
        ("51.210.", "Франция (OVH)", "🇫🇷"),
        ("54.36.", "Франция (OVH)", "🇫🇷"),
        ("54.37.", "Франция (OVH)", "🇫🇷"),
        ("57.128.", "Франция (OVH)", "🇫🇷"),
        ("87.98.", "Франция (OVH)", "🇫🇷"),
        ("91.121.", "Франция (OVH)", "🇫🇷"),
        ("91.134.", "Франция (OVH)", "🇫🇷"),
        ("92.222.", "Франция (OVH)", "🇫🇷"),
        ("94.23.", "Франция (OVH)", "🇫🇷"),
        ("135.125.", "Франция (OVH)", "🇫🇷"),
        ("137.74.", "Франция (OVH)", "🇫🇷"),
        ("139.99.", "Франция (OVH)", "🇫🇷"),
        ("141.94.", "Франция (OVH)", "🇫🇷"),
        ("141.95.", "Франция (OVH)", "🇫🇷"),
        ("145.239.", "Франция (OVH)", "🇫🇷"),
        ("146.59.", "Франция (OVH)", "🇫🇷"),
        ("147.135.", "Франция (OVH)", "🇫🇷"),
        ("149.202.", "Франция (OVH)", "🇫🇷"),
        ("151.80.", "Франция (OVH)", "🇫🇷"),
        ("164.132.", "Франция (OVH)", "🇫🇷"),
        ("178.32.", "Франция (OVH)", "🇫🇷"),
        ("188.165.", "Франция (OVH)", "🇫🇷"),
        ("193.70.", "Франция (OVH)", "🇫🇷"),
        ("212.47.", "Франция (Scaleway)", "🇫🇷"),
        ("212.83.", "Франция (Scaleway)", "🇫🇷"),
        ("163.172.", "Франция (Scaleway)", "🇫🇷"),
        // США: Google / Cloudflare / Meta / GitHub.
        ("64.233.", "США (Google)", "🇺🇸"),
        ("74.125.", "США (Google)", "🇺🇸"),
        ("142.250.", "США (Google)", "🇺🇸"),
        ("172.217.", "США (Google)", "🇺🇸"),
        ("216.58.", "США (Google)", "🇺🇸"),
        ("216.239.", "США (Google)", "🇺🇸"),
        ("104.16.", "США (Cloudflare)", "🇺🇸"),
        ("104.21.", "США (Cloudflare)", "🇺🇸"),
        ("172.67.", "США (Cloudflare)", "🇺🇸"),
        ("173.245.", "США (Cloudflare)", "🇺🇸"),
        ("162.158.", "США (Cloudflare)", "🇺🇸"),
        ("31.13.", "США (Meta)", "🇺🇸"),
        ("69.63.", "США (Meta)", "🇺🇸"),
        ("69.171.", "США (Meta)", "🇺🇸"),
        ("66.220.", "США (Meta)", "🇺🇸"),
        ("157.240.", "США (Meta)", "🇺🇸"),
        ("173.252.", "США (Meta)", "🇺🇸"),
        ("140.82.", "США (GitHub)", "🇺🇸"),
        // Китай: Alibaba / Tencent.
        ("47.52.", "Китай (Alibaba)", "🇨🇳"),
        ("47.74.", "Китай (Alibaba)", "🇨🇳"),
        ("47.75.", "Китай (Alibaba)", "🇨🇳"),
        ("47.88.", "Китай (Alibaba)", "🇨🇳"),
        ("47.89.", "Китай (Alibaba)", "🇨🇳"),
        ("47.90.", "Китай (Alibaba)", "🇨🇳"),
        ("47.91.", "Китай (Alibaba)", "🇨🇳"),
        ("47.96.", "Китай (Alibaba)", "🇨🇳"),
        ("47.99.", "Китай (Alibaba)", "🇨🇳"),
        ("47.100.", "Китай (Alibaba)", "🇨🇳"),
        ("47.101.", "Китай (Alibaba)", "🇨🇳"),
        ("47.102.", "Китай (Alibaba)", "🇨🇳"),
        ("47.103.", "Китай (Alibaba)", "🇨🇳"),
        ("47.104.", "Китай (Alibaba)", "🇨🇳"),
        ("47.105.", "Китай (Alibaba)", "🇨🇳"),
        ("47.106.", "Китай (Alibaba)", "🇨🇳"),
        ("47.107.", "Китай (Alibaba)", "🇨🇳"),
        ("47.108.", "Китай (Alibaba)", "🇨🇳"),
        ("43.128.", "Китай (Tencent)", "🇨🇳"),
        ("43.129.", "Китай (Tencent)", "🇨🇳"),
        ("43.130.", "Китай (Tencent)", "🇨🇳"),
        ("43.131.", "Китай (Tencent)", "🇨🇳"),
        ("49.51.", "Китай (Tencent)", "🇨🇳"),
        ("106.52.", "Китай (Tencent)", "🇨🇳"),
        ("106.53.", "Китай (Tencent)", "🇨🇳"),
        ("106.54.", "Китай (Tencent)", "🇨🇳"),
        ("106.55.", "Китай (Tencent)", "🇨🇳"),
        ("111.230.", "Китай (Tencent)", "🇨🇳"),
        ("111.231.", "Китай (Tencent)", "🇨🇳"),
        ("115.159.", "Китай (Tencent)", "🇨🇳"),
        ("118.25.", "Китай (Tencent)", "🇨🇳"),
        ("119.28.", "Китай (Tencent)", "🇨🇳"),
        ("129.28.", "Китай (Tencent)", "🇨🇳"),
        ("139.199.", "Китай (Tencent)", "🇨🇳"),
        ("140.143.", "Китай (Tencent)", "🇨🇳"),
        ("150.109.", "Китай (Tencent)", "🇨🇳"),
        ("152.136.", "Китай (Tencent)", "🇨🇳"),
        ("162.14.", "Китай (Tencent)", "🇨🇳"),
    };

    /// <summary>IPv6-префиксы известных сетей (ключ уже в lowercase).</summary>
    private static readonly (string Prefix, string CountryName, string CountryFlag)[] Ipv6Prefixes =
    {
        ("2001:4860:", "США (Google)", "🇺🇸"),
        ("2606:4700:", "США (Cloudflare)", "🇺🇸"),
        ("2a02:6b8:", "Россия (Яндекс)", "🇷🇺"),
        ("2620:fe:", "США (Quad9)", "🇺🇸"),
    };

    /// <summary>
    /// Определяет страну, флаг и приватность IP-адреса. Никогда не бросает исключений.
    /// </summary>
    /// <param name="ipAddress">IP-адрес строкой (IPv4/IPv6, "*" или пусто для слушающих сокетов).</param>
    public static (string CountryName, string CountryFlag, bool IsPrivate) Resolve(string? ipAddress)
    {
        var key = (ipAddress ?? string.Empty).Trim();
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        (string CountryName, string CountryFlag, bool IsPrivate) result;
        try
        {
            result = ResolveCore(key);
        }
        catch
        {
            result = FallbackResult;
        }

        Cache[key] = result;
        if (Cache.Count > 5000)
            Cache.Clear();
        return result;
    }

    private static (string CountryName, string CountryFlag, bool IsPrivate) ResolveCore(string key)
    {
        // Неопределённые / слушающие сокеты.
        if (key.Length == 0
            || key == "*"
            || key == "0.0.0.0"
            || key == "::"
            || key == "0:0:0:0:0:0:0:0"
            || key == "[::]")
            return ListeningResult;

        // Популярные публичные DNS/сервисы — точное совпадение.
        if (KnownHosts.TryGetValue(key, out var known))
            return (known.CountryName, known.CountryFlag, false);

        if (!IPAddress.TryParse(key, out var ip))
            return FallbackResult;

        if (IPAddress.IsLoopback(ip))
            return PrivateResult;

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return ResolveIpv4(ip, key.ToLowerInvariant());

        return ResolveIpv6(ip, key.ToLowerInvariant());
    }

    private static (string CountryName, string CountryFlag, bool IsPrivate) ResolveIpv4(IPAddress ip, string lowerKey)
    {
        var b = ip.GetAddressBytes();

        // 0.0.0.0/8 — unspecified/software scope.
        if (b[0] == 0)
            return ListeningResult;

        // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16.
        if (b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254))
            return PrivateResult;

        // 100.64.0.0/10 — Shared CGNAT (внутри провайдера).
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            return ("Локальная сеть (CGNAT)", PrivateFlag, true);

        // 224.0.0.0/4 — мультикаст.
        if (b[0] >= 224 && b[0] <= 239)
            return ("Мультикаст", "📡", true);

        // 240.0.0.0/4 — зарезервировано/широковещание.
        if (b[0] >= 240)
            return b[0] == 255 && b[1] == 255 && b[2] == 255 && b[3] == 255
                ? ("Широковещание", "📡", true)
                : ("Зарезервировано", "📡", true);

        // Документация/тесты/бенчмарки — не реальные удалённые хосты.
        if ((b[0] == 192 && b[1] == 0 && b[2] == 2)
            || (b[0] == 198 && b[1] == 51 && b[2] == 100)
            || (b[0] == 203 && b[1] == 0 && b[2] == 113)
            || (b[0] == 198 && b[1] >= 18 && b[1] <= 19))
            return ("Тестовая сеть", "📡", true);

        // Известные префиксы популярных сетей.
        foreach (var (prefix, country, flag) in Prefixes)
        {
            if (lowerKey.StartsWith(prefix, StringComparison.Ordinal))
                return (country, flag, false);
        }

        // Эвристика по первому октету (не authoritative!).
        return ResolveByFirstOctet(b[0]);
    }

    private static (string CountryName, string CountryFlag, bool IsPrivate) ResolveIpv6(IPAddress ip, string lowerKey)
    {
        // IPv4-mapped (::ffff:a.b.c.d) — резолвим как IPv4.
        if (ip.IsIPv4MappedToIPv6)
        {
            try
            {
                return ResolveIpv4(ip.MapToIPv4(), lowerKey);
            }
            catch
            {
                return FallbackResult;
            }
        }

        var b = ip.GetAddressBytes();

        // :: (unspecified) — на случай ненормализованных форм.
        var allZero = true;
        for (var i = 0; i < b.Length; i++)
        {
            if (b[i] != 0) { allZero = false; break; }
        }
        if (allZero)
            return ListeningResult;

        // fe80::/10 link-local, fec0::/10 site-local (deprecated), fc00::/7 unique-local.
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            return PrivateResult;
        if ((b[0] & 0xFE) == 0xFC)
            return PrivateResult;

        // 2001:db8::/32 — документация.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
            return ("Тестовая сеть", "📡", true);

        foreach (var (prefix, country, flag) in Ipv6Prefixes)
        {
            if (lowerKey.StartsWith(prefix, StringComparison.Ordinal))
                return (country, flag, false);
        }

        return FallbackResult;
    }

    /// <summary>
    /// Грубая эвристика страны по первому октету IPv4 для популярных диапазонов.
    /// НЕ является точной геолокацией: смешанные европейские диапазоны RIPE
    /// по умолчанию относятся к России (целевой регион продукта).
    /// </summary>
    private static (string CountryName, string CountryFlag, bool IsPrivate) ResolveByFirstOctet(byte b0)
    {
        return b0 switch
        {
            // --- США: ARIN legacy + американские облака (AWS/Azure/GCP) ---
            1 or 3 or 4 or 6 or 8 or 9 or 11 or 12 or 13 or 15 or 16 or 17 or 18 or 19
                or 20 or 21 or 22 or 23 or 24 or 32 or 33 or 34 or 35 or 38 or 40 or 44
                or 45 or 47 or 48 or 50 or 52 or 53 or 54 or 55 or 56 or 63 or 64 or 65
                or 66 or 67 or 68 or 69 or 70 or 71 or 72 or 73 or 74 or 75 or 76 or 96
                or 97 or 98 or 99 or 100 or 104 or 107 or 108 or 130 or 131 or 132 or 134
                or 136 or 137 or 138 or 139 or 140 or 142 or 143 or 144 or 146 or 147 or 148
                or 149 or 150 or 151 or 152 or 154 or 155 or 156 or 157 or 158 or 159 or 160
                or 161 or 162 or 163 or 164 or 165 or 166 or 167 or 168 or 169 or 170 or 171
                or 172 or 173 or 174 or 184 or 192 or 198 or 199 or 204 or 205 or 206 or 207
                or 208 or 209 or 214 or 216 => ("США", "🇺🇸", false),

            // --- Россия: крупные российские операторы в пространстве RIPE ---
            5 or 31 or 37 or 46 or 62 or 77 or 78 or 79 or 81 or 84 or 85 or 87 or 88
                or 89 or 91 or 93 or 94 or 95 or 109 or 176 or 178 or 188 or 193 or 194
                or 195 or 212 or 213 or 217 => ("Россия", "🇷🇺", false),

            // --- Германия ---
            80 => ("Германия", "🇩🇪", false),

            // --- Нидерланды ---
            82 or 83 => ("Нидерланды", "🇳🇱", false),

            // --- Великобритания (25/8 — Минобороны UK; 86/92 — крупные BT-блоки) ---
            25 or 86 or 92 => ("Великобритания", "🇬🇧", false),

            // --- Франция (2/8 — Orange; 51/57/90 — OVH/Orange) ---
            2 or 51 or 57 or 90 => ("Франция", "🇫🇷", false),

            // --- Китай: APNIC-блоки с доминирующим присутствием CN ---
            14 or 36 or 39 or 42 or 43 or 49 or 58 or 59 or 60 or 61 or 101 or 106
                or 110 or 111 or 112 or 113 or 114 or 115 or 116 or 117 or 118 or 119
                or 120 or 121 or 122 or 123 or 124 or 125 or 180 or 182 or 183 or 218
                or 219 or 220 or 221 or 222 or 223 => ("Китай", "🇨🇳", false),

            // --- Япония ---
            126 or 133 or 153 => ("Япония", "🇯🇵", false),

            // --- Южная Корея ---
            175 => ("Южная Корея", "🇰🇷", false),

            // --- Индия ---
            103 => ("Индия", "🇮🇳", false),

            // --- Бразилия / Латинская Америка ---
            177 or 179 or 181 or 186 or 187 or 189 or 190 or 191 or 200 or 201 => ("Бразилия", "🇧🇷", false),

            // --- Африка ---
            41 or 102 or 105 or 196 or 197 => ("Африка", "🌍", false),

            _ => FallbackResult,
        };
    }
}
