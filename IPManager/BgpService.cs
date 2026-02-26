using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using System.IO;
using System.Net;
using System.Linq;

namespace IPManager
{
    public class BgpService
    {
        private readonly HttpClient _httpClient;

        public BgpService()
        {
            // Используем SocketsHttpHandler для поддержки современных протоколов
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            _httpClient = new HttpClient(handler);

            // Имитируем реальный браузер максимально подробно
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
        }

        public async Task<int> GetAsnByIpWhois(string ip)
        {
            try
            {
                using var client = new TcpClient();
                var task = client.ConnectAsync("whois.cymru.com", 43);
                if (await Task.WhenAny(task, Task.Delay(3000)) != task) throw new TimeoutException();

                using var stream = client.GetStream();
                using var writer = new StreamWriter(stream) { AutoFlush = true };
                using var reader = new StreamReader(stream);

                await writer.WriteLineAsync($"-v {ip}");
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("AS") || string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('|');
                    if (parts.Length > 0)
                    {
                        string rawAsn = parts[0].Trim();
                        if (int.TryParse(rawAsn, out int asn)) return asn;
                    }
                }
            }
            catch { return await GetAsnViaRdap(ip); }
            return 0;
        }

        private async Task<int> GetAsnViaRdap(string ip)
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"https://rdap.db.ripe.net/ip/{ip}");
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("entities", out var entities))
                {
                    foreach (var entity in entities.EnumerateArray())
                    {
                        if (entity.TryGetProperty("handle", out var handle))
                        {
                            var match = Regex.Match(handle.GetString() ?? "", @"AS(\d+)");
                            if (match.Success) return int.Parse(match.Groups[1].Value);
                        }
                    }
                }
            }
            catch { }
            return 0;
        }

        public async Task<(string name, string country, List<(uint start, uint end)> ranges)> GetAsnFullInfo(int asnId)
        {
            // 1. Пытаемся получить префиксы через парсинг HTML (самый надежный способ для HE.net)
            var ranges = await GetPrefixesFromHeHtml(asnId);

            // 2. Имя и страна через ARIN
            string name = $"AS{asnId}";
            string country = "??";
            try
            {
                using var client = new TcpClient("whois.arin.net", 43);
                using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                using var reader = new StreamReader(client.GetStream());
                await writer.WriteLineAsync($"n + AS{asnId}");
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("ASName:")) name = line.Replace("ASName:", "").Trim();
                    if (line.StartsWith("Country:")) country = line.Replace("Country:", "").Trim();
                }
            }
            catch { }

            return (name, country, ranges);
        }

        private async Task<List<(uint start, uint end)>> GetPrefixesFromHeHtml(int asnId)
        {
            var result = new List<(uint start, uint end)>();
            try
            {
                // Используем CancellationTokenSource для контроля зависаний
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                string url = $"https://bgp.he.net/AS{asnId}#_prefixes";

                var response = await _httpClient.GetStringAsync(url, cts.Token);

                // Уточненное регулярное выражение: ищем именно паттерн сети в ссылках
                // Пример: <a href="/net/1.2.3.0/24">1.2.3.0/24</a>
                var matches = Regex.Matches(response, @"/net/(\d{1,3}(\.\d{1,3}){3}/\d{1,2})");

                foreach (Match match in matches)
                {
                    string cidr = match.Groups[1].Value;
                    // Метод ParseCidr у вас уже реализован корректно
                    result.Add(ParseCidr(cidr));
                }

                return result.Distinct().ToList();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new Exception("BGP.HE.NET denied access (403). Try again later or use a VPN.");
            }
            catch (Exception ex)
            {
                throw new Exception($"BGP Error: {ex.Message}");
            }
        }

        private (uint start, uint end) ParseCidr(string cidr)
        {
            var parts = cidr.Split('/');
            uint ip = IpToUint(parts[0]);
            int mask = int.Parse(parts[1]);

            uint maskBits = (mask == 0) ? 0 : uint.MaxValue << (32 - mask);
            uint start = ip & maskBits;
            uint end = start | ~maskBits;

            return (start, end);
        }

        private uint IpToUint(string ipAddress)
        {
            var ip = IPAddress.Parse(ipAddress);
            var bytes = ip.GetAddressBytes();
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }
    }
}