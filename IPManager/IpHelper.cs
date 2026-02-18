using System.Net;



namespace IPManager

{

    // Вспомогательный класс для работы с IP

    public static class IpHelper
    {

        public static uint ToUint(string ipAddress)
        {

            var address = System.Net.IPAddress.Parse(ipAddress);

            byte[] bytes = address.GetAddressBytes();

            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);

            return BitConverter.ToUInt32(bytes, 0);

        }



        public static (uint start, uint end) ParseCIDR(string cidr)
        {

            string[] parts = cidr.Split('/');

            uint ip = ToUint(parts[0]);

            int maskLength = int.Parse(parts[1]);

            uint mask = maskLength == 0 ? 0 : 0xffffffff << (32 - maskLength);

            uint start = ip & mask;

            uint end = start | ~mask;

            return (start, end);

        }

    }

}