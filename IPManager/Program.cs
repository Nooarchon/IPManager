using IPManager;
using Photino.NET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq;
using System.Runtime.CompilerServices;

// Разрешаем проекту тестов видеть internal методы
[assembly: InternalsVisibleTo("IPManager.Tests")]

namespace IPManager
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var dbService = new DatabaseService();

                var window = new PhotinoWindow()
                    .SetTitle("ASN IP Manager")
                    .SetSize(1200, 800)
                    .SetUseOsDefaultSize(false);

                window.RegisterWebMessageReceivedHandler((sender, message) =>
                {
                    var windowInstance = (PhotinoWindow)sender;
                    HandleWebMessage(windowInstance, dbService, message);
                });

                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "index.html");
                if (!File.Exists(htmlPath)) htmlPath = Path.GetFullPath("../../../wwwroot/index.html");

                window.Load(htmlPath);
                window.WaitForClose();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Критическая ошибка запуска: {ex.Message}");
            }
        }

        // --- ЛОГИКА ОБРАБОТКИ (ДЛЯ ТЕСТОВ) ---

        internal static void HandleWebMessage(PhotinoWindow window, DatabaseService db, string message)
        {
            try
            {
                var request = JsonSerializer.Deserialize<Payload>(message);
                if (request == null) return;
                ExecuteCommand(window, db, request);
            }
            catch (Exception ex)
            {
                SendJson(window, new { success = false, error = $"Системная ошибка: {ex.Message}" });
            }
        }

        internal static void ExecuteCommand(PhotinoWindow window, DatabaseService db, Payload request)
        {
            switch (request.command)
            {
                case "GET_DATA":
                    var list = request.page == "asn" ? (object)db.GetAsnList() : db.GetIpLists();
                    SendJson(window, new { success = true, page = request.page, data = list });
                    break;

                case "ADD_ASN":
                    if (int.TryParse(request.value, out int asnId) && asnId >= 0 && asnId <= 65535)
                    {
                        if (db.AsnExists(asnId))
                            SendJson(window, new { success = false, error = "Этот ASN уже есть в базе." });
                        else
                            ProcessAsn(window, db, asnId);
                    }
                    else
                        SendJson(window, new { success = false, error = "Введите корректный AS (0-65535)" });
                    break;

                case "TOGGLE_BLACKLIST":
                    db.ToggleBlacklist(request.id, request.status);
                    SendJson(window, new { success = true, page = "asn", data = db.GetAsnList() });
                    break;

                case "DELETE_ASN":
                    db.DeleteAsn(request.id);
                    SendJson(window, new { success = true, page = "asn", data = db.GetAsnList() });
                    break;

                case "SELECT_FILE":
                    ShowFileDialog(window);
                    break;

                case "UPLOAD_IP":
                    ProcessIpUpload(window, db, request.value);
                    break;

                case "GET_NO_ASN":
                    SendJson(window, new { success = true, page = "no_asn", data = db.GetIpsWithoutAsn(request.id) });
                    break;

                case "GET_WITH_ASN":
                    SendJson(window, new { success = true, page = "with_asn", data = db.GetIpsWithAsn(request.id) });
                    break;

                case "GET_ASN_RANGES":
                    SendJson(window, new { success = true, page = "asn_ranges", data = db.GetAsnRanges(request.id) });
                    break;

                case "DELETE_IP_LIST":
                    db.DeleteIpList(request.id);
                    SendJson(window, new { success = true, page = "ip", data = db.GetIpLists() });
                    break;

                case "GET_GROUP_DETAILS":
                    var ips = db.GetFirst10IpsInGroup(request.id, request.value);
                    SendJson(window, new { success = true, command = "RENDER_GROUP_IPS", prefix = request.value, data = ips });
                    break;

                case "FIND_ASN_BY_IP":
                    ProcessFindAsn(window, db, request.value);
                    break;

                case "UNLINK_ASN_FROM_LIST":
                    db.UnlinkAsnFromList(request.id, request.asn_id);
                    SendJson(window, new { success = true, command = "REFRESH_AFTER_FIND" });
                    break;
            }
        }

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ---

        private static void SendJson(PhotinoWindow window, object obj)
            => window?.SendWebMessage(JsonSerializer.Serialize(obj));

        private static void ProcessAsn(PhotinoWindow window, DatabaseService db, int asnId)
        {
            Task.Run(async () => {
                try
                {
                    var bgp = new BgpService();
                    var info = await bgp.GetAsnFullInfo(asnId);
                    db.SaveAsn(asnId, info.name, info.country, info.ranges ?? new List<(uint, uint)>());

                    if (info.ranges != null && info.ranges.Count > 0)
                        db.RebindIpsToAsn(asnId);

                    string msg = (info.ranges == null || info.ranges.Count == 0)
                        ? $"AS{asnId} добавлен без IPv4 префиксов."
                        : $"AS{asnId} успешно добавлен.";

                    SendJson(window, new { success = true, page = "asn", data = db.GetAsnList(), message = msg });
                }
                catch (Exception ex)
                {
                    SendJson(window, new { success = false, error = $"Ошибка при загрузке AS{asnId}: {ex.Message}" });
                }
            });
        }

        private static void ProcessIpUpload(PhotinoWindow window, DatabaseService db, string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    throw new Exception("Файл не выбран.");

                var ipList = new HashSet<uint>();
                int lineNo = 0;

                foreach (var raw in File.ReadLines(path))
                {
                    lineNo++;
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    if (!IPAddress.TryParse(line, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
                        throw new Exception($"Некорректный IPv4 в строке {lineNo}: {line}");

                    var bytes = ip.GetAddressBytes();
                    uint ipNum = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
                    ipList.Add(ipNum);
                }

                db.ImportIpList(Path.GetFileName(path), ipList.ToList());
                SendJson(window, new { success = true, page = "ip", data = db.GetIpLists() });
            }
            catch (Exception ex)
            {
                SendJson(window, new { success = false, error = ex.Message });
            }
        }

        private static void ProcessFindAsn(PhotinoWindow window, DatabaseService db, string ipStr)
        {
            Task.Run(async () => {
                try
                {
                    var bgp = new BgpService();
                    int foundAsn = await bgp.GetAsnByIpWhois(ipStr);

                    if (foundAsn <= 0) throw new Exception("ASN не определен.");

                    if (!db.AsnExists(foundAsn))
                    {
                        var info = await bgp.GetAsnFullInfo(foundAsn);
                        db.SaveAsn(foundAsn, info.name, info.country, info.ranges);
                    }

                    db.RebindIpsToAsn(foundAsn);
                    SendJson(window, new { success = true, command = "REFRESH_AFTER_FIND", message = $"IP сопоставлен с AS{foundAsn}" });
                }
                catch (Exception ex) { SendJson(window, new { success = false, error = ex.Message }); }
            });
        }

        private static void ShowFileDialog(PhotinoWindow window)
        {
            var thread = new Thread(() => {
                using var dialog = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt", Title = "Выберите список IP" };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    SendJson(window, new { command = "FILE_SELECTED", path = dialog.FileName });
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public class Payload
        {
            public string command { get; set; } = "";
            public string page { get; set; } = "";
            public string value { get; set; } = "";
            public int id { get; set; }
            public int asn_id { get; set; }
            public bool status { get; set; }
        }
    }
}