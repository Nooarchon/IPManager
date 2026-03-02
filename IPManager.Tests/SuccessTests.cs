using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using IPManager;

namespace IPManager.Tests
{
    public class SuccessTests
    {
        [Fact]
        public void ToUint_ValidIp_ReturnsCorrectLongValue()
        {
            // Check that 192.168.0.1 is converted correctly
            string ip = "192.168.0.1";
            uint expected = 0xC0A80001; // This is 192.168.0.1 in hexadecimal

            uint result = IpHelper.ToUint(ip);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ParseCIDR_ValidNetwork_ReturnsCorrectRange()
        {
            // Checking the network 192.168.1.0/24 (mask 255.255.255.0)
            string cidr = "192.168.1.0/24";

            var (start, end) = IpHelper.ParseCIDR(cidr);

            // Start: 192.168.1.0, End: 192.168.1.255
            Assert.Equal(IpHelper.ToUint("192.168.1.0"), start);
            Assert.Equal(IpHelper.ToUint("192.168.1.255"), end);
        }
    }
}