using Xunit;
using Moq;
using IPManager;
using System.Collections.Generic;
using System.Linq;

namespace IPManager.Tests
{
    // Ваши существующие тесты команд (Unit Tests)
    public class UnitTest1
    {
        [Fact]
        public void ExecuteCommand_DeleteAsn_ShouldCallDatabaseDelete()
        {
            var dbMock = new Mock<DatabaseService>();
            var payload = new Program.Payload { command = "DELETE_ASN", id = 99 };
            Program.ExecuteCommand(null, dbMock.Object, payload);
            dbMock.Verify(db => db.DeleteAsn(99), Times.Once);
        }

        [Theory]
        [InlineData("70000")]
        [InlineData("-1")]
        [InlineData("abc")]
        public void ExecuteCommand_AddAsn_InvalidValue_ShouldNotProcess(string invalidAsn)
        {
            var dbMock = new Mock<DatabaseService>();
            var payload = new Program.Payload { command = "ADD_ASN", value = invalidAsn };

            Program.ExecuteCommand(null, dbMock.Object, payload);

            // Используем It.IsAny для всего списка, не уточняя типы внутри кортежа, если Moq капризничает
            dbMock.Verify(db => db.SaveAsn(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<(uint, uint)>>()),
                Times.Never);
        }

        [Fact]
        public void ExecuteCommand_ToggleBlacklist_ShouldUpdateStatus()
        {
            var dbMock = new Mock<DatabaseService>();
            var payload = new Program.Payload { command = "TOGGLE_BLACKLIST", id = 1, status = true };
            Program.ExecuteCommand(null, dbMock.Object, payload);
            dbMock.Verify(db => db.ToggleBlacklist(1, true), Times.Once);
        }
    }

    // НОВЫЙ КЛАСС: Тесты логики привязки IP (Integration Tests)
    public class IPBindingTests
    {
        [Fact]
        public void ImportIpList_ShouldAssignCorrectAsn_WhenIpIsInRange()
        {
            // 1. Arrange: Создаем реальную службу БД (она создаст файл ip_manager.db в папке тестов)
            var db = new DatabaseService();

            int testAsnId = 136907;
            // Диапазон 203.0.113.0 - 203.0.113.255 (в числах 3405902080 - 3405902335)
            var ranges = new List<(uint start, uint end)> { (3405902080, 3405902335) };

            db.SaveAsn(testAsnId, "Test Net", "US", ranges);

            // IP: 203.0.113.50 (число 3405902130)
            var importedIps = new List<uint> { 3405902130 };

            // 2. Act: Загружаем IP
            db.ImportIpList("integration_test.txt", importedIps);

            // 3. Assert: Проверяем, что в списке "IP с ASN" появился наш адрес
            var results = db.GetIpsWithAsn(GetLastListId(db));

            Assert.Contains(results, (dynamic r) => r.asn == testAsnId);
        }

        // Вспомогательный метод, чтобы найти ID последнего загруженного списка
        private int GetLastListId(DatabaseService db)
        {
            var lists = db.GetIpLists();
            if (lists.Count == 0) return 0;
            // Берем ID самого первого (верхнего) списка
            return (int)((dynamic)lists[0]).id;
        }
    }
}