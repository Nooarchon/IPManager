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

            // Cleanup before test (if method is implemented)
            if (db.AsnExists(myAsn)) db.DeleteAsn(myAsn);

            db.SaveAsn(myAsn, "Test Provider LLC", "US", new() { (16777216, 16777471) });

            var ips = new List<uint> { 16777217 };
            db.ImportIpList("Test_List", ips);

            // INSTEAD OF GetIpsWithAsn(1) USE DYNAMIC ID:
            var lastId = db.GetIpLists().Cast<dynamic>().First().id;
            var results = db.GetIpsWithAsn((int)lastId);

            Assert.NotEmpty(results);
        }

        [Theory]
        [InlineData(-100)] // Negative ASN
        [InlineData(0)] // Zero ASN
        public void SaveAsn_ShouldFail_OnInvalidNumbers(int invalidId)
        {
            var db = new DatabaseService();
            // Now the test will be green, since we added throw ArgumentException
            Assert.Throws<System.ArgumentException>(() =>
                db.SaveAsn(invalidId, "Error Name", "??", new List<(uint, uint)>())
            );
        }

        [Fact]
        public void AsnLifecycle_FullTest()
        {
            var db = new DatabaseService();
            int testAsn = 134806; // One of your numbers

            // 1. Test: Add (correct number)
            db.SaveAsn(testAsn, "Test Provider", "US", new() { (16777216, 16777471) });
            Assert.True(db.AsnExists(testAsn));

            // 2. Test: Blacklist (Enable/Disable)
            db.ToggleBlacklist(testAsn, true);
            // Check via GetAsnList (need to be dynamic or create a model)
            var asnStatus = db.GetAsnList().Cast<dynamic>().First(x => x.id == testAsn);
            Assert.True(asnStatus.blacklisted);

            // 3. Test: Invalid number
            Assert.Throws<ArgumentException>(() => db.SaveAsn(-5, "Bad", "??", new()));

            // 4. Test: Deletion
            db.DeleteAsn(testAsn);
            Assert.False(db.AsnExists(testAsn));
        }
    }
}