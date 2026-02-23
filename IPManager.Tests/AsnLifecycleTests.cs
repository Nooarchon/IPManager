using Xunit;
using IPManager;
using System.Collections.Generic;
using System.Linq;

namespace IPManager.Tests
{
    public class AsnLifecycleTests
    {
        [Fact]
        public void FullProcess_WithRealAsn_ShouldWork()
        {
            var db = new DatabaseService();
            // Используем один из ваших номеров для теста
            int myAsn = 134806;
            string myName = "Test Provider LLC";

            // 1. ПРОВЕРКА ДОБАВЛЕНИЯ
            var ranges = new List<(uint start, uint end)>
            {
                (16777216, 16777471) // 1.0.0.0 - 1.0.0.255
            };
            db.SaveAsn(myAsn, myName, "US", ranges);
            Assert.True(db.AsnExists(myAsn));

            // 2. ПРОВЕРКА IP ЧИСЕЛ (ПРИВЯЗКА)
            // Добавляем IP из этого диапазона
            var ips = new List<uint> { 16777217 }; // 1.0.0.1
            db.ImportIpList("Test_List", ips);

            // Проверяем, что IP нашел своего владельца (ASN)
            var results = db.GetIpsWithAsn(1); // Список ID = 1
            Assert.NotEmpty(results);

            // 3. ПРОВЕРКА БЛЭКЛИСТА
            db.ToggleBlacklist(myAsn, true);
            var asnInfo = (dynamic)db.GetAsnList().First(x => (int)((dynamic)x).id == myAsn);
            Assert.True(asnInfo.blacklisted);

            // 4. ПРОВЕРКА УДАЛЕНИЯ
            db.DeleteAsn(myAsn);
            Assert.False(db.AsnExists(myAsn));
        }

        [Theory]
        [InlineData(-100)] // Отрицательный ASN
        [InlineData(0)]    // Нулевой ASN
        public void SaveAsn_ShouldFail_OnInvalidNumbers(int invalidId)
        {
            var db = new DatabaseService();
            // Теперь тест будет зеленым, так как мы добавили throw ArgumentException
            Assert.Throws<System.ArgumentException>(() =>
                db.SaveAsn(invalidId, "Error Name", "??", new List<(uint, uint)>())
            );
        }

        [Fact]
        public void AsnLifecycle_FullTest()
        {
            var db = new DatabaseService();
            int testAsn = 134806; // Один из ваших номеров

            // 1. Тест: Добавление (правильное число)
            db.SaveAsn(testAsn, "Test Provider", "US", new() { (16777216, 16777471) });
            Assert.True(db.AsnExists(testAsn));

            // 2. Тест: Блэклист (Включить/Выключить)
            db.ToggleBlacklist(testAsn, true);
            // Проверка через GetAsnList (нужно привести к динамике или создать модель)
            var asnStatus = db.GetAsnList().Cast<dynamic>().First(x => x.id == testAsn);
            Assert.True(asnStatus.blacklisted);

            // 3. Тест: Неправильное число
            Assert.Throws<ArgumentException>(() => db.SaveAsn(-5, "Bad", "??", new()));

            // 4. Тест: Удаление
            db.DeleteAsn(testAsn);
            Assert.False(db.AsnExists(testAsn));
        }
    }
}