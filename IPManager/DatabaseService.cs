using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IPManager
{
    public class DatabaseService
    {
        private readonly string _connectionString = "Data Source=ip_manager.db";

        public DatabaseService()
        {
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var pragmaCmd = new SqliteCommand("PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;", connection);
            pragmaCmd.ExecuteNonQuery();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS asn (
                    asn_id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    country_id TEXT, 
                    blacklisted INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS asn_ip_range (
                    asn_id INTEGER NOT NULL,
                    range_start INTEGER NOT NULL,
                    range_end INTEGER NOT NULL,
                    PRIMARY KEY (asn_id, range_start),
                    FOREIGN KEY (asn_id) REFERENCES asn(asn_id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_range_lookup ON asn_ip_range (range_start, range_end);

                CREATE TABLE IF NOT EXISTS ip_list (
                    iplist_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    created_at TEXT NOT NULL DEFAULT (datetime('now'))
                );

                CREATE TABLE IF NOT EXISTS ip_list_row (
                    iplist_id INTEGER NOT NULL,
                    ip INTEGER NOT NULL,
                    asn_id INTEGER,
                    PRIMARY KEY (iplist_id, ip),
                    FOREIGN KEY (iplist_id) REFERENCES ip_list(iplist_id) ON DELETE CASCADE,
                    FOREIGN KEY (asn_id) REFERENCES asn(asn_id) ON DELETE SET NULL
                );
                
                CREATE INDEX IF NOT EXISTS idx_ip_row_asn ON ip_list_row (asn_id);";
            cmd.ExecuteNonQuery();
        }

        // --- CONNECTION (USED IN Program.cs) ---

        public virtual void RebindIpsToAsn(int asnId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var ranges = new List<(long start, long end)>();
            using (var cmd = new SqliteCommand("SELECT range_start, range_end FROM asn_ip_range WHERE asn_id = @id", connection))
            {
                cmd.Parameters.AddWithValue("@id", asnId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) ranges.Add((reader.GetInt64(0), reader.GetInt64(1)));
            }

            using var tx = connection.BeginTransaction();
            try
            {
                foreach (var range in ranges)
                {
                    var sql = "UPDATE ip_list_row SET asn_id = @asn WHERE asn_id IS NULL AND ip BETWEEN @s AND @e";
                    using var cmd = new SqliteCommand(sql, connection, tx);
                    cmd.Parameters.AddWithValue("@asn", asnId);
                    cmd.Parameters.AddWithValue("@s", range.start);
                    cmd.Parameters.AddWithValue("@e", range.end);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        public virtual void UnlinkAsnFromList(int iplistId, int asnId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var sql = "UPDATE ip_list_row SET asn_id = NULL WHERE iplist_id = @lid AND asn_id = @aid";
            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@lid", iplistId);
            cmd.Parameters.AddWithValue("@aid", asnId);
            cmd.ExecuteNonQuery();
        }

        // --- WORK WITH  ASN ---

        public virtual void SaveAsn(int id, string name, string country, List<(uint start, uint end)> ranges)
        {
            if (id <= 0)
                throw new ArgumentException("ASN must be a positive number.");

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO asn (asn_id, name, country_id) 
                    VALUES (@id, @name, @country)
                    ON CONFLICT(asn_id) DO UPDATE SET 
                        name = excluded.name, 
                        country_id = excluded.country_id";

                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@name", name ?? "");
                cmd.Parameters.AddWithValue("@country", country ?? "??");
                cmd.ExecuteNonQuery();

                var delCmd = connection.CreateCommand();
                delCmd.CommandText = "DELETE FROM asn_ip_range WHERE asn_id = @id";
                delCmd.Parameters.AddWithValue("@id", id);
                delCmd.ExecuteNonQuery();

                var rangeCmd = connection.CreateCommand();
                rangeCmd.CommandText = "INSERT OR IGNORE INTO asn_ip_range (asn_id, range_start, range_end) VALUES (@id, @start, @end)";
                var pStart = rangeCmd.Parameters.Add("@start", SqliteType.Integer);
                var pEnd = rangeCmd.Parameters.Add("@end", SqliteType.Integer);
                rangeCmd.Parameters.AddWithValue("@id", id);

                foreach (var range in ranges)
                {
                    pStart.Value = (long)range.start;
                    pEnd.Value = (long)range.end;
                    rangeCmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public virtual List<object> GetAsnList()
        {
            var result = new List<object>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            string sql = @"
                SELECT a.asn_id, a.name, a.country_id, a.blacklisted,
                (SELECT COUNT(*) FROM ip_list_row r WHERE r.asn_id = a.asn_id) as ip_count
                FROM asn a ORDER BY a.asn_id";

            using var cmd = new SqliteCommand(sql, connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new
                {
                    id = reader.GetInt32(0),
                    name = reader.GetString(1),
                    country = reader.IsDBNull(2) ? "-" : reader.GetString(2),
                    blacklisted = reader.GetInt32(3) == 1,
                    ip_count = reader.GetInt32(4)
                });
            }
            return result;
        }

        // --- IMPORT AND VIEW LISTS ---

        public virtual void ImportIpList(string fileName, List<uint> ips)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            try
            {
                var listCmd = new SqliteCommand("INSERT INTO ip_list (name) VALUES (@n); SELECT last_insert_rowid();", connection, tx);
                listCmd.Parameters.AddWithValue("@n", fileName);
                long listId = (long)listCmd.ExecuteScalar();

                using var ins = new SqliteCommand("INSERT OR IGNORE INTO ip_list_row (iplist_id, ip) VALUES (@l, @ip)", connection, tx);
                foreach (var ip in ips)
                {
                    ins.Parameters.Clear();
                    ins.Parameters.AddWithValue("@l", listId);
                    ins.Parameters.AddWithValue("@ip", (long)ip);
                    ins.ExecuteNonQuery();
                }

                var updateSql = @"
                    UPDATE ip_list_row 
                    SET asn_id = (
                        SELECT r.asn_id FROM asn_ip_range r 
                        WHERE ip_list_row.ip BETWEEN r.range_start AND r.range_end LIMIT 1
                    )
                    WHERE iplist_id = @l";

                using var upCmd = new SqliteCommand(updateSql, connection, tx);
                upCmd.Parameters.AddWithValue("@l", listId);
                upCmd.ExecuteNonQuery();

                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        public virtual List<object> GetIpsWithoutAsn(int iplistId)
        {
            var result = new List<object>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var sql = @"
                SELECT (ip / 65536) as prefix_val, COUNT(*) 
                FROM ip_list_row 
                WHERE iplist_id = @id AND asn_id IS NULL 
                GROUP BY prefix_val";

            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@id", iplistId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                long pVal = reader.GetInt64(0);
                string prefixStr = $"{(pVal >> 8) & 0xFF}.{pVal & 0xFF}.x.x";
                result.Add(new { prefix = prefixStr, count = reader.GetInt32(1) });
            }
            return result;
        }

        public virtual List<object> GetIpsWithAsn(int iplistId)
        {
            var result = new List<object>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var sql = @"
                SELECT a.asn_id, a.name, COUNT(r.ip) 
                FROM ip_list_row r 
                JOIN asn a ON r.asn_id = a.asn_id 
                WHERE r.iplist_id = @id 
                GROUP BY a.asn_id";
            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@id", iplistId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new { asn = reader.GetInt32(0), name = reader.GetString(1), count = reader.GetInt32(2) });
            }
            return result;
        }

        public virtual List<string> GetFirst10IpsInGroup(int iplistId, string prefixStr)
        {
            var parts = prefixStr.Replace(".x.x", "").Split('.');
            long start = (long.Parse(parts[0]) << 24) | (long.Parse(parts[1]) << 16);
            long end = start | 0xFFFF;

            var result = new List<string>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var cmd = new SqliteCommand("SELECT ip FROM ip_list_row WHERE iplist_id = @id AND ip BETWEEN @s AND @e LIMIT 10", connection);
            cmd.Parameters.AddWithValue("@id", iplistId);
            cmd.Parameters.AddWithValue("@s", start);
            cmd.Parameters.AddWithValue("@e", end);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(UintToIp((uint)reader.GetInt64(0)));
            return result;
        }

        public virtual bool AsnExists(int asnId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var cmd = new SqliteCommand("SELECT 1 FROM asn WHERE asn_id = @id", connection);
            cmd.Parameters.AddWithValue("@id", asnId);
            return cmd.ExecuteScalar() != null;
        }

        public virtual void DeleteAsn(int asnId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var cmd = new SqliteCommand("DELETE FROM asn WHERE asn_id = @id", connection);
            cmd.Parameters.AddWithValue("@id", asnId);
            cmd.ExecuteNonQuery();
        }

        public virtual void ToggleBlacklist(int asnId, bool status)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var cmd = new SqliteCommand("UPDATE asn SET blacklisted = @s WHERE asn_id = @id", connection);
            cmd.Parameters.AddWithValue("@s", status ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", asnId);
            cmd.ExecuteNonQuery();
        }

        public virtual List<object> GetIpLists()
        {
            var result = new List<object>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var cmd = new SqliteCommand("SELECT iplist_id, name, created_at FROM ip_list ORDER BY iplist_id DESC", connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new { id = reader.GetInt32(0), name = reader.GetString(1), date = reader.GetString(2) });
            }
            return result;
        }

        public virtual void DeleteIpList(int iplistId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var cmd = new SqliteCommand("DELETE FROM ip_list WHERE iplist_id = @id", connection);
            cmd.Parameters.AddWithValue("@id", iplistId);
            cmd.ExecuteNonQuery();
        }

        public virtual object GetAsnRanges(int asnId)
        {
            var list = new List<object>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = new SqliteCommand("SELECT range_start, range_end FROM asn_ip_range WHERE asn_id = @id", conn);
            cmd.Parameters.AddWithValue("@id", asnId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new
                {
                    start = UintToIp((uint)reader.GetInt64(0)),
                    end = UintToIp((uint)reader.GetInt64(1))
                });
            }
            return list;
        }

        private string UintToIp(uint ip)
        {
            byte[] bytes = BitConverter.GetBytes(ip);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return new System.Net.IPAddress(bytes).ToString();
        }
    }
}