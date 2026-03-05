using System.Net;

namespace IPManager

{
    // Helper class for working with IP
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
            if (string.IsNullOrWhiteSpace(cidr))
                throw new ArgumentException("CIDR cannot be empty");

            string[] parts = cidr.Split('/');

            // CHECK: If there is no '/' or the format is incorrect
            if (parts.Length != 2)
                throw new FormatException("Invalid CIDR format. Expected IP/Prefix (e.g., 192.168.1.0/24)");

            uint ip = ToUint(parts[0]);

            // CHECK: Is the mask a number and in the range 0-32
            if (!int.TryParse(parts[1], out int maskLength) || maskLength < 0 || maskLength > 32)
                throw new ArgumentOutOfRangeException(nameof(cidr), "Mask length must be between 0 and 32");

            uint mask = maskLength == 0 ? 0 : 0xffffffff << (32 - maskLength);
            uint start = ip & mask;
            uint end = start | ~mask;

            return (start, end);
        }

    }

}