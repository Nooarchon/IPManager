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
        [InlineData("not-an-ip")]       // Вообще не IP
        [InlineData("256.0.0.1")]       // Некорректное число
        [InlineData(null)]              // Null
        public void ToUint_InvalidIp_ThrowsException(string invalidIp)
        {
            // Проверяем, что метод выбрасывает ошибку при плохом IP
            Assert.ThrowsAny<Exception>(() => IpHelper.ToUint(invalidIp));
        }

        [Theory]
        [InlineData("192.168.1.0/abc")] // Маска буквами
        [InlineData("192.168.1.0/33")]  // Маска слишком большая
        [InlineData("192.168.1.0")]     // Нет слеша и маски
        public void ParseCIDR_InvalidFormat_ThrowsException(string invalidCidr)
        {
            // Проверяем, что при неверном CIDR метод падает
            Assert.ThrowsAny<Exception>(() => IpHelper.ParseCIDR(invalidCidr));
        }
    }
}
