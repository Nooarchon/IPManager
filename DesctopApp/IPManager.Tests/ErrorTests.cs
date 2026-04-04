using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using IPManager;
using System;

namespace IPManager.Tests
{
    public class ErrorTests
    {
        [Theory]
        [InlineData("not-an-ip")] // Not an IP at all
        [InlineData("256.0.0.1")] // Invalid number
        [InlineData(null)] // Null
        public void ToUint_InvalidIp_ThrowsException(string invalidIp)
        {
            // Check that the method throws an error if the IP is invalid
            Assert.ThrowsAny<Exception>(() => IpHelper.ToUint(invalidIp));
        }

        [Theory]
        [InlineData("192.168.1.0/abc")] // Alphabetical mask
        [InlineData("192.168.1.0/33")] // Mask is too large
        [InlineData("192.168.1.0")] // Missing slash and mask
        public void ParseCIDR_InvalidFormat_ThrowsException(string invalidCidr)
        {
            // Check that the method fails with an invalid CIDR
            Assert.ThrowsAny<Exception>(() => IpHelper.ParseCIDR(invalidCidr));
        }
    }
}
