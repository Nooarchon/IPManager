using Xunit;
using Moq;
using IPManager;
using System.Collections.Generic;
using System.Linq;

namespace IPManager.Tests
{
    // Your existing Unit Tests
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

            // Use It.IsAny for the entire list, without specifying types within the tuple if Moq is being picky
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

    // NEW CLASS: IP Binding Logic Tests (Integration Tests)
    public class IPBindingTests
    {
        [Fact]
        public void ImportIpList_ShouldAssignCorrectAsn_WhenIpIsInRange()
        {
            // 1. Arrange: Create a real DB service (it will create the ip_manager.db file in the tests folder)
            var db = new DatabaseService();

            int testAsnId = 136907;
            // Range 203.0.113.0 - 203.0.113.255 (in numbers 3405902080 - 3405902335)
            var ranges = new List<(uint start, uint end)> { (3405902080, 3405902335) };

            db.SaveAsn(testAsnId, "Test Net", "US", ranges);

            // IP: 203.0.113.50 (number 3405902130)
            var importedIps = new List<uint> { 3405902130 };

            // 2. Act: Load IP
            db.ImportIpList("integration_test.txt", importedIps);

            // 3. Assert: We check that our address appears in the "IP with ASN" list
            var results = db.GetIpsWithAsn(GetLastListId(db));

            Assert.Contains(results, (dynamic r) => r.asn == testAsnId);
        }

        // Helper method to find the ID of the last loaded list
        private int GetLastListId(DatabaseService db)
        {
            var lists = db.GetIpLists();
            if (lists.Count == 0) return 0;
            // Take the ID of the very first (top) list
            return (int)((dynamic)lists[0]).id;
        }
    }
}