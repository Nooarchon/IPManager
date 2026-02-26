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
            int myAsn = 134806;

            // Очистка перед тестом (если метод реализован)
            if (db.AsnExists(myAsn)) db.DeleteAsn(myAsn);

            db.SaveAsn(myAsn, "Test Provider LLC", "US", new() { (16777216, 16777471) });

            var ips = new List<uint> { 16777217 };
            db.ImportIpList("Test_List", ips);

            // ВМЕСТО GetIpsWithAsn(1) ИСПОЛЬЗУЙТЕ ДИНАМИЧЕСКИЙ ID:
            var lastId = db.GetIpLists().Cast<dynamic>().First().id;
            var results = db.GetIpsWithAsn((int)lastId);

            Assert.NotEmpty(results);
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